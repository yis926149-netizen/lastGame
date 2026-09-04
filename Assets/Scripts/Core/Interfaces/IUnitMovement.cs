// IUnitMovement.cs
using UnityEngine;
using System.Collections.Generic;

public interface IUnitMovement
{
    GameObject gameObject { get; }                // ��λ����
    Vector3 CurrentHexCoordinate { get; }         // ��ǰ���ڵ�����������
    float RemainingMovement { get; set; }         // ʣ���ƶ���������ϵͳ�޸ģ�
    float MaxMovement { get; }                     // ����ƶ���
    bool IsMoving { get; }                         // �Ƿ������ƶ�
    bool IsBusy { get; }                           // �ƶ��������������Ƿ����ڽ���

    /// <summary>�ƶ���Ŀ��������</summary>
    bool MoveTo(Vector3 targetHex, Enums.MovementPurpose purpose = Enums.MovementPurpose.MoveToDestination);
    /// <summary>ȡ����ǰ�ƶ�</summary>
    void CancelMove();
    /// <summary>�����ƶ������»غ�ʱ���ã�</summary>
    void ResetMovement();

    /// <summary>��ȡ��ǰ��λ�ɴ�������������б�������ʣ���ƶ�����</summary>
    List<Vector3> GetReachableHexes();

    /// <summary>�ƶ����ʱ�Ļص�����ϵͳ���ã�</summary>
    void OnMoveFinished();

    /// <summary>���߼���껺�棨��ϵͳ��� OnMoveFinished ��д transform.position �ҿ��ʱ���ã�</summary>
    void InvalidateHexCoordinateCache();
}
