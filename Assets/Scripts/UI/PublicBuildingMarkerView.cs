using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 提示浮标视图：呼吸动画 + 图标设置 + 始终面向相机。
/// 【浮标叠加相机】本组件负责把浮标整体移到"Marker"专用图层，并确保 MarkerOverlayCamera
/// 存在且从主相机剔除该图层——浮标由叠加相机在主相机雾化后处理之后渲染，
/// 永不参与选择性雾化（图标始终清晰，且不再需要屏幕矩形擦除）。
/// </summary>
public class PublicBuildingMarkerView : MonoBehaviour
{
    [SerializeField] private Image _icon;

    private Camera _camera;
    private CanvasGroup _canvasGroup;
    private Vector3 _baseScale;
    private float _phase;
    private static int? _markerLayer;

    public void SetIcon(Sprite sprite)
    {
        if (_icon != null && sprite != null)
        {
            _icon.sprite = sprite;
            _icon.preserveAspect = true;
        }
    }

    private void Awake()
    {
        _camera = Camera.main;
        _canvasGroup = GetComponent<CanvasGroup>();
        _baseScale = transform.localScale;
        _phase = Random.Range(0f, Mathf.PI * 2f);

        if (!_markerLayer.HasValue)
            _markerLayer = LayerMask.NameToLayer("Marker");

        if (_camera != null)
        {
            MarkerOverlayCamera.Ensure(_camera);
            _camera.cullingMask &= ~(1 << _markerLayer.Value);
        }
        SetLayerRecursively(gameObject, _markerLayer.Value);
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        for (int i = 0; i < root.transform.childCount; i++)
            SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
    }

    private void LateUpdate()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera != null)
            transform.LookAt(transform.position + _camera.transform.forward, _camera.transform.up);

        float pulse = (Mathf.Sin(Time.time * 2f + _phase) + 1f) * 0.5f;
        transform.localScale = _baseScale * Mathf.Lerp(0.94f, 1.06f, pulse);
        if (_canvasGroup != null)
            _canvasGroup.alpha = Mathf.Lerp(0.72f, 1f, pulse);
    }
}
