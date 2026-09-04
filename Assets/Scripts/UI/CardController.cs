using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Zenject;

[RequireComponent(typeof(Image), typeof(RectTransform))]
public class CardController : MonoBehaviour, ICardView, IPointerEnterHandler, IPointerExitHandler,
    IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Inject] private IMapDataService _mapDataService;
    [Inject] private ICardDropHandler _dropHandler;
    [Inject] private IUIConfigProvider _uiConfig;
    [Inject] private GameLoop _gameLoop;
    [Inject] private LazyInject<PlayerInputHandler> _playerInputHandler;
    [Inject(Optional = true)] private IMapRaycastService _mapRaycastService;
    [Inject] private GoldWallet _goldWallet;

    /// <summary>金币不足时切换为灰版卡面（NormalCardConfigSO.grayCardSprite）。</summary>
    // 原「压暗」方案（FeelConfigProvider.UnaffordableCardDim）已废弃，改为切图。

    /// <summary>当前是否买得起（金币 >= 卡牌费用）。战术卡（无金币约束）恒为 true。</summary>
    private bool _isAffordable = true;

    /// <summary>各 Graphic 的原始颜色（含 alpha），用于拖拽淡出/还原。SetData 时采样。</summary>
    private readonly Dictionary<Graphic, Color> _graphicBaseColors = new Dictionary<Graphic, Color>();

    /// <summary>
    /// 拖拽期间的视觉通道（普通卡 = CardPresenter；战术卡不实现本接口，自动无模型预览）。
    /// 在 OnBeginDrag 时锁定一次，保证 Update/End 与 Begin 落在同一个 handler 上。
    /// </summary>
    private ICardDragVisualHandler _dragVisual;

    /// <summary>拖拽淡出倍率（§3 cardAlpha）。与原始 alpha 相乘后统一写入。</summary>
    private float _dragAlpha = 1f;

    /// <summary>本卡所在 Canvas 的参考高度；OnBeginDrag 缓存一次，避免逐帧 GetComponentInParent。</summary>
    private float _canvasHeight = UIScreenHelper.ReferenceHeight;

    /// <summary>逐帧缓存的进度，供 OnDrag 通知视觉通道时复用（避免重复计算）。</summary>
    private float _upwardDistance;
    private float _cardProgress;

    /// <summary>纵向增益倍率：落点纵向位移 = 触点纵向位移 × 本值。
    /// 1 = 恒等（落点跟手，即无遮挡缓解）。第一版恒定，不随行程变化。</summary>
    private const float CardDragLogicGainY = 1.5f;

    /// <summary>横向增益倍率：1 = 直通（落点横向跟手），对应效果文档 §4.4。</summary>
    private const float CardDragLogicGainX = 1.0f;

    /// <summary>本次拖拽的增益映射起点（屏幕像素）。OnBeginDrag 采样，ReleaseDragCapture 清除。
    /// 必须是静态的：PlayerInputHandler（轮询式）与 CardDragWorldPreviewController（只持 token）
    /// 都拿不到卡牌实例，却都要调 GetCardDragLogicPosition。</summary>
    private static Vector2 _dragOriginScreenPoint;
    private static bool _hasDragOrigin;

    /// <summary>
    /// 【落点图标与连线计划 §3.2】卡牌拖拽地图射线统一最大距离。
    /// 最大缩放下屏幕上缘射线到地面约 160 世界单位，显式传 300 保证全屏可用；
    /// 模型预览 / 高亮 / 落点判定三处共用，避免高缩放下高亮与落点先于模型“打不到”地面。
    /// </summary>
    public const float CardDragRaycastMaxDistance = 300f;

    /// <summary>返回用于地图射线的逻辑位置（屏幕像素坐标）。
    /// 以拖拽起点为原点，纵向位移放大 CardDragLogicGainY 倍，横向按 CardDragLogicGainX（1× 直通）。
    /// 起点未就绪时退化为恒等映射。</summary>
    public static Vector2 GetCardDragLogicPosition(Vector2 pointerPosition)
    {
        if (!_hasDragOrigin) return pointerPosition;

        Vector2 delta = pointerPosition - _dragOriginScreenPoint;
        return _dragOriginScreenPoint + new Vector2(delta.x * CardDragLogicGainX, delta.y * CardDragLogicGainY);
    }

    /// <summary>允许外部覆盖 drop handler（战术卡等非默认材质）。应在 Zenject 注入之后、首次拖拽之前调用。</summary>
    public void OverrideDropHandler(ICardDropHandler handler) => _dropHandler = handler;

    /// <summary>当前 drop handler（普通卡 = CardPresenter，战术卡 = TacticalCardPresenter）。</summary>
    public ICardDropHandler DropHandler => _dropHandler;

    /// <summary>设置拖拽代理（幽灵）：非空时 OnDragUpdate 移动/缩放代理而非本体。</summary>
    public void SetDragProxy(RectTransform proxy) => _dragProxy = proxy;

    private RectTransform _rectTransform;
    private Image _image;
    private RectTransform _dragProxy;

    public CardData _data;
    private Vector3 _originPosition;
    public Vector3 OriginPosition
    {
        get => _originPosition;
        set => _originPosition = value;
    }

    private bool _isDragging;
    private bool _isNextCard;
    private static CardController _activeDraggingCard;

    /// <summary>
    /// 【提起态】卡牌上移并停住的粘滞状态。同一时刻全局至多一张。
    /// 入口按输入设备分流（PointerEventData.pointerId：鼠标 &lt; 0，触摸 &gt;= 0）：
    ///   PC   → OnPointerEnter 进入 / OnPointerExit 退出；
    ///   移动 → OnPointerClick 进入 / PlayerInputHandler 轮询卡外点击退出。
    /// 两条入口汇入同一对 RaiseCard / LowerCard，视觉与状态完全一致。
    /// </summary>
    private bool _isRaised;
    private static CardController _activeRaisedCard;

    /// <summary>提起态上移量（Canvas 参考高度比例），与原悬浮上移保持一致。</summary>
    private const float RaiseOffsetRatio = 0.025f;

    /// <summary>提起态上移时长（秒）。上移与放缩共用同一时长，保证到顶时放缩恰好结束。</summary>
    private const float RaiseDuration = 0.2f;

    /// <summary>上升途中放缩幅度：放大目标 = 原始尺寸 × (1 + RaisePopScale)。</summary>
    private const float RaisePopScale = 0.10f;

    /// <summary>上升途中放缩补间的句柄（Sequence 亦为 Tween），用于定点清理（避免误杀入场/发牌缩放补间）。</summary>
    private Tween _raisePopTween;

    // ===== 提起态边缘流光（CardEdgeFlow.shader）=====

    /// <summary>流光总强度的材质属性 ID（_GlowMaster：0 = 全灭，1 = 全亮）。</summary>
    private static readonly int GlowMasterID = Shader.PropertyToID("_GlowMaster");

    /// <summary>本卡独占的材质实例。共享材质会让所有卡牌一起亮，必须每卡克隆一份。
    /// 注意：这里不能用 MaterialPropertyBlock —— UGUI 的 Graphic 走 CanvasRenderer 合批，
    /// 不读 MaterialPropertyBlock，属性不会生效。</summary>
    private Material _glowMaterialInstance;

    /// <summary>流光淡入淡出补间句柄，用于定点清理。</summary>
    private Tween _glowTween;

    /// <summary>流光淡入/淡出时长（秒）。淡入与提起上移同步，淡出略快，避免落下后余光滞留。</summary>
    private const float GlowFadeInDuration = 0.18f;
    private const float GlowFadeOutDuration = 0.12f;

    // ===== 金币不足·自顶向下压暗（CardEdgeFlow.shader 的 _DimFill）=====

    /// <summary>压暗覆盖比例的材质属性 ID（_DimFill：0 = 不压暗，1 = 整张压暗）。</summary>
    private static readonly int DimFillID = Shader.PropertyToID("_DimFill");

    /// <summary>压暗比例补间句柄，用于定点清理。</summary>
    private Tween _dimTween;

    /// <summary>压暗比例过渡时长（秒）。金币可能连续到账，取短值保证跟手。</summary>
    private const float DimFillDuration = 0.18f;

    public static bool IsAnyCardDragging => _activeDraggingCard != null;

    /// <summary>
    /// 当前处于提起态的卡牌；无则 null。供 PlayerInputHandler 轮询"点击卡外收起"。
    /// 注意：语义与 IsAnyCardDragging 独立，**不参与相机拖动屏蔽**——
    /// 提起后玩家仍需平移地图寻找落点。
    /// </summary>
    public static CardController ActiveRaisedCard => _activeRaisedCard;

    /// <summary>
    /// 当前拖拽中的卡牌视觉 RectTransform（战术卡为幽灵代理 _dragProxy，否则卡牌本体）；无拖拽时为 null。
    /// 【落点图标与连线计划 §5.2】供 CardDragLinkView 取连线下端点（卡牌顶边中点），
    /// 战术卡必须取幽灵卡而非原卡，否则连线指向留在原位的卡面。
    /// </summary>
    public static RectTransform ActiveDragVisualRect
    {
        get
        {
            CardController active = _activeDraggingCard;
            if (active == null) return null;
            return active._dragProxy != null ? active._dragProxy : active._rectTransform;
        }
    }


    private int originalSiblingIndex;  

    public int CardID => _data?.ID ?? -1;
    public CardData Data => _data;
    public int PlacementID { get; set; }
    public bool IsNextCard
    {
        get => _isNextCard;
        set
        {
            if (_isNextCard == value) return;
            _isNextCard = value;
            RefreshAffordability();
        }
    }
    public RectTransform RectTransform => _rectTransform;

    [Inject]
    private void Construct()
    {
        _rectTransform = GetComponent<RectTransform>();
        _image = GetComponent<Image>();
        originalSiblingIndex = transform.GetSiblingIndex();

        // Prefab 内透明 Button 的 RectTransform 远大于卡面，若参与射线会在卡外大范围触发悬浮。
        // 卡牌交互统一由根节点 Image + CardController 处理，子 Button 只保留视觉层。
        Button childButton = GetComponentInChildren<Button>(true);
        if (childButton != null && childButton.gameObject != gameObject)
        {
            childButton.interactable = false;
            if (childButton.targetGraphic != null)
                childButton.targetGraphic.raycastTarget = false;
        }

        //_image.alphaHitTestMinimumThreshold = 0.01f;

        // 流光初始必须是灭的。不能依赖材质资源里存的 _GlowMaster 值：
        // 那是共享资源，任何人在 Inspector 里拖动滑条预览都会被持久化进 .mat，
        // 导致开局所有卡牌常亮（直到被提起一次、由代码接管才恢复）。
        EnsureGlowMaterial();
        KillGlowImmediate();
    }

    public void SetData(CardData data, int placementID, Vector3 originPosition)
    {
        if (data == null)
        {
            Debug.LogError("[CardController] SetData: data is null!");
            return;
        }
        _data = data;
        PlacementID = placementID;
        _originPosition = originPosition;
        _image.sprite = data.CardSprite;

        // 使用价格显示：预制体第二个子物体（cost）的第一个子物体（Text (TMP)）
        if (transform.childCount > 1)
        {
            Transform costRoot = transform.GetChild(1);
            if (costRoot != null && costRoot.childCount > 0)
            {
                TextMeshProUGUI costText = costRoot.GetChild(0).GetComponent<TextMeshProUGUI>();
                if (costText != null) costText.text = data.CardCost.ToString();
            }
        }

        //_image.SetNativeSize();

        _image.color = Color.white;

        try { _image.alphaHitTestMinimumThreshold = 0.01f; } catch (System.Exception) { }

        // 在计算可负担性之前采样原始颜色，避免把预制体内隐形的大型 Button 显形。
        CaptureBaseColors();

        // 订阅金币变动以实时刷新“买不起 → 灰版卡面 + 禁用交互”状态。
        if (_goldWallet != null)
        {
            _goldWallet.OnGoldChanged -= OnGoldChanged;
            _goldWallet.OnGoldChanged += OnGoldChanged;
        }
        RefreshAffordability();
    }

    /// <summary>金币变动回调：刷新压暗进度；跨越“买得起/买不起”阈值时另做视觉与交互切换。</summary>
    private void OnGoldChanged(int _)
    {
        RefreshAffordability();
    }

    /// <summary>根据当前金币与卡牌费用刷新可用性；切换灰版卡面并阻断悬浮/拖拽。</summary>
    private void RefreshAffordability()
    {
        // 预告卡与战术卡（负数 ID / 无金币约束）不做灰化。
        if (_data == null || IsNextCard || _data.ID < 0)
        {
            SetAffordable(true);
            return;
        }

        bool affordable = _goldWallet == null || _goldWallet.Gold >= _data.CardCost;
        SetAffordable(affordable);
    }

    /// <summary>采样根 Image 及所有子级可见 Graphic 的原始颜色，作为拖拽淡出/还原的基准。</summary>
    private void CaptureBaseColors()
    {
        _graphicBaseColors.Clear();
        foreach (Graphic g in GetComponentsInChildren<Graphic>(true))
        {
            if (g == null) continue;
            // 预制体内隐藏着一块超大但完全透明的 Button Image（用于表现层），
            // 其 alpha=0；若纳入淡出集合会被显形为半透明白板，因此跳过完全透明的组件。
            if (g.color.a <= 0f) continue;
            _graphicBaseColors[g] = g.color;
        }
    }

    /// <summary>
    /// 刷新可负担状态。
    /// 【重要】本方法每次金币变动都会被调用（哪怕仍然买不起），因为压暗进度是连续量。
    /// 因此复位位置 / CancelDrag 这类**一次性副作用**必须只在跨越阈值时触发，
    /// 否则金币每跳一次就会重播一遍 0.2s 位移补间。
    /// </summary>
    private void SetAffordable(bool affordable)
    {
        bool crossed = _isAffordable != affordable;
        _isAffordable = affordable;

        ApplyCardFaceSprite();
        ApplyGraphicAlpha();
        // 压暗是连续量（随金币逐格下移），每次都要刷新，不受 crossed 限制。
        ApplyDimFill();

        // 买不起时停止接收射线（悬浮/拖拽）。幂等赋值，可每次执行。
        if (_image != null) _image.raycastTarget = affordable;

        if (!affordable && crossed)
        {
            // 悬浮中变为买不起时，UI 射线不再触发 OnPointerExit，需主动复位位置。
            if (!_isDragging)
            {
                // 提起态下金币掉到买不起：视觉在此统一回落，故只清标志不再播一次回落 tween，
                // 否则状态与视觉会脱节（标志仍是 Raised，PlayerInputHandler 会继续等一次卡外点击）。
                ReleaseRaiseCapture();
                StopRaisePopAndResetScale();
                // 只复位位置，不清除缩放 Tween：金币状态刷新不应打断发牌/入手的缩放动画。
                _rectTransform.DOAnchorPos(_originPosition, 0.2f);
            }
            if (_isDragging) CancelDrag();
        }
    }

    /// <summary>压暗覆盖比例的上限（金币比例=0 时的压暗值）。留 10% 不压暗，避免整卡全黑看不清卡面。</summary>
    private const float MaxDimFill = 0.9f;

    /// <summary>
    /// 非线性映射指数（&lt;1）。线性映射下，条带随金币接近卡费而匀速变薄，
    /// 最后一小段金币里条带薄到肉眼难以察觉，会让人误以为「提前解除了压暗」（灰图切换是二值，
    /// 只在攒满瞬间才切，二者观感脱节）。指数 &lt;1 把"薄片期"压缩到金币的最后一小段，
    /// 之前的大部分区间条带都保持足够厚度，两个通道的视觉节奏更贴近。
    /// </summary>
    private const float DimFillCurveExponent = 0.5f;

    /// <summary>
    /// 刷新压暗覆盖比例：_DimFill = (1 - 金币/卡费)^DimFillCurveExponent × MaxDimFill（截断到 [0, MaxDimFill]）。
    /// 自上而下解锁 —— 顶部先恢复原色，暗区留在底部并随金币增加而收缩。
    /// 买得起时恒为 0。叠加在灰版卡面之上，两者互不替代。
    /// 材质缺失（预制体未挂 CardEdgeFlow）时静默跳过，与流光一致。
    /// </summary>
    private void ApplyDimFill()
    {
        if (!EnsureGlowMaterial()) return;

        float target = 0f;
        if (!_isAffordable && _data != null && _data.CardCost > 0 && _goldWallet != null)
        {
            float remain = Mathf.Clamp01(1f - (float)_goldWallet.Gold / _data.CardCost);
            target = Mathf.Pow(remain, DimFillCurveExponent) * MaxDimFill;
        }

        _dimTween?.Kill();
        _dimTween = DOTween
            .To(() => _glowMaterialInstance.GetFloat(DimFillID),
                v => _glowMaterialInstance.SetFloat(DimFillID, v),
                target, DimFillDuration)
            .SetEase(Ease.OutQuad)
            .OnComplete(() => _dimTween = null);
    }

    /// <summary>
    /// 卡面精灵唯一写入口：买不起时切到灰版卡面，买得起时切回彩色卡面。
    /// 灰图未配置（null）则始终沿用彩色卡面，避免出现空白卡。
    /// </summary>
    private void ApplyCardFaceSprite()
    {
        if (_image == null || _data == null) return;

        Sprite target = (!_isAffordable && _data.GrayCardSprite != null)
            ? _data.GrayCardSprite
            : _data.CardSprite;

        if (target != null && _image.sprite != target) _image.sprite = target;
    }

    /// <summary>
    /// alpha 唯一写入口：最终 alpha = 原始 alpha × 拖拽淡出倍率。
    /// 「买不起」已改为切换灰版卡面（见 ApplyCardFaceSprite），不再参与 alpha 合成。
    /// </summary>
    private void ApplyGraphicAlpha()
    {
        float mul = _dragAlpha;

        foreach (var kv in _graphicBaseColors)
        {
            if (kv.Key == null) continue;
            Color c = kv.Value;
            c.a *= mul;
            kv.Key.color = c;
        }
    }

    public void PlayDealAnimation(Vector3 targetPosition, System.Action onComplete, bool isNextCard = false)
    {
        if (isNextCard)
        {
            // 预告卡弹出动画
            // 初始状态：稍小 + 稍低位置
            // B3: 竖屏适配 —— 偏移量改为屏幕高度的比例，替代横屏硬编码值
            _rectTransform.localScale = _uiConfig.NextCardSize * 0.65f;
            Vector3 startPos = targetPosition + new Vector3(0, UIScreenHelper.ReferenceHeight * -0.015f, 0);
            _rectTransform.anchoredPosition = startPos;

            // 0.6s 带回弹的弹出
            // IsTweening 由补间自身解锁：本分支的 onComplete 是同步调用（预告卡无需等待），
            // 若把解锁挂在 onComplete 上会立刻置回 false，锁形同虚设。
            IsTweening = true;
            _rectTransform.DOAnchorPos(targetPosition, 0.6f).SetEase(Ease.OutBack, 1.2f)
                .OnComplete(() => IsTweening = false);
            _rectTransform.DOScale(_uiConfig.NextCardSize, 0.6f).SetEase(Ease.OutBack, 1.2f);

            onComplete?.Invoke(); // 预告卡无需等待
            return;
        }

        // 普通手牌发牌逻辑（保持不变）
        IsTweening = true;
        _rectTransform.DOAnchorPos(targetPosition, 0.4f).OnComplete(() =>
        {
            IsTweening = false;
            onComplete?.Invoke();
        });
        _rectTransform.DOScale(_uiConfig.CardSize, 0.4f);
    }

    public void OnDragUpdate(Vector2 localPoint, Vector2 originPos)
    {
        RectTransform target = _dragProxy != null ? _dragProxy : _rectTransform;
        target.anchoredPosition = localPoint;

        // 只有向上拖才推进进度；向下拖进度按 0 处理（§0.5），不取绝对值。
        _upwardDistance = Mathf.Max(0f, localPoint.y - originPos.y);

        if (!SupportsModelPreview)
        {
            // 战术卡/幽灵卡：维持原有单阶段缩放手感（minScale 0.6，双向生效）。
            float legacyDistance = Mathf.Abs(localPoint.y - originPos.y);
            float legacyMax = _canvasHeight * 0.37f;
            float legacyRatio = Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(legacyDistance / legacyMax));
            target.localScale = _uiConfig.CardSize * legacyRatio;

            _cardProgress = 0f;
            return;
        }

        // 单阶段（§六）：模型在拎起瞬间即出现，卡牌自身按阶段一比例缩放并淡出。
        float d1 = Mathf.Max(1f, _canvasHeight * FeelConfigProvider.CardDragStage1Ratio);

        _cardProgress = Mathf.Clamp01(_upwardDistance / d1);

        // 阶段一：卡牌 100% → CardMinScale，并在 CardFadeStart 之后淡出。
        float cardScale = Mathf.Lerp(1f, FeelConfigProvider.CardDragCardMinScale, _cardProgress);
        target.localScale = _uiConfig.CardSize * cardScale;

        float fadeStart = Mathf.Clamp01(FeelConfigProvider.CardDragCardFadeStart);
        float fadeSpan = Mathf.Max(0.0001f, 1f - fadeStart);
        _dragAlpha = 1f - Mathf.Clamp01((_cardProgress - fadeStart) / fadeSpan);
        ApplyGraphicAlpha();
    }

    /// <summary>是否走世界空间模型预览：仅普通卡（持有 NormalCardConfig）且非代理拖拽。</summary>
    private bool SupportsModelPreview => _dragProxy == null && _data != null && _data.NormalCardConfig != null;

    public void ResetToOrigin()
    {
        // DOKill 会跳过补间的 OnComplete，入场锁不会自行解开；
        // 拖拽取消回到原位时卡牌本就该恢复可交互，在此显式解锁。
        StopRaisePopAndResetScale();
        IsTweening = false;
        _rectTransform.DOKill();
        _rectTransform.DOAnchorPos(_originPosition, 0.2f);
        transform.DOScale(_uiConfig.CardSize, 0.2f);

        // 幽灵代理在拖拽期间承担了位移/缩放，取消时必须一并复位，否则下次借出时姿态残留。
        if (_dragProxy != null)
        {
            _dragProxy.DOKill();
            _dragProxy.localScale = _uiConfig.CardSize;
        }

        // 复位拖拽淡出因子并重新合成 alpha（买不起的灰版卡面由 ApplyCardFaceSprite 独立维持）。
        _dragAlpha = 1f;
        _upwardDistance = 0f;
        _cardProgress = 0f;
        ApplyGraphicAlpha();
    }

    public void ClearHighlights()
    {
        _playerInputHandler.Value.ClearCardDragHighlight();
    }

    [Header("调试")]
    [Tooltip("勾选后，PC 上也走移动端的单击路线（单击提起 / 单击别处落下），" +
             "鼠标悬浮不再提起。用于在编辑器里验证移动端交互，无需打包到真机。\n" +
             "改卡牌预制体上的本项即对全部卡牌生效。")]
    [SerializeField] private bool _forceClickModeOnPC;

    /// <summary>
    /// 本次事件是否走"单击路线"（移动端语义）。
    /// 触摸事件（pointerId >= 0）恒为 true；鼠标事件仅在调试开关打开时为 true。
    /// 提起态的进入/退出入口全部由本方法分流，保证两条路线互斥——
    /// 否则 PC 上会出现"移入提起、点一下又落下"的混乱状态。
    /// </summary>
    private bool UsesClickRoute(PointerEventData eventData)
    {
        if (eventData == null) return _forceClickModeOnPC;
        return eventData.pointerId >= 0 || _forceClickModeOnPC;
    }

    /// <summary>
    /// 提起态是否可进入。
    /// IsTweening：入场/升手牌补间期间上锁——RaiseCard 的 DOKill 会杀掉该 RectTransform 上
    /// 所有 tween，补间途中提起会让卡牌从半路被拽走。
    /// </summary>
    private bool CanRaise => !IsNextCard && !_isDragging && _isAffordable && !IsTweening;

    /// <summary>位移补间期间的交互锁，见 ICardView.IsTweening。</summary>
    public bool IsTweening { get; set; }

    /// <summary>
    /// 进入提起态。全局唯一：先让上一张落下。
    /// DOKill 是必需的——发牌/入手动画或上一次落下的 tween 可能仍在播，
    /// 不杀会与本次上移争抢 anchoredPosition。
    /// </summary>
    public void RaiseCard()
    {
        if (_isRaised) return;

        if (_activeRaisedCard != null && _activeRaisedCard != this)
            _activeRaisedCard.LowerCard();

        _isRaised = true;
        _activeRaisedCard = this;

        SetGlow(true);

        Vector3 baseScale = _uiConfig.CardSize;
        Vector3 popScale = baseScale * (1f + RaisePopScale);
        Vector3 raisePos = _originPosition + new Vector3(0, UIScreenHelper.ReferenceHeight * RaiseOffsetRatio, 0);

        _rectTransform.DOKill();

        // 先快后慢：OutCubic 减速上浮，全程 RaiseDuration。
        _rectTransform
            .DOAnchorPos(raisePos, RaiseDuration)
            .SetEase(Ease.OutCubic);

        // 上升途中放缩：100% → 110% → 100%，同样持续 RaiseDuration，与上移同起点、同终点，
        // 到顶时恰好放缩结束。前半放大、后半回落，各占一半时长。
        float half = RaiseDuration / 2f;
        _raisePopTween = DOTween.Sequence()
            .Append(_rectTransform.DOScale(popScale, half).SetEase(Ease.OutQuad))
            .Append(_rectTransform.DOScale(baseScale, half).SetEase(Ease.OutCubic))
            .OnComplete(() => _raisePopTween = null);
    }

    /// <summary>停止上升途中放缩并复位到原始尺寸（幂等）。所有结束提起态的路径统一调用。</summary>
    private void StopRaisePopAndResetScale()
    {
        if (_raisePopTween != null)
        {
            _raisePopTween.Kill();
            _raisePopTween = null;
        }
        if (_rectTransform != null && _uiConfig != null)
            _rectTransform.localScale = _uiConfig.CardSize;
    }

    /// <summary>
    /// 克隆本卡独占的材质实例（幂等）。在 Construct 中即调用一次，确保开局就把 _GlowMaster
    /// 与 _DimFill 压成 0 —— 共享材质资源里存的值不可信（见 Construct 内注释）。
    /// 预制体未挂流光材质（或挂的是别的 shader）时返回 false，调用方静默跳过。
    /// </summary>
    private bool EnsureGlowMaterial()
    {
        if (_glowMaterialInstance != null) return true;
        if (_image == null || _image.material == null) return false;

        // Image.material 默认返回内置的 Default UI Material，它没有 _GlowMaster；
        // 用属性存在与否判断预制体是否真的挂了流光材质，避免误克隆默认材质。
        if (!_image.material.HasProperty(GlowMasterID)) return false;

        _glowMaterialInstance = new Material(_image.material);
        _image.material = _glowMaterialInstance;
        _glowMaterialInstance.SetFloat(GlowMasterID, 0f);
        // 同理压掉 _DimFill：Inspector 里预览过滑条的脏值会让开局所有卡带着压暗。
        _glowMaterialInstance.SetFloat(DimFillID, 0f);
        return true;
    }

    /// <summary>点亮/熄灭提起态边缘流光（幂等）。材质缺失时静默跳过，不影响提起动效本身。</summary>
    private void SetGlow(bool on)
    {
        if (!EnsureGlowMaterial()) return;

        _glowTween?.Kill();

        float target = on ? 1f : 0f;
        float duration = on ? GlowFadeInDuration : GlowFadeOutDuration;

        _glowTween = DOTween
            .To(() => _glowMaterialInstance.GetFloat(GlowMasterID),
                v => _glowMaterialInstance.SetFloat(GlowMasterID, v),
                target, duration)
            .SetEase(on ? Ease.OutQuad : Ease.InQuad)
            .OnComplete(() => _glowTween = null);
    }

    /// <summary>立即熄灭流光并杀掉补间，不播淡出。用于失活/销毁等"来不及淡出"的路径。</summary>
    private void KillGlowImmediate()
    {
        _glowTween?.Kill();
        _glowTween = null;
        if (_glowMaterialInstance != null)
            _glowMaterialInstance.SetFloat(GlowMasterID, 0f);
    }

    /// <summary>立即清除压暗并杀掉补间，不播过渡。用于失活/销毁等"来不及过渡"的路径。</summary>
    private void KillDimImmediate()
    {
        _dimTween?.Kill();
        _dimTween = null;
        if (_glowMaterialInstance != null)
            _glowMaterialInstance.SetFloat(DimFillID, 0f);
    }

    /// <summary>退出提起态并回落原位。对外供 PlayerInputHandler 在"点击卡外"时调用。</summary>
    public void LowerCard()
    {
        if (!_isRaised) return;

        ReleaseRaiseCapture();
        StopRaisePopAndResetScale();

        _rectTransform.DOKill();
        _rectTransform.DOAnchorPos(_originPosition, 0.2f);
    }

    /// <summary>
    /// 只清提起态的标志与全局引用，不播回落动画。
    /// 用于"位置随后会被别的逻辑接管"的场景（进入拖拽 / 失活 / 买不起复位），
    /// 此时再播一次回落 tween 会与接管方争抢 anchoredPosition。
    /// </summary>
    private void ReleaseRaiseCapture()
    {
        _isRaised = false;
        if (_activeRaisedCard == this) _activeRaisedCard = null;
        // 挂在这里而非 LowerCard：拖拽/失活/买不起复位等路径只走本方法，
        // 不播回落动画，若挂在 LowerCard 上这些路径的流光会一直亮着。
        SetGlow(false);
    }

    /// <summary>
    /// PC 悬浮入口：鼠标移入即提起。
    /// 触摸设备上 EventSystem 会在手指按下时补发一次 Enter，必须挡掉，
    /// 否则移动端会退化为"按住才抬起"。调试开关打开时鼠标也走单击路线，此处同样不响应。
    /// </summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (UsesClickRoute(eventData)) return;
        if (!CanRaise) return;

        RaiseCard();
    }

    /// <summary>PC 悬浮入口：鼠标移出即落下。触摸补发的 Exit / 单击路线下同样不响应。</summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        if (UsesClickRoute(eventData)) return;
        if (_isDragging) return;

        LowerCard();
    }

    /// <summary>
    /// 单击入口：单击提起 / 再次单击落下（toggle）。移动端常规路径；
    /// PC 上仅在调试开关 _forceClickModeOnPC 打开时生效——否则 PC 的提起完全由
    /// Enter/Exit 驱动，指针仍在卡上时点击不应改变状态。
    /// 与拖拽天然互斥：位移超过 EventSystem.pixelDragThreshold 后只走 Drag 链，不再触发 Click。
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (!UsesClickRoute(eventData)) return;
        if (_isDragging) return;

        if (_isRaised) LowerCard();
        else if (CanRaise) RaiseCard();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (IsNextCard || !_isAffordable)
        {
            eventData.pointerDrag = null;
            return;
        }
        // 增益映射起点。必须早于 OnCardDragBegin：世界空间预览的 Begin 内部会立即
        // Follow + TrySnapToTerrain 做一次初始吸附，那一刻就要读到起点。
        _dragOriginScreenPoint = eventData.position;
        _hasDragOrigin = true;

        // 提起态直接起拖是主路径（两种状态都可拖）。必须在此杀掉上移 tween 并同步清标志：
        // OnDragUpdate 会逐帧直写 anchoredPosition，未播完的 tween 会与之争抢位置；
        // 且进度基准是 _originPosition，视觉起点若停在 +0.025H 会让缩放/淡出进度从非 0 跳变。
        // 这里不播回落动画——下一帧拖拽立即接管位置。
        StopRaisePopAndResetScale();
        _rectTransform.DOKill();
        ReleaseRaiseCapture();

        _playerInputHandler.Value.ForceDeselectUnit();
        _isDragging = true;
        _activeDraggingCard = this;

        // 逐帧比例基准只在拖拽开始取一次（§3：优先 Canvas 参考高度，回退 1920）。
        _canvasHeight = UIScreenHelper.CanvasHeight(this);
        _dragAlpha = 1f;
        _upwardDistance = 0f;
        _cardProgress = 0f;

        _dropHandler?.OnCardDragBegin(this);

        // OnCardDragBegin 可能设置幽灵代理（战术卡），因此在其之后再判定是否走模型预览。
        _dragVisual = SupportsModelPreview ? _dropHandler as ICardDragVisualHandler : null;
        _dragVisual?.OnCardDragUpdate(this, eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (IsNextCard || !_isDragging) return;

        transform.SetAsLastSibling();

        RectTransform handPanelRect = _rectTransform.parent as RectTransform;
        if (handPanelRect == null) return;

        // 卡牌 / 幽灵卡位置跟随原始触点（v3：视觉不上移）；世界空间预览转发原始触点，
        // 由持握控制器内部换算逻辑射线坐标（GetCardDragLogicPosition）。
        // 手牌 Canvas 为 ScreenSpaceOverlay，传 null 正确；若未来改为
        // ScreenSpaceCamera，此处需改为 canvas.worldCamera。
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            handPanelRect, eventData.position, null, out Vector2 pointerLocalPos))
            return;

        OnDragUpdate(pointerLocalPos, (Vector2)_originPosition);

        // 只转发原始触点；世界空间预览的逐帧跟随由持握控制器自行驱动。
        _dragVisual?.OnCardDragUpdate(this, eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (IsNextCard || !_isDragging) return;

        // 必须在 ReleaseDragCapture 之前算：后者清除增益映射起点，
        // 之后再调 GetCardDragLogicPosition 会退化为恒等映射，
        // 落点将落回手指处、与松手瞬间的高亮格不一致。
        Vector2 dragLogicPosition = GetCardDragLogicPosition(eventData.position);

        transform.SetSiblingIndex(originalSiblingIndex);
        _isDragging = false;
        ReleaseDragCapture();
        // 成功部署路径会提前 return，故提起态在此统一清理（OnBeginDrag 已清过一次，幂等）。
        ReleaseRaiseCapture();

        ClearHighlights();

        RaycastHit hit;
        bool isMapHit;
        if (_mapRaycastService != null)
        {
            // 统一射线服务：命中 Chunk 的 MapChunkView 后代。
            // 显式传 CardDragRaycastMaxDistance（§3.2），与高亮 / 模型预览同一射程。
            isMapHit = _mapRaycastService.RaycastMap(dragLogicPosition, out hit, CardDragRaycastMaxDistance);
        }
        else
        {
            // 备用路径：必须使用同一个 dragLogicPosition，
            // 否则高亮与最终落点不一致；相机为空时按失败落点流程处理。
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                EndDragVisual();
                _dropHandler?.OnCardDragCancel(this);
                ResetToOrigin();
                return;
            }

            Ray ray = mainCamera.ScreenPointToRay(dragLogicPosition);
            isMapHit = Physics.Raycast(ray, out hit)
                && hit.transform.gameObject == _mapDataService.MapGameObject;
        }
        if (isMapHit)
        {
            HexCellData targetCell = _mapRaycastService != null
                ? _mapRaycastService.GetCellByWorldPosition(hit.point)
                : _mapDataService.GetCellByWorldPosition(hit.point);
            if (targetCell != null)
            {
                if (_dropHandler.HandleCardDragEnd(this, targetCell, hit.point))
                {
                    // 成功路径不会走 OnCardDragCancel，且卡牌随后被销毁，
                    // 必须在此显式收尾（End 幂等，与 handler 内部的提前关闭不冲突）。
                    EndDragVisual();
                    return;
                }
            }
        }

        EndDragVisual();
        _dropHandler?.OnCardDragCancel(this);
        ResetToOrigin();
    }

    /// <summary>关闭本次拖拽的视觉通道（幂等）；随后清空引用，避免迟到回调作用到下一次拖拽。</summary>
    private void EndDragVisual()
    {
        if (_dragVisual == null) return;

        ICardDragVisualHandler visual = _dragVisual;
        _dragVisual = null;
        visual.OnCardDragEnd(this);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus || !_isDragging) return;

        CancelDrag();
    }

    private void OnDisable()
    {
        if (_goldWallet != null) _goldWallet.OnGoldChanged -= OnGoldChanged;
        // 失活可能发生在成功部署销毁流程中，此时视觉通道已由 EndDragVisual 收尾；这里兜底幂等关闭。
        EndDragVisual();
        if (_isDragging) _dropHandler?.OnCardDragCancel(this);
        ReleaseDragCapture();
        // 卡牌打出后会被销毁：静态引用必须清掉，否则 PlayerInputHandler 会持有已销毁对象。
        ReleaseRaiseCapture();
        StopRaisePopAndResetScale();
        // ReleaseRaiseCapture 会起一个淡出补间，但失活路径上没有下一帧可播；
        // 直接掐掉，避免补间残留（实例复用时还会把上一次的淡出接着播完）。
        KillGlowImmediate();
        // 压暗同理：实例复用时残留的补间会把上一张卡的压暗接着播完。
        // 复位为 0 后，下次 SetData → RefreshAffordability 会按新卡重算。
        KillDimImmediate();
        // 下面的 DOKill 会跳过入场补间的 OnComplete，锁不会自行解开；
        // 若该实例被回收复用，残留的 true 会让它永远无法提起。
        IsTweening = false;
        _rectTransform?.DOKill();
    }

    /// <summary>克隆出来的材质实例不属于任何资源，必须手动销毁，否则每张卡牌泄漏一份。</summary>
    private void OnDestroy()
    {
        _glowTween?.Kill();
        _glowTween = null;
        _dimTween?.Kill();
        _dimTween = null;
        if (_glowMaterialInstance != null)
        {
            Destroy(_glowMaterialInstance);
            _glowMaterialInstance = null;
        }
    }

    private void ReleaseDragCapture()
    {
        if (_activeDraggingCard == this)
        {
            _activeDraggingCard = null;
            _hasDragOrigin = false;
        }
    }

    private void CancelDrag()
    {
        _isDragging = false;
        ReleaseDragCapture();
        // 拖拽结束一律回 Idle，不回提起态：ResetToOrigin 复位到 _originPosition，
        // 若保留 Raised 标志会与视觉位置矛盾。
        ReleaseRaiseCapture();
        transform.SetSiblingIndex(originalSiblingIndex);
        ClearHighlights();
        EndDragVisual();
        _dropHandler?.OnCardDragCancel(this);
        ResetToOrigin();
    }
}
