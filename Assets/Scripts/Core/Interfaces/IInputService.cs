using UnityEngine;

public interface IInputService
{
    // 鼠标状态
    Vector3 MousePosition { get; }
    bool GetMouseButtonDown(int button);
    bool GetMouseButtonUp(int button);
    bool GetMouseButton(int button);

    // 键盘
    bool GetKey(KeyCode key);
    bool GetKeyDown(KeyCode key);
    bool GetKeyUp(KeyCode key);

    // 轴输入（用于摄像机移动）
    float GetAxis(string axisName);

    // 鼠标滚轮
    float MouseScrollDelta { get; }

    // UI 遮挡检测（可指定目标 Canvas，null 则检测所有 UI）
    bool IsPointerOverUI(Canvas targetCanvas = null);

    // 射线检测（世界坐标）
    bool RaycastFromScreen(Vector2 screenPos, out RaycastHit hit, float maxDistance, LayerMask layerMask);
}