using UnityEngine;

/// <summary>
/// 【提示浮标叠加相机】专用叠加相机：在主相机及其雾化后处理之后渲染"Marker"图层，
/// 浮标因此永不参与选择性雾化（图标始终清晰），同时不再依赖"屏幕矩形擦除"——
/// 矩形擦除会连带清除浮标周围地面/金矿模型的雾，造成距离相关的不一致观感。
/// 由 PublicBuildingMarkerView 首次创建，每帧跟随主相机变换与 FOV。
/// </summary>
public class MarkerOverlayCamera : MonoBehaviour
{
    private static MarkerOverlayCamera _instance;
    public static MarkerOverlayCamera Instance => _instance;

    private Camera _mainCamera;
    private Camera _overlayCamera;

    public static void Ensure(Camera mainCamera)
    {
        if (_instance != null) return;
        if (mainCamera == null) return;

        GameObject go = new GameObject("MarkerOverlayCamera");
        _instance = go.AddComponent<MarkerOverlayCamera>();
        _instance._mainCamera = mainCamera;

        int markerLayer = LayerMask.NameToLayer("Marker");
        Camera overlay = go.AddComponent<Camera>();
        overlay.depth = mainCamera.depth + 1f;
        overlay.cullingMask = 1 << markerLayer;
        overlay.clearFlags = CameraClearFlags.Depth;
        overlay.allowHDR = mainCamera.allowHDR;
        overlay.allowMSAA = mainCamera.allowMSAA;
        overlay.nearClipPlane = mainCamera.nearClipPlane;
        overlay.farClipPlane = mainCamera.farClipPlane;
        overlay.useOcclusionCulling = false;
        _instance._overlayCamera = overlay;
    }

    private void LateUpdate()
    {
        if (_mainCamera == null || _overlayCamera == null) return;
        transform.SetPositionAndRotation(_mainCamera.transform.position, _mainCamera.transform.rotation);
        _overlayCamera.fieldOfView = _mainCamera.fieldOfView;
    }
}
