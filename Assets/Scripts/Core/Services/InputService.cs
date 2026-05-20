// Services/InputService.cs
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI; // 新增

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

    public bool IsPointerOverUI()
    {
        // -1 = 鼠标在 Standalone/Editor 下可靠；无参版本在部分构建里对鼠标不稳定
        return _eventSystem != null && _eventSystem.IsPointerOverGameObject(-1);
    }

    // 检测 UI：targetCanvas 为 null 时检测任意 UI；否则只检测该 Canvas 下的 GraphicRaycaster
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
}