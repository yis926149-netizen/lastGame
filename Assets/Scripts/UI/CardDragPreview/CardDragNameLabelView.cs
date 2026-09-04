using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 卡牌拖拽模型名牌视图（方案二：单个屏幕空间跟随标签）。
/// 由 CardDragWorldPreviewController 懒创建一次并复用（SetActive 收起），拖拽期逐帧调用
/// UpdateFollow 把世界锚点反投影到屏幕并贴合文本；绝不挂到模型预览实例下——
/// CardDragPreviewUtils.PrepareForDrag 会禁用实例子树内的全部 Canvas（同 CardDragTargetMarkerView 注释）。
///
/// Prefab 要求（编辑器自行搭建，本类不创建 GameObject、不 Resources.Load）：
/// - 根节点挂 Canvas，渲染模式建议 Screen Space - Overlay（与手牌 Canvas 一致，见 CardDragLinkView 注释）；
///   若用 Screen Space - Camera 需给 Canvas 指定 worldCamera（代码自动兼容两种模式）；
/// - 子节点挂 TextMeshProUGUI（或旧版 Text），拖到 TMP Text / Legacy Text 槽；留空时
///   Awake 用 GetComponentInChildren 自动查找；
/// - 字号 / 颜色 / 描边 / 背景 / pivot（建议 (0.5,1)）一律在 Inspector 设定，代码只写文本与位置；
/// - Screen Offset 为屏幕像素微调（Y 正 = 向上），在世界锚点投影后叠加，编辑器随时可调。
///
/// 隐藏判定（与世界空间预览一致的兜底）：
/// - 相机背后：WorldToScreenPoint 返回镜像坐标，标签会瞬间甩到屏幕另一侧，z&lt;=0 直接隐藏（同 CardDragLinkView）；
/// - 屏幕外（含模型未射中地形时的地图下方隐藏位）：按 ScreenMargin 像素余量隐藏。
/// </summary>
public class CardDragNameLabelView : MonoBehaviour
{
    [Header("文本绑定（编辑器指定，留空自动查找）")]
    [SerializeField] private TextMeshProUGUI _tmpText;   // 首选：TMP 文本
    [SerializeField] private Text _legacyText;           // 兜底：旧版 Text

    [Header("屏幕空间跟随")]
    [Tooltip("在模型世界锚点投影到屏幕后叠加的固定像素偏移（Y 为正表示向上）。纯屏幕像素量，不随相机距离或缩放变化。")]
    [SerializeField] private Vector2 _screenOffset = new Vector2(0f, 80f);

    /// <summary>屏幕外判定余量（像素）：锚点超出屏幕该距离即隐藏，避免文字边缘残留。</summary>
    private const float ScreenMargin = 200f;

    private RectTransform _textRect;
    private Canvas _canvas;
    private RectTransform _canvasRect;
    private bool _bound;

    /// <summary>写入显示名（拖拽开始时由控制器调用一次）。</summary>
    public void SetDisplayName(string displayName)
    {
        if (!_bound) return;
        if (_tmpText != null) _tmpText.text = displayName;
        else if (_legacyText != null) _legacyText.text = displayName;
    }

    public void Show()
    {
        // 先激活：prefab 若以隐藏状态保存，Awake 会在此刻补跑并完成绑定（同 CardDragTargetMarkerView）。
        gameObject.SetActive(true);
        if (!_bound) gameObject.SetActive(false);
    }

    public void Hide()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    /// <summary>
    /// 逐帧跟随：世界锚点 → 屏幕 → 文本 RectTransform 世界坐标。
    /// 相机为空 / 锚点在相机背后 / 屏幕外时自动隐藏，其余情况激活。
    /// </summary>
    public void UpdateFollow(Vector3 worldPosition, Camera camera)
    {
        if (!_bound || camera == null)
        {
            Hide();
            return;
        }

        Vector3 screen = camera.WorldToScreenPoint(worldPosition);
        if (screen.z <= 0f)
        {
            // 相机背后：镜像坐标，直接隐藏（同 CardDragLinkView 判定）。
            Hide();
            return;
        }

        if (screen.x < -ScreenMargin || screen.x > Screen.width + ScreenMargin ||
            screen.y < -ScreenMargin || screen.y > Screen.height + ScreenMargin)
        {
            Hide();
            return;
        }

        // Overlay 画布 camera 传 null；Camera 模式必须用 canvas.worldCamera，缺相机则隐藏。
        Camera rectCamera = null;
        if (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            rectCamera = _canvas.worldCamera;
            if (rectCamera == null)
            {
                Hide();
                return;
            }
        }

        // 固定像素偏移：在屏幕坐标上叠加（世界锚点先投影，再按像素平移），
        // 与相机距离、CanvasScaler 缩放解耦——远近平移量在屏幕上恒定。
        screen.x += _screenOffset.x;
        screen.y += _screenOffset.y;

        // 走世界坐标路径（而非 anchoredPosition）：与父层级、锚点、CanvasScaler 缩放全部解耦，
        // 任意层级下 position 都直接成立（Overlay 下 UI 世界坐标 == 屏幕像素）。
        Vector3 world;
        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                _canvasRect, screen, rectCamera, out world))
        {
            Hide();
            return;
        }

        if (!gameObject.activeSelf) gameObject.SetActive(true);
        _textRect.position = world;
    }

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
        if (_canvas == null)
        {
            Debug.LogError(
                "[CardDragNameLabel] 视图不在任何 Canvas 下：请在名牌 prefab 根节点挂 Canvas（建议 Screen Space - Overlay）。该视图恒隐藏。");
            _bound = false;
            gameObject.SetActive(false);
            return;
        }
        _canvasRect = (RectTransform)_canvas.transform;

        if (_tmpText == null) _tmpText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (_tmpText == null && _legacyText == null) _legacyText = GetComponentInChildren<Text>(true);

        if (_tmpText == null && _legacyText == null)
        {
            Debug.LogError(
                "[CardDragNameLabel] 视图未绑定且未找到 TextMeshProUGUI/Text：请在 prefab 上挂文本组件并拖到 TMP Text / Legacy Text 槽。该视图恒隐藏。");
            _bound = false;
            gameObject.SetActive(false);
            return;
        }

        _textRect = _tmpText != null ? _tmpText.rectTransform : _legacyText.rectTransform;
        _bound = true;
        // 不在此无条件隐藏：prefab 若以隐藏状态保存，Awake 在首次 Show 激活时补跑，
        // 再隐藏会抵消本次激活；控制器在 ReleaseOwnership / Cancel / Dispose 时显式 Hide。
    }
}
