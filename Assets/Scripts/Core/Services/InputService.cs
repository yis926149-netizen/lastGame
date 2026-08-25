// Services/InputService.cs
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI; // ����

public class InputService : IInputService
{
    private readonly EventSystem _eventSystem;
    private readonly Camera _mainCamera;

    public InputService(EventSystem eventSystem, Camera mainCamera)
    {
        _eventSystem = eventSystem;
        _mainCamera = mainCamera;
    }

    public Vector3 MousePosition => Input.mousePosition;

    public bool GetMouseButtonDown(int button) => Input.GetMouseButtonDown(button);
    public bool GetMouseButtonUp(int button) => Input.GetMouseButtonUp(button);
    public bool GetMouseButton(int button) => Input.GetMouseButton(button);

    public bool GetKey(KeyCode key) => Input.GetKey(key);
    public bool GetKeyDown(KeyCode key) => Input.GetKeyDown(key);
    public bool GetKeyUp(KeyCode key) => Input.GetKeyUp(key);

    public float GetAxis(string axisName) => Input.GetAxis(axisName);
    public float MouseScrollDelta => Input.mouseScrollDelta.y;
    public bool IsMultiTouch => Input.touchCount >= 2;

    private bool _pinching;
    private float _pinchStartDistance = 1f;

    public float PinchRatio
    {
        get
        {
            if (Input.touchCount < 2)
            {
                _pinching = false;
                return 1f;
            }

            float currentDistance = Vector2.Distance(Input.GetTouch(0).position, Input.GetTouch(1).position);
            if (currentDistance < 0.0001f) return 1f;

            if (!_pinching)
            {
                _pinching = true;
                _pinchStartDistance = currentDistance;
                return 1f;
            }

            return currentDistance / _pinchStartDistance;
        }
    }

    public bool IsPointerOverUI()
    {
        if (_eventSystem == null) return false;
        // 触屏设备使用实际 fingerId，编辑器/PC 端使用 -1（鼠标）
        if (Input.touchCount > 0)
            return _eventSystem.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
        return _eventSystem.IsPointerOverGameObject(-1);
    }

    // ��� UI��targetCanvas Ϊ null ʱ������� UI������ֻ���� Canvas �µ� GraphicRaycaster
    public bool IsPointerOverUI(Canvas targetCanvas = null)
    {
        if (targetCanvas == null)
            return IsPointerOverUI();

        var raycaster = targetCanvas.GetComponent<GraphicRaycaster>();
        if (raycaster == null || _eventSystem == null)
            return false;

        var pointerData = new PointerEventData(_eventSystem);
        pointerData.position = Input.mousePosition;
        var results = new System.Collections.Generic.List<RaycastResult>();
        raycaster.Raycast(pointerData, results);
        return results.Count > 0;
    }

    public bool RaycastFromScreen(Vector2 screenPos, out RaycastHit hit, float maxDistance, LayerMask layerMask)
    {
        Ray ray = _mainCamera.ScreenPointToRay(screenPos);
        return Physics.Raycast(ray, out hit, maxDistance, layerMask);
    }

    public RaycastHit[] RaycastAllFromScreen(Vector2 screenPos, float maxDistance, LayerMask layerMask)
    {
        Ray ray = _mainCamera.ScreenPointToRay(screenPos);
        RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, layerMask);
        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        return hits;
    }
}
