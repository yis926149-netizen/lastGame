using UnityEngine;
using Zenject;

public class CameraController : MonoBehaviour, ITickable
{
    private IInputService _input;
    private IMapDataService _mapData;
    private MapGenerationConfigSO _config;
    private MapGenerator _mapGenerator;

    private Camera _mainCamera;

    [Header("B4: 竖屏适配")]
    [Tooltip("横屏基准 FOV（竖屏时按 portraitFOVMultiplier 放大）")]
    public float baseFOV = 60f; // 横屏基准 FOV
    [Tooltip("竖屏 FOV 放大系数（宽高比 < 1 时生效）")]
    public float portraitFOVMultiplier = 1.3f; // 竖屏 FOV 放大系数

    [Header("相机移动范围")]
    [Tooltip("相机平移速度（世界单位/秒）")]
    public float moveSpeed = 5f;
    [Tooltip("相机平移平滑时间（秒），值越大跟随越慢")]
    public float smoothMoveTime = 0.1f;
    private float _minX = -50f;
    private float _maxX = 50f;
    private float _minZ = -50f;
    private float _maxZ = 50f;
    private float _boundsPlaneY;
    private readonly Vector3[] _frustumCorners = new Vector3[4];

    [Header("相机缩放距离")]
    [Tooltip("相机目标位置的世界 Y 坐标下限")]
    public float minZoomDistance = 20f;
    [Tooltip("相机目标位置的世界 Y 坐标上限")]
    public float maxZoomDistance = 75f;
    [Tooltip("鼠标滚轮缩放速度（每格滚轮移动的世界单位量）")]
    public float zoomSpeed = 0.1f;
    [Tooltip("相机缩放平滑时间（秒）")]
    public float smoothZoomTime = 0.1f;

    private Vector3 _targetCameraPosition;
    private Quaternion _targetCameraRotation;
    private Vector3 _zoomVelocity;
    private bool _boundsInitialized = false;

    private bool _wasPinching;
    private float _pinchStartHeight;

    [Header("鼠标拖拽移动")]
    [Tooltip("鼠标右键拖拽灵敏度（值越大拖动越快）")]
    public float dragSensitivity = 0.012f;
    [Tooltip("拖拽启动阈值（屏幕像素）")]
    public float dragStartThreshold = 10f;
    private bool _isDragging = false;
    private Vector3 _dragStartPosition;
    private Vector3 _lastMousePosition;

    // ── 屏幕震动 ──────────────────────────────────────
    private float _shakeStrength;
    private float _shakeDuration;
    private float _shakeElapsed;
    private Vector3 _shakeOffset;
    // 【Excel 数值化】相机震动频率迁移至 FeelConfigProvider（原 const ShakeFrequency = 50）。

    /// <summary>触发一次屏幕震动（叠加在平滑位移之上，不影响相机目标位置）。</summary>
    public void Shake(float strength, float duration)
    {
        _shakeStrength = strength;
        _shakeDuration = duration;
        _shakeElapsed = 0f;
    }

    [Inject]
    public void Construct(
        IInputService input,
        IMapDataService mapData,
        MapGenerationConfigSO config,
        MapGenerator mapGenerator)
    {
        _input = input;
        _mapData = mapData;
        _config = config;
        _mapGenerator = mapGenerator;
    }

    private void Start()
    {
        _mainCamera = Camera.main;
        if (_mainCamera == null)
        {
            Debug.LogError("CameraController: 场景中没有找到主相机");
            return;
        }

        // B4: 竖屏适配 - 根据屏幕宽高比动态调整 FOV
        float aspectRatio = (float)Screen.width / Screen.height;
        if (aspectRatio < 1f) // 竖屏
        {
            _mainCamera.fieldOfView = baseFOV * portraitFOVMultiplier;
        }
        else // 横屏
        {
            _mainCamera.fieldOfView = baseFOV;
        }

        TryInitializeBounds();

        Vector3 spawnPos = _mapGenerator.SpawnHexCenterPoint;
        float currentHeight = _mainCamera.transform.position.y;
        Vector3 offset = new Vector3(0, currentHeight, -currentHeight * 0.5f);
        Vector3 cameraTargetPos = spawnPos + offset;
        cameraTargetPos.y = Mathf.Clamp(cameraTargetPos.y, minZoomDistance, maxZoomDistance);

        _targetCameraPosition = cameraTargetPos;
        _mainCamera.transform.position = cameraTargetPos;
        _targetCameraRotation = _mainCamera.transform.rotation;
        ClampTargetToBounds(ref _targetCameraPosition);
        _mainCamera.transform.position = _targetCameraPosition;
    }

    // ==================== 一键恢复默认值 ====================
    private void Reset()
    {
        moveSpeed = 5f;
        smoothMoveTime = 0.1f;
        minZoomDistance = 20f;
        maxZoomDistance = 75f;
        zoomSpeed = 35f;
        smoothZoomTime = 0.1f;

        dragSensitivity = 0.012f;
        dragStartThreshold = 10f;
    }

    public void Tick()
    {
        if (_mainCamera == null) return;

        if (!_boundsInitialized)
        {
            TryInitializeBounds();
            if (_boundsInitialized)
            {
                ClampTargetToBounds(ref _targetCameraPosition);
                _mainCamera.transform.position = _targetCameraPosition;
                transform.position = _targetCameraPosition;
            }
        }

        HandleKeyboardInput();
        HandleMouseScroll();
        HandleMouseDrag();
        ApplySmoothTransform();
    }

    private void TryInitializeBounds()
    {
        var allCells = _mapData.GetAllCells();
        if (allCells == null || allCells.Count == 0) return;
        _minX = float.PositiveInfinity;
        _maxX = float.NegativeInfinity;
        _minZ = float.PositiveInfinity;
        _maxZ = float.NegativeInfinity;
        _boundsPlaneY = float.PositiveInfinity;

        foreach (var cell in allCells)
        {
            if (cell == null) continue;

            Vector3 center = cell.RealCenterWorldCoordinate;
            _minX = Mathf.Min(_minX, center.x);
            _maxX = Mathf.Max(_maxX, center.x);
            _minZ = Mathf.Min(_minZ, center.z);
            _maxZ = Mathf.Max(_maxZ, center.z);
            _boundsPlaneY = Mathf.Min(_boundsPlaneY, center.y);
        }

        if (float.IsInfinity(_minX)) return;

        float edgePaddingX = _config.OuterRadius + _config.fogCoverWidth;
        float edgePaddingZ = _config.OuterRadius + _config.fogCoverWidth * 0.5f;
        _minX -= edgePaddingX;
        _maxX += edgePaddingX;
        _minZ -= edgePaddingZ;
        _maxZ += edgePaddingZ;

        _boundsInitialized = true;
        //Debug.Log("CameraController 边界初始化成功");
    }

    private void HandleKeyboardInput()
    {
        if (_isDragging) return;

        float horizontal = _input.GetAxis("Horizontal");
        float vertical = _input.GetAxis("Vertical");

        if (Mathf.Abs(horizontal) > 0.1f || Mathf.Abs(vertical) > 0.1f)
        {
            Vector3 forward = transform.forward; forward.y = 0; forward.Normalize();
            Vector3 right = transform.right; right.y = 0; right.Normalize();

            Vector3 moveDirection = (forward * vertical + right * horizontal).normalized;
            _targetCameraPosition += moveDirection * moveSpeed * Time.deltaTime;
            ClampTargetToBounds(ref _targetCameraPosition);
        }
    }

    private void HandleMouseScroll()
    {
        if (_input.IsMultiTouch)
        {
            HandlePinchZoom();
            return;
        }

        _wasPinching = false;

        float scrollDelta = _input.MouseScrollDelta;
        if (scrollDelta == 0) return;

        Vector3 zoomMove = scrollDelta * _mainCamera.transform.forward * (zoomSpeed / 60f);
        float targetY = Mathf.Clamp(
            _targetCameraPosition.y + zoomMove.y,
            minZoomDistance,
            maxZoomDistance);

        if (Mathf.Approximately(zoomMove.y, 0f)) return;

        zoomMove *= (targetY - _targetCameraPosition.y) / zoomMove.y;
        _targetCameraPosition += zoomMove;
        ClampTargetToBounds(ref _targetCameraPosition);
    }

    // 双指捏合缩放：不依赖 deltaPosition（微信 WebGL 触摸符号不可靠），
    // 改用「相对捏合起点的间距比值」：ratio > 1 张开 = 拉近（地图放大），ratio < 1 合拢 = 拉远。
    private void HandlePinchZoom()
    {
        if (!_wasPinching)
        {
            _wasPinching = true;
            _pinchStartHeight = _targetCameraPosition.y;
        }

        float ratio = _input.PinchRatio;
        if (Mathf.Abs(ratio - 1f) < 0.001f) return;

        float targetY = Mathf.Clamp(_pinchStartHeight / ratio, minZoomDistance, maxZoomDistance);
        float deltaY = targetY - _targetCameraPosition.y;
        if (Mathf.Approximately(deltaY, 0f)) return;

        float forwardY = _mainCamera.transform.forward.y;
        if (Mathf.Approximately(forwardY, 0f)) return;

        Vector3 zoomMove = _mainCamera.transform.forward * (deltaY / forwardY);
        _targetCameraPosition += zoomMove;
        ClampTargetToBounds(ref _targetCameraPosition);
    }

    private void HandleMouseDrag()
    {
        // 卡牌拖拽由 EventSystem/UI 独占指针；否则指针离开卡牌进入地图后会被误判为相机拖拽。
        if (CardController.IsAnyCardDragging)
        {
            _isDragging = false;
            return;
        }

        if (_input.IsMultiTouch)
        {
            _isDragging = false;
            return;
        }

        if (_input.GetMouseButtonDown(0))
        {
            if (_input.IsPointerOverUI()) return;

            _dragStartPosition = _input.MousePosition;
            _lastMousePosition = _dragStartPosition;
            _isDragging = false;
            return;
        }

        if (_input.GetMouseButtonUp(0))
        {
            _isDragging = false;
            return;
        }

        if (!_input.GetMouseButton(0)) return;

        Vector3 currentPosition = _input.MousePosition;
        if (!_isDragging)
        {
            if ((currentPosition - _dragStartPosition).sqrMagnitude < dragStartThreshold * dragStartThreshold)
                return;

            _isDragging = true;
            _lastMousePosition = currentPosition;
            return;
        }

        Vector3 mouseDelta = currentPosition - _lastMousePosition;
        _lastMousePosition = currentPosition;

        if (mouseDelta.sqrMagnitude < 0.01f) return;

        Vector3 worldDelta = CalculateWorldDelta(mouseDelta);
        _targetCameraPosition -= worldDelta;
        ClampTargetToBounds(ref _targetCameraPosition);
    }

    private Vector3 CalculateWorldDelta(Vector3 screenDelta)
    {
        Vector3 cameraRight = _mainCamera.transform.right;
        cameraRight.y = 0;
        cameraRight.Normalize();

        Vector3 cameraForward = _mainCamera.transform.forward;
        cameraForward.y = 0;
        cameraForward.Normalize();

        // 用平方根缓和高度差异，避免高处拖动过快
        float heightFactor = Mathf.Sqrt(_mainCamera.transform.position.y) * dragSensitivity;
        return (cameraRight * screenDelta.x + cameraForward * screenDelta.y) * heightFactor;
    }

    private void ApplySmoothTransform()
    {
        if (_mainCamera == null) return;

        Vector3 smoothed = Vector3.SmoothDamp(_mainCamera.transform.position - _shakeOffset, _targetCameraPosition, ref _zoomVelocity, smoothZoomTime);

        UpdateShakeOffset();

        _mainCamera.transform.position = smoothed + _shakeOffset;
        _mainCamera.transform.rotation = _targetCameraRotation;
        transform.position = smoothed;
    }

    private void UpdateShakeOffset()
    {
        if (_shakeElapsed >= _shakeDuration)
        {
            _shakeOffset = Vector3.zero;
            return;
        }

        _shakeElapsed += Time.deltaTime;
        // 衰减包络：随时间线性降至 0
        float falloff = _shakeStrength * (1f - _shakeElapsed / _shakeDuration);
        // 高频正弦波 + 少量随机扰动，保证视觉上是快速细碎抖动而非大幅漂移
        float t = _shakeElapsed * FeelConfigProvider.CameraShakeFrequency;
        float x = Mathf.Sin(t * 1.0f) + Random.Range(-0.05f, 0.05f);
        float y = Mathf.Sin(t * 1.3f) + Random.Range(-0.05f, 0.05f);
        _shakeOffset = new Vector3(x * falloff, y * falloff, 0f);
    }

    /// <summary>相机目标位置（平滑前），供射线检测等需要即时坐标的场景使用。</summary>
    public Vector3 TargetCameraPosition => _targetCameraPosition;

    public void SetTargetPosition(Vector3 worldPosition)
    {
        _targetCameraPosition = worldPosition;
        ClampTargetToBounds(ref _targetCameraPosition);
    }

    private void ClampTargetToBounds(ref Vector3 position)
    {
        if (!_boundsInitialized || _mainCamera == null) return;

        _mainCamera.CalculateFrustumCorners(
            new Rect(0f, 0f, 1f, 1f),
            1f,
            Camera.MonoOrStereoscopicEye.Mono,
            _frustumCorners);

        float minOffsetX = float.PositiveInfinity;
        float maxOffsetX = float.NegativeInfinity;
        float minOffsetZ = float.PositiveInfinity;
        float maxOffsetZ = float.NegativeInfinity;

        foreach (Vector3 corner in _frustumCorners)
        {
            Vector3 direction = _targetCameraRotation * corner.normalized;
            if (direction.y >= -0.001f) return;

            float distance = (_boundsPlaneY - position.y) / direction.y;
            Vector3 offset = direction * distance;
            minOffsetX = Mathf.Min(minOffsetX, offset.x);
            maxOffsetX = Mathf.Max(maxOffsetX, offset.x);
            minOffsetZ = Mathf.Min(minOffsetZ, offset.z);
            maxOffsetZ = Mathf.Max(maxOffsetZ, offset.z);
        }

        position.x = ClampViewAxis(position.x, _minX, _maxX, minOffsetX, maxOffsetX);
        position.z = ClampViewAxis(position.z, _minZ, _maxZ, minOffsetZ, maxOffsetZ);
    }

    private static float ClampViewAxis(float position, float mapMin, float mapMax, float viewMinOffset, float viewMaxOffset)
    {
        float allowedMin = mapMin - viewMinOffset;
        float allowedMax = mapMax - viewMaxOffset;

        return allowedMin <= allowedMax
            ? Mathf.Clamp(position, allowedMin, allowedMax)
            : (allowedMin + allowedMax) * 0.5f;
    }
}
