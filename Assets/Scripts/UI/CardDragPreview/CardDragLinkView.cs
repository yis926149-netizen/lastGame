using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 卡牌拖拽连线视图（落点图标与连线计划 §4.2）。
/// 一个 Image（_line，pivot 需在编辑器设为 (0, 0.5)），每帧在 LateUpdate 自行计算两个端点：
/// 上端点 = 图标底部（markerView.LinkAnchorWorldPosition 经主相机反投影），
/// 下端点 = 拖拽中卡牌的顶边中点（战术卡为幽灵代理）。
/// 不接受外部 push 位置：PlayerInputHandler.Tick（Update）与 EventSystem.OnDrag（同为 Update）
/// 执行顺序不保证，若由 Tick 推送卡牌位置，快速拖动时线会滞后卡牌一帧、肉眼可见地脱开。
///
/// 线体外观（sprite / color / material）完全由编辑器绑定，代码只写 rectTransform 的
/// sizeDelta.x（长度）、localRotation、anchoredPosition；sizeDelta.y（线宽）由
/// FeelConfigProvider.CardDragLinkWidth 驱动，该值 ≤ 0 时保留编辑器宽度不覆盖。
///
/// 画布假设：手牌 Canvas 为 ScreenSpaceOverlay（见 CardController.OnDrag 注释），
/// ScreenPointToLocalPointInRectangle 的 camera 参数传 null；若改为 ScreenSpaceCamera 需同步修改。
/// 已知：Overlay 画布在所有相机之后绘制，连线会盖在图标之上；因连线终止于图标底部，
/// 重叠面积极小，接受不处理。
/// </summary>
public class CardDragLinkView : MonoBehaviour
{
    [Header("连线绑定（编辑器指定）")]
    /// <summary>线体。sprite（可用九宫格虚线图）/ color / material 在 Inspector 设定，代码不覆盖。</summary>
    [SerializeField] private Image _line;

    private RectTransform _rootRect;
    private RectTransform _lineRect;
    private CardDragTargetMarkerView _markerView;
    private bool _lineBound;

    /// <summary>由 CardDragTargetMarkerController 在创建后注入：连线上端点来源（图标底部）。</summary>
    public void SetMarkerAnchor(CardDragTargetMarkerView markerView) => _markerView = markerView;

    public void Show()
    {
        // 先激活：prefab 若以隐藏状态保存，Awake 会在此刻补跑并完成绑定。
        gameObject.SetActive(true);
        if (!_lineBound) gameObject.SetActive(false);
    }

    public void Hide()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    private void Awake()
    {
        _rootRect = (RectTransform)transform;

        if (_line == null)
        {
            // 不静默降级：未绑定即“功能没生效”。报错后恒隐藏。
            Debug.LogError(
                "[CardDragLink] 连线视图未绑定 _line：请在 prefab 上把线体 Image 拖到视图的 Line 槽。该视图恒隐藏。");
            _lineBound = false;
            gameObject.SetActive(false);
            return;
        }

        _lineBound = true;
        _lineRect = _line.rectTransform;
        // 不做无条件隐藏：prefab 若以隐藏状态保存，Awake 在首次 Show 激活时补跑，
        // 若在此再隐藏会抵消本次激活。初始无 Canvas 父级不会渲染，LateUpdate 会自行按状态收起。
    }

    private void LateUpdate()
    {
        // 连线与图标共存亡：图标隐藏（控制器收起 / 未绑定 / Camera.main 为空）时同帧隐藏。
        if (!_lineBound || _markerView == null || !_markerView.gameObject.activeSelf)
        {
            Hide();
            return;
        }

        // 下端点来源：当前拖拽卡牌的视觉 RectTransform（战术卡 = 幽灵代理）；无拖拽即隐藏。
        RectTransform cardRect = CardController.ActiveDragVisualRect;
        if (cardRect == null)
        {
            Hide();
            return;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Hide();
            return;
        }

        // 上端点：图标底部世界坐标 → 屏幕像素。
        Vector3 topScreen = mainCamera.WorldToScreenPoint(_markerView.LinkAnchorWorldPosition);
        // 图标跑到相机背后时 WorldToScreenPoint 返回镜像坐标，线会瞬间甩到屏幕另一侧：直接隐藏。
        if (topScreen.z <= 0f)
        {
            Hide();
            return;
        }

        RectTransform handPanel = cardRect.parent as RectTransform;
        if (handPanel == null || handPanel.parent == null)
        {
            Hide();
            return;
        }

        // 挂为手牌面板的前置兄弟节点（同一 Overlay Canvas 体系）：
        // 卡牌在 OnDrag 里 SetAsLastSibling，线稳定压在其下、不遮挡卡面。
        EnsureAttachedBefore(handPanel);

        // 上端点转本视图根节点局部坐标（Overlay Canvas → camera 传 null）。
        Vector2 topLocal;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rootRect, (Vector2)topScreen, null, out topLocal))
        {
            Hide();
            return;
        }

        // 下端点 = 卡牌顶边中点（取顶边而非触点：语义接近且不被手指盖住）。
        // Overlay Canvas 下 UI 世界坐标即屏幕像素，直接转根节点局部坐标。
        Rect cardLocalRect = cardRect.rect;
        Vector3 cardTopMidWorld = cardRect.TransformPoint(
            new Vector3(cardLocalRect.center.x, cardLocalRect.yMax, 0f));
        Vector2 bottomLocal = _rootRect.InverseTransformPoint(cardTopMidWorld);

        Vector2 dir = topLocal - bottomLocal;
        float length = dir.magnitude;
        if (length <= 0.0001f)
        {
            Hide();
            return;
        }

        // 线宽用 Canvas 参考单位（非设备像素），由配置驱动；≤ 0 时保留编辑器宽度。
        float width = FeelConfigProvider.CardDragLinkWidth;
        if (width > 0f)
            _lineRect.sizeDelta = new Vector2(length, width);
        else
            _lineRect.sizeDelta = new Vector2(length, _lineRect.sizeDelta.y);

        _lineRect.anchoredPosition = bottomLocal;
        _lineRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
    }

    /// <summary>确保本视图挂接在手牌面板父节点下、且兄弟序紧挨手牌面板之前（稳定压在其下）。</summary>
    private void EnsureAttachedBefore(RectTransform handPanel)
    {
        if (transform.parent != handPanel.parent)
            transform.SetParent(handPanel.parent, false);

        // 根 Rect 归零对齐父节点原点，使“根局部坐标 = 画布坐标”，端点换算无需再考虑根自身偏移。
        _rootRect.anchorMin = Vector2.zero;
        _rootRect.anchorMax = Vector2.zero;
        _rootRect.anchoredPosition = Vector2.zero;

        int handIndex = handPanel.GetSiblingIndex();
        if (transform.GetSiblingIndex() != handIndex - 1)
            transform.SetSiblingIndex(Mathf.Max(0, handIndex - 1));
    }
}
