using System.Linq;
using UnityEngine;
using Zenject;

public class CameraController : MonoBehaviour, ITickable
{
    private IInputService _input;
    private IMapDataService _mapData;
    private MapGenerationConfigSO _config;
    private IUIConfigProvider _uiConfig;
    private MapGenerator _mapGenerator;

    private Camera _mainCamera;

    [Header("摄像机移动设置")]
    public float moveSpeed = 5f;
    public float smoothMoveTime = 0.1f;
    private float _minX = -50f;
    private float _maxX = 50f;
    private float _minZ = -50f;
    private float _maxZ = 50f;

    [Header("摄像机缩放设置")]
    public float minZoomDistance = 20f;
    public float maxZoomDistance = 50f;
    public float zoomSpeed = 0.1f;
    public float smoothZoomTime = 0.1f;

    [Header("摄像机旋转设置")]
    public float rotateSpeed = 30f;                   
    public float smoothRotateTime = 0.1f;            
    public float returnRotateSpeed = 60f;           
    public float defaultRotationDistance = 50f;       
    public LayerMask rotationRaycastLayers;

    private Vector3 _targetCameraPosition;
    private Quaternion _targetCameraRotation;

    public float returnDuration = 0.2f;   // ← 回归动画时长（秒），改这个数字就能调快慢
    private float _returnStartTime;       // ← 记录开始回归的时间

    // 旋转状态记录
    private Vector3 _rotationStartPosition;
    private Quaternion _rotationStartRotation;
    private bool _isReturningToStart = false;
    private Vector3 _currentRotationCenter;
    private float _returnTotalAngle;
    private float _targetReturnAngle;
    private float _currentReturnAngle;
    private bool _isRotating = false;
    private float _rotationYVelocity;

    private Vector3 _zoomVelocity;

    private bool _boundsInitialized = false;

    [Inject]
    public void Construct(
        IInputService input,
        IMapDataService mapData,
        MapGenerationConfigSO config,
        IUIConfigProvider uiConfig,
        MapGenerator mapGenerator)
    {
        _input = input;
        _mapData = mapData;
        _config = config;
        _uiConfig = uiConfig;
        _mapGenerator = mapGenerator;
    }

    private void Start()
    {
        _mainCamera = Camera.main;
        if (_mainCamera == null)
        {
            Debug.LogError("CameraController: 场景中没有主相机！");
            return;
        }

        TryInitializeBounds();

        Vector3 spawnPos = _mapGenerator.SpawnHexCenterPoint;
        float currentHeight = _mainCamera.transform.position.y;
        Vector3 offset = new Vector3(0, currentHeight, -currentHeight * 0.5f);
        Vector3 cameraTargetPos = spawnPos + offset;

        _targetCameraPosition = cameraTargetPos;
        _mainCamera.transform.position = cameraTargetPos;
        _mainCamera.transform.LookAt(spawnPos);
        _targetCameraRotation = _mainCamera.transform.rotation;
    }

    // ==================== 一键恢复默认值 ====================
    private void Reset()
    {
        moveSpeed = 5f;
        smoothMoveTime = 0.1f;
        minZoomDistance = 20f;
        maxZoomDistance = 50f;
        zoomSpeed = 35f;
        smoothZoomTime = 0.1f;

        rotateSpeed = 30f;
        smoothRotateTime = 0.1f;
        returnRotateSpeed = 60f;
        defaultRotationDistance = 50f;

        returnDuration = 0.2f;
    }

    public void Tick()
    {
        if (_mainCamera == null) return;

        if (!_boundsInitialized)
            TryInitializeBounds();

        if (_isReturningToStart)
        {
            HandleReturnToStart();
            return;
        }

        HandleKeyboardInput();
        HandleMouseScroll();
        HandleRotationInput();
        ApplySmoothTransform();
    }

    private void TryInitializeBounds()
    {
        var allCells = _mapData.GetAllCells();
        if (allCells == null || allCells.Count == 0) return;
        int xCount = _config.xNumber;
        if (xCount <= 0 || allCells.Count < xCount) return;

        var lowerLeftCell = allCells[0];
        var lowerRightCell = allCells[xCount - 1];
        var topLeftCell = allCells[allCells.Count - xCount];

        if (lowerLeftCell == null || lowerRightCell == null || topLeftCell == null) return;

        Vector3 lowerLeft = lowerLeftCell.RealCenterWorldCoordinate;
        Vector3 lowerRight = lowerRightCell.RealCenterWorldCoordinate;
        Vector3 topLeft = topLeftCell.RealCenterWorldCoordinate;

        _minX = lowerLeft.x + 13f;
        _maxX = lowerRight.x - 13f;
        _minZ = lowerLeft.z - 5f;
        _maxZ = topLeft.z - 20f;

        _boundsInitialized = true;
        //Debug.Log("CameraController 边界初始化成功");
    }

    private void HandleKeyboardInput()
    {
        if (_isRotating || _isReturningToStart) return;

        float horizontal = _input.GetAxis("Horizontal");
        float vertical = _input.GetAxis("Vertical");

        if (Mathf.Abs(horizontal) > 0.1f || Mathf.Abs(vertical) > 0.1f)
        {
            if (!_input.GetKey(KeyCode.LeftControl) && !_input.GetKey(KeyCode.RightControl))
            {
                Vector3 forward = transform.forward; forward.y = 0; forward.Normalize();
                Vector3 right = transform.right; right.y = 0; right.Normalize();

                Vector3 moveDirection = (forward * vertical + right * horizontal).normalized;
                _targetCameraPosition += moveDirection * moveSpeed * Time.deltaTime;

                _targetCameraPosition.x = Mathf.Clamp(_targetCameraPosition.x, _minX, _maxX);
                _targetCameraPosition.z = Mathf.Clamp(_targetCameraPosition.z, _minZ, _maxZ);
            }
        }
    }

    private void HandleMouseScroll()
    {
        float scrollDelta = _input.MouseScrollDelta;
        if (scrollDelta == 0) return;

        Vector3 zoomMove = scrollDelta * _mainCamera.transform.forward * zoomSpeed * Time.deltaTime;
        Vector3 newTarget = _targetCameraPosition + zoomMove;

        float newHeight = newTarget.y;
        if (newHeight >= minZoomDistance && newHeight <= maxZoomDistance)
            _targetCameraPosition = newTarget;
        else if (newHeight < minZoomDistance)
            _targetCameraPosition = new Vector3(newTarget.x, minZoomDistance, newTarget.z);
        else if (newHeight > maxZoomDistance)
            _targetCameraPosition = new Vector3(newTarget.x, maxZoomDistance, newTarget.z);
    }

    // ====================== 旋转逻辑（带详细日志） ======================
    private void HandleRotationInput()
    {
        if (_mainCamera == null) return;

        //Debug.Log($"[Camera Rotation Debug] HandleRotationInput 被调用 | Ctrl: {_input.GetKey(KeyCode.LeftControl) || _input.GetKey(KeyCode.RightControl)} | _isRotating: {_isRotating}");

        var canvasList = _uiConfig.RuntimeCanvases;
        bool isCtrlPressed = _input.GetKey(KeyCode.LeftControl) || _input.GetKey(KeyCode.RightControl);

        if (!isCtrlPressed)
        {
            if (_isRotating)
            {
                StartReturnRotation();
                _isRotating = false;
            }
            return;
        }

        float rotateAmount = 0f;
        if (_input.GetKey(KeyCode.A))
        {
            rotateAmount = rotateSpeed * Time.deltaTime;
            //Debug.Log($"[Camera Rotation Debug] ★ Ctrl + A 按下！rotateAmount = {rotateAmount:F3}");
            HideAllCanvases();
        }
        else if (_input.GetKey(KeyCode.D))
        {
            rotateAmount = -rotateSpeed * Time.deltaTime;
            //Debug.Log($"[Camera Rotation Debug] ★ Ctrl + D 按下！rotateAmount = {rotateAmount:F3}");
            HideAllCanvases();
        }
        else
        {
            if (_isRotating)
            {
                StartReturnRotation();
                _isRotating = false;
            }
            return;
        }

        if (rotateAmount != 0)
        {
            if (!_isRotating)
            {
                _currentRotationCenter = GetScreenCenterWorldPosition();
                _rotationStartPosition = _targetCameraPosition;
                _rotationStartRotation = _targetCameraRotation;
                _isRotating = true;
                //Debug.Log($"[Camera Rotation Debug] 首次旋转初始化完成 | 旋转中心 = {_currentRotationCenter}");
            }

            Vector3 offsetFromCenter = _targetCameraPosition - _currentRotationCenter;
            Vector3 oldPos = _targetCameraPosition;
            float oldRotY = _targetCameraRotation.eulerAngles.y;

            offsetFromCenter = Quaternion.Euler(0, rotateAmount, 0) * offsetFromCenter;
            _targetCameraPosition = _currentRotationCenter + offsetFromCenter;
            _targetCameraRotation = Quaternion.LookRotation(_currentRotationCenter - _targetCameraPosition);

            //Debug.Log($"[Camera Rotation Debug] 旋转已应用 | rotateAmount={rotateAmount:F3} | 位置变化 | Y旋转: {oldRotY:F1}° → {_targetCameraRotation.eulerAngles.y:F1}°");
        }
    }

    private void HideAllCanvases()
    {
        var canvasList = _uiConfig.RuntimeCanvases;
        foreach (Canvas c in canvasList.ToList())
        {
            if (c == null)
            {
                canvasList.Remove(c);
                continue;
            }
            c.gameObject.SetActive(false);
        }
    }

    private void StartReturnRotation()
    {
        _isReturningToStart = true;
        Vector3 startToCenter = _rotationStartPosition - _currentRotationCenter;
        Vector3 currentToCenter = _targetCameraPosition - _currentRotationCenter;
        _returnTotalAngle = Vector3.SignedAngle(currentToCenter, startToCenter, Vector3.up);

        if (_returnTotalAngle > 180) _returnTotalAngle -= 360;
        else if (_returnTotalAngle < -180) _returnTotalAngle += 360;

        _targetReturnAngle = _returnTotalAngle;
        _currentReturnAngle = 0;

        _returnStartTime = Time.time;   // 记录开始回归的时间
    }

    private void HandleReturnToStart()
    {
        if (_mainCamera == null) return;

        // 恢复画布
        var canvasList = _uiConfig.RuntimeCanvases;
        foreach (Canvas c in canvasList)
        {
            if (c != null && _mapData.GetCellByWorldPosition(c.transform.parent.position).IsExplored)
                c.gameObject.SetActive(true);
        }

        // ==================== 固定0.3秒内完成回归 ====================
        float elapsed = Time.time - _returnStartTime;
        float t = Mathf.Clamp01(elapsed / returnDuration);           // t 从 0 → 1，正好 0.3 秒
        float targetAngle = Mathf.LerpAngle(0f, _targetReturnAngle, t);

        float rotateAngle = targetAngle - _currentReturnAngle;
        _currentReturnAngle = targetAngle;

        Vector3 offsetFromCenter = _targetCameraPosition - _currentRotationCenter;
        offsetFromCenter = Quaternion.Euler(0, rotateAngle, 0) * offsetFromCenter;
        _targetCameraPosition = _currentRotationCenter + offsetFromCenter;
        _targetCameraRotation = Quaternion.LookRotation(_currentRotationCenter - _targetCameraPosition);

        if (t >= 1f || Mathf.Abs(_currentReturnAngle - _targetReturnAngle) < 0.1f)
        {
            _isReturningToStart = false;
            _targetCameraPosition = _rotationStartPosition;
            _targetCameraRotation = _rotationStartRotation;
        }

        ApplySmoothTransform();
    }

    private Vector3 GetScreenCenterWorldPosition()
    {
        if (_mainCamera == null) return Vector3.zero;

        Ray centerRay = _mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        if (Physics.Raycast(centerRay, out RaycastHit hit, Mathf.Infinity, rotationRaycastLayers))
            return hit.point;

        return _mainCamera.transform.position + _mainCamera.transform.forward * defaultRotationDistance;
    }

    private void ApplySmoothTransform()
    {
        if (_mainCamera == null) return;

        _mainCamera.transform.position = Vector3.SmoothDamp(_mainCamera.transform.position, _targetCameraPosition, ref _zoomVelocity, smoothZoomTime);

        float targetY = _targetCameraRotation.eulerAngles.y;
        float currentY = _mainCamera.transform.rotation.eulerAngles.y;
        float smoothY = Mathf.SmoothDampAngle(currentY, targetY, ref _rotationYVelocity, smoothRotateTime);

        Vector3 newEuler = _mainCamera.transform.rotation.eulerAngles;
        newEuler.y = smoothY;
        _mainCamera.transform.rotation = Quaternion.Euler(newEuler);

        transform.position = _mainCamera.transform.position;
    }

    public void SetTargetPosition(Vector3 worldPosition)
    {
        _targetCameraPosition = worldPosition;
    }
}