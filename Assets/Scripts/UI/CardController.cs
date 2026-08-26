using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Zenject;

[RequireComponent(typeof(Image), typeof(RectTransform))]
public class CardController : MonoBehaviour, ICardView, IPointerEnterHandler, IPointerExitHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Inject] private IMapDataService _mapDataService;
    [Inject] private ICardDropHandler _dropHandler;
    [Inject] private IUIConfigProvider _uiConfig;
    [Inject] private GameLoop _gameLoop;
    [Inject] private LazyInject<PlayerInputHandler> _playerInputHandler;
    [Inject(Optional = true)] private IMapRaycastService _mapRaycastService;
    [Inject] private GoldWallet _goldWallet;

    /// <summary>金币不足时卡面压暗的透明度倍率（已迁移至 FeelConfigProvider）。</summary>
    // 【Excel 数值化】原 const UnaffordableDim = 0.45f 迁移至 FeelConfigProvider。

    /// <summary>当前是否买得起（金币 >= 卡牌费用）。战术卡（无金币约束）恒为 true。</summary>
    private bool _isAffordable = true;

    /// <summary>各 Graphic 的原始颜色（含 alpha），用于压暗/还原。SetData 时采样。</summary>
    private readonly Dictionary<Graphic, Color> _graphicBaseColors = new Dictionary<Graphic, Color>();

    /// <summary>
    /// 拖拽期间的视觉通道（普通卡 = CardPresenter；战术卡不实现本接口，自动无模型预览）。
    /// 在 OnBeginDrag 时锁定一次，保证 Update/End 与 Begin 落在同一个 handler 上。
    /// </summary>
    private ICardDragVisualHandler _dragVisual;

    /// <summary>拖拽淡出倍率（§3 cardAlpha）。与可负担压暗、原始 alpha 相乘后统一写入。</summary>
    private float _dragAlpha = 1f;

    /// <summary>本卡所在 Canvas 的参考高度；OnBeginDrag 缓存一次，避免逐帧 GetComponentInParent。</summary>
    private float _canvasHeight = UIScreenHelper.ReferenceHeight;

    /// <summary>逐帧缓存的两阶段进度，供 OnDrag 通知视觉通道时复用（避免重复计算）。</summary>
    private float _upwardDistance;
    private float _cardProgress;
    private float _modelProgress;

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

    public static bool IsAnyCardDragging => _activeDraggingCard != null;


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

        // 订阅金币变动以实时刷新“买不起 → 压暗 + 禁用交互”状态。
        if (_goldWallet != null)
        {
            _goldWallet.OnGoldChanged -= OnGoldChanged;
            _goldWallet.OnGoldChanged += OnGoldChanged;
        }
        RefreshAffordability();
    }

    /// <summary>金币变动回调：仅在跨越“买得起/买不起”阈值时更新视觉与交互。</summary>
    private void OnGoldChanged(int _)
    {
        RefreshAffordability();
    }

    /// <summary>根据当前金币与卡牌费用刷新可用性；压暗卡面并阻断悬浮/拖拽。</summary>
    private void RefreshAffordability()
    {
        // 预告卡与战术卡（负数 ID / 无金币约束）不做金币压暗。
        if (_data == null || IsNextCard || _data.ID < 0)
        {
            SetAffordable(true);
            return;
        }

        bool affordable = _goldWallet == null || _goldWallet.Gold >= _data.CardCost;
        SetAffordable(affordable);
    }

    /// <summary>采样根 Image 及所有子级可见 Graphic 的原始颜色，作为压暗/还原的基准。</summary>
    private void CaptureBaseColors()
    {
        _graphicBaseColors.Clear();
        foreach (Graphic g in GetComponentsInChildren<Graphic>(true))
        {
            if (g == null) continue;
            // 预制体内隐藏着一块超大但完全透明的 Button Image（用于表现层），
            // 其 alpha=0；若纳入压暗集合会被显形为半透明白板，因此跳过完全透明的组件。
            if (g.color.a <= 0f) continue;
            _graphicBaseColors[g] = g.color;
        }
    }

    private void SetAffordable(bool affordable)
    {
        _isAffordable = affordable;

        ApplyGraphicAlpha();

        // 买不起时停止接收射线（悬浮/拖拽），并复位悬浮上移；若在拖拽中立即取消。
        if (_image != null) _image.raycastTarget = affordable;

        if (!affordable)
        {
            // 悬浮中变为买不起时，UI 射线不再触发 OnPointerExit，需主动复位位置。
            if (!_isDragging)
            {
                // 只复位位置，不清除缩放 Tween：金币状态刷新不应打断发牌/入手的缩放动画。
                _rectTransform.DOAnchorPos(_originPosition, 0.2f);
            }
            if (_isDragging) CancelDrag();
        }
    }

    /// <summary>
    /// alpha 唯一写入口（实施计划 §5.3）：最终 alpha = 原始 alpha × 可负担倍率 × 拖拽淡出倍率。
    /// 三个来源都只改自己那一项因子，再由本方法统一合成，避免互相覆盖。
    /// </summary>
    private void ApplyGraphicAlpha()
    {
        float affordMul = _isAffordable ? 1f : FeelConfigProvider.UnaffordableCardDim;
        float mul = affordMul * _dragAlpha;

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
            _rectTransform.DOAnchorPos(targetPosition, 0.6f).SetEase(Ease.OutBack, 1.2f);
            _rectTransform.DOScale(_uiConfig.NextCardSize, 0.6f).SetEase(Ease.OutBack, 1.2f);

            onComplete?.Invoke(); // 预告卡无需等待
            return;
        }

        // 普通手牌发牌逻辑（保持不变）
        _rectTransform.DOAnchorPos(targetPosition, 0.4f).OnComplete(() => onComplete?.Invoke());
        _rectTransform.DOScale(_uiConfig.CardSize, 0.4f);
    }

    public void OnDragUpdate(Vector2 localPoint, Vector2 originPos)
    {
        RectTransform target = _dragProxy != null ? _dragProxy : _rectTransform;
        target.anchoredPosition = localPoint;

        // 只有向上拖才推进两阶段进度；向下拖进度按 0 处理（§0.5），不取绝对值。
        _upwardDistance = Mathf.Max(0f, localPoint.y - originPos.y);

        if (!SupportsModelPreview)
        {
            // 战术卡/幽灵卡：维持原有单阶段缩放手感（minScale 0.6，双向生效）。
            float legacyDistance = Mathf.Abs(localPoint.y - originPos.y);
            float legacyMax = _canvasHeight * 0.37f;
            float legacyRatio = Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(legacyDistance / legacyMax));
            target.localScale = _uiConfig.CardSize * legacyRatio;

            _cardProgress = 0f;
            _modelProgress = 0f;
            return;
        }

        float d1 = Mathf.Max(1f, _canvasHeight * FeelConfigProvider.CardDragStage1Ratio);
        float d2 = Mathf.Max(d1 + 1f, _canvasHeight * FeelConfigProvider.CardDragStage2Ratio);

        _cardProgress = Mathf.Clamp01(_upwardDistance / d1);
        _modelProgress = Mathf.Clamp01((_upwardDistance - d1) / (d2 - d1));

        // 阶段一：卡牌 100% → CardMinScale，并在 CardFadeStart 之后淡出。
        float cardScale = Mathf.Lerp(1f, FeelConfigProvider.CardDragCardMinScale, _cardProgress);
        target.localScale = _uiConfig.CardSize * cardScale;

        float fadeStart = Mathf.Clamp01(FeelConfigProvider.CardDragCardFadeStart);
        float fadeSpan = Mathf.Max(0.0001f, 1f - fadeStart);
        _dragAlpha = 1f - Mathf.Clamp01((_cardProgress - fadeStart) / fadeSpan);
        ApplyGraphicAlpha();
    }

    /// <summary>是否走「卡牌→模型」两阶段表现：仅普通卡（持有 NormalCardConfig）且非代理拖拽。</summary>
    private bool SupportsModelPreview => _dragProxy == null && _data != null && _data.NormalCardConfig != null;

    public void ResetToOrigin()
    {
        _rectTransform.DOKill();
        _rectTransform.DOAnchorPos(_originPosition, 0.2f);
        transform.DOScale(_uiConfig.CardSize, 0.2f);

        // 幽灵代理在拖拽期间承担了位移/缩放，取消时必须一并复位，否则下次借出时姿态残留。
        if (_dragProxy != null)
        {
            _dragProxy.DOKill();
            _dragProxy.localScale = _uiConfig.CardSize;
        }

        // 复位拖拽淡出因子并重新合成 alpha（可负担压暗由 _isAffordable 继续生效）。
        _dragAlpha = 1f;
        _upwardDistance = 0f;
        _cardProgress = 0f;
        _modelProgress = 0f;
        ApplyGraphicAlpha();
    }

    public void ClearHighlights()
    {
        _playerInputHandler.Value.ClearCardDragHighlight();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (IsNextCard) return;
        if (_isDragging) return;
        if (!_isAffordable) return;

        _rectTransform.DOAnchorPos(_originPosition + new Vector3(0, UIScreenHelper.ReferenceHeight * 0.025f, 0), 0.2f); // B3: 悬停上移改为 Canvas 参考高度比例
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (IsNextCard) return;
        if (_isDragging) return;

        _rectTransform.DOAnchorPos(_originPosition, 0.2f);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (IsNextCard || !_isAffordable || (_gameLoop != null && _gameLoop.IsPaused))
        {
            eventData.pointerDrag = null;
            return;
        }
        _playerInputHandler.Value.ForceDeselectUnit();
        _isDragging = true;
        _activeDraggingCard = this;

        // 逐帧比例基准只在拖拽开始取一次（§3：优先 Canvas 参考高度，回退 1920）。
        _canvasHeight = UIScreenHelper.CanvasHeight(this);
        _dragAlpha = 1f;
        _upwardDistance = 0f;
        _cardProgress = 0f;
        _modelProgress = 0f;

        _dropHandler?.OnCardDragBegin(this);

        // OnCardDragBegin 可能设置幽灵代理（战术卡），因此在其之后再判定是否走模型预览。
        _dragVisual = SupportsModelPreview ? _dropHandler as ICardDragVisualHandler : null;
        _dragVisual?.OnCardDragUpdate(this, eventData.position, 0f, 0f, 0f);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (IsNextCard || !_isDragging) return;

        transform.SetAsLastSibling();

        RectTransform handPanelRect = _rectTransform.parent as RectTransform;
        if (handPanelRect == null) return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            handPanelRect,
            eventData.position,
            null,
            out Vector2 handPanelLocalPos))
        {
            OnDragUpdate(handPanelLocalPos, _originPosition);

            // 进度已在 OnDragUpdate 中算好，这里只做转发（每帧不重复计算）。
            _dragVisual?.OnCardDragUpdate(this, eventData.position, _upwardDistance, _cardProgress, _modelProgress);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (IsNextCard || !_isDragging) return;

        transform.SetSiblingIndex(originalSiblingIndex);   
        _isDragging = false;
        ReleaseDragCapture();

        ClearHighlights();

        if (_gameLoop != null && _gameLoop.IsPaused)
        {
            EndDragVisual();
            _dropHandler?.OnCardDragCancel(this);
            ResetToOrigin();
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(eventData.position);
        RaycastHit hit;
        bool isMapHit;
        if (_mapRaycastService != null)
        {
            // 统一射线服务：命中 Chunk 的 MapChunkView 后代。
            isMapHit = _mapRaycastService.RaycastMap(eventData.position, out hit);
        }
        else
        {
            isMapHit = Physics.Raycast(ray, out hit) && hit.transform.gameObject == _mapDataService.MapGameObject;
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
        _rectTransform?.DOKill();
    }

    private void ReleaseDragCapture()
    {
        if (_activeDraggingCard == this)
            _activeDraggingCard = null;
    }

    private void CancelDrag()
    {
        _isDragging = false;
        ReleaseDragCapture();
        transform.SetSiblingIndex(originalSiblingIndex);
        ClearHighlights();
        EndDragVisual();
        _dropHandler?.OnCardDragCancel(this);
        ResetToOrigin();
    }
}
