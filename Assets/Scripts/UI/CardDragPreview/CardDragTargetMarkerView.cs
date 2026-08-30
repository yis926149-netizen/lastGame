using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 卡牌拖拽落点图标视图（落点图标与连线计划 §4.1）。
/// 直接照搬 PublicBuildingMarkerView 的成熟范式：world-space Canvas + Image、
/// LateUpdate billboard、Awake 把自身及全部子物体移入 "Marker" 层并自行
/// Ensure MarkerOverlayCamera——该叠加相机目前只由 PublicBuildingMarkerView.Awake 创建，
/// 本局尚未出现任何公共建筑浮标时相机不存在，图标会直接不可见，因此本视图必须自行 Ensure。
/// 走 Marker 层的收益：叠加相机 clearFlags = Depth 且在主相机之后渲染，图标
/// 永不被山体 / 前景单位遮挡，也不吃选择性雾化（逻辑位置经常落在山背后，这是刚需）。
///
/// 图标外观完全由编辑器绑定：本类不创建 GameObject、不 Resources.Load、
/// 不写 sprite / color / sizeDelta。代码只负责位置、朝向、显隐与呼吸缩放。
/// 独立对象、独立生命周期：绝不挂到模型预览实例下——CardDragPreviewUtils.PrepareForDrag
/// 会 GetComponentsInChildren 把 Canvas / GraphicRaycaster / 全部 MonoBehaviour 禁用，
/// 挂进去会被静默关掉，RestoreForDeployment 还会在落地时把它恢复。
/// </summary>
public class CardDragTargetMarkerView : MonoBehaviour
{
    [Header("图标绑定（编辑器指定）")]
    /// <summary>落点图标本体。sprite / color / RectTransform 尺寸一律在 Inspector 上设定，代码不覆盖。</summary>
    [SerializeField] private Image _icon;

    /// <summary>可选：与图标一起显隐的装饰（光晕、箭头、外圈等）。留空即不使用。</summary>
    [SerializeField] private GameObject[] _decorations;

    private Camera _camera;
    private CanvasGroup _canvasGroup;
    private Vector3 _baseScale;
    private float _phase;
    private bool _iconBound;
    private static int? _markerLayer;

    /// <summary>连线上端点的世界坐标（图标底部），供 CardDragLinkView（第二批）反投影。</summary>
    public Vector3 LinkAnchorWorldPosition
    {
        get
        {
            if (_icon == null) return transform.position;

            // 图标底部 = 根位置向下偏移半个图标世界高度；连线为第二批功能，端点精度届时再核对。
            float halfHeight = _icon.rectTransform.rect.height * 0.5f * _icon.rectTransform.lossyScale.y;
            return transform.position - transform.up * halfHeight;
        }
    }

    /// <summary>显示于指定世界坐标上方（通常为射线命中点 hit.point）。
    /// 高度偏移由 FeelConfigProvider.CardDragTargetIconHeight 控制；
    /// 与 CardDragWorldPreviewController 使用的 CardDragPreviewHoverHeight 语义对齐：
    /// 两者均为"贴地后往上抬多少"，可调为相同值使图标恰好悬浮在模型正上方。</summary>
    public void ShowAtPosition(Vector3 worldPos)
    {
        // 先激活：prefab 若以隐藏状态保存，Awake 会在此刻补跑并完成绑定与 Marker 层设置。
        gameObject.SetActive(true);
        if (!_iconBound) return;

        transform.position = worldPos
            + Vector3.up * Mathf.Max(0f, FeelConfigProvider.CardDragTargetIconHeight);

        SetDecorationsActive(true);
    }

    public void Hide()
    {
        // 显隐统一走根节点：图标与全部装饰子物体共存亡（连线约束 3 的同源保证）。
        SetDecorationsActive(false);
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    private void Awake()
    {
        _camera = Camera.main;
        _canvasGroup = GetComponent<CanvasGroup>();
        _baseScale = transform.localScale;
        _phase = Random.Range(0f, Mathf.PI * 2f);

        if (_icon == null)
        {
            // 不静默降级：未绑定即“功能没生效”，排查成本远高于一条报错。报错后恒隐藏。
            Debug.LogError(
                "[CardDragTargetMarker] 图标视图未绑定 _icon：请在 prefab 上把图标 Image 拖到视图的 Icon 槽。该视图恒隐藏。");
            _iconBound = false;
            gameObject.SetActive(false);
            return;
        }
        _iconBound = true;

        if (!_markerLayer.HasValue)
            _markerLayer = LayerMask.NameToLayer("Marker");

        if (_camera != null)
            EnsureOverlayVisibility();

        SetLayerRecursively(gameObject, _markerLayer.Value);
    }

    /// <summary>主相机剔除 Marker 层 + 确保叠加相机存在；相机在 Awake 后出现时由 LateUpdate 补做。</summary>
    private void EnsureOverlayVisibility()
    {
        MarkerOverlayCamera.Ensure(_camera);
        _camera.cullingMask &= ~(1 << _markerLayer.Value);
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        for (int i = 0; i < root.transform.childCount; i++)
            SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
    }

    private void SetDecorationsActive(bool active)
    {
        if (_decorations == null) return;
        for (int i = 0; i < _decorations.Length; i++)
        {
            if (_decorations[i] != null)
                _decorations[i].SetActive(active);
        }
    }

    private void LateUpdate()
    {
        if (!_iconBound) return;

        if (_camera == null)
        {
            _camera = Camera.main;
            if (_camera != null) EnsureOverlayVisibility();
        }

        if (_camera != null)
            transform.LookAt(transform.position + _camera.transform.forward, _camera.transform.up);

        // 呼吸动画：以 _baseScale（Awake 采样，含编辑器调好的世界大小）为基准缩放，
        // 绝不写死 localScale = Vector3.one——那会覆盖编辑器里调好的尺寸。
        float pulse = (Mathf.Sin(Time.time * 2f + _phase) + 1f) * 0.5f;
        transform.localScale = _baseScale
            * Mathf.Max(0f, FeelConfigProvider.CardDragTargetIconScale)
            * Mathf.Lerp(0.94f, 1.06f, pulse);

        if (_canvasGroup != null)
            _canvasGroup.alpha = Mathf.Lerp(0.72f, 1f, pulse);
    }
}
