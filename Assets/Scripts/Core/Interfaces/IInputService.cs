using UnityEngine;

public interface IInputService
{
    // ���״̬
    Vector3 MousePosition { get; }
    bool GetMouseButtonDown(int button);
    bool GetMouseButtonUp(int button);
    bool GetMouseButton(int button);

    // ����
    bool GetKey(KeyCode key);
    bool GetKeyDown(KeyCode key);
    bool GetKeyUp(KeyCode key);

    // �����루����������ƶ���
    float GetAxis(string axisName);

    // ������
    float MouseScrollDelta { get; }
    float PinchDelta { get; }
    bool IsMultiTouch { get; }

    // UI �ڵ���⣨��ָ��Ŀ�� Canvas��null �������� UI��
    bool IsPointerOverUI(Canvas targetCanvas = null);

    // ���߼�⣨�������꣩
    bool RaycastFromScreen(Vector2 screenPos, out RaycastHit hit, float maxDistance, LayerMask layerMask);
    RaycastHit[] RaycastAllFromScreen(Vector2 screenPos, float maxDistance, LayerMask layerMask);
}
