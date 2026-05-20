// IUnitMovement.cs
using UnityEngine;
using System.Collections.Generic;

public interface IUnitMovement
{
    GameObject gameObject { get; }                // 单位自身
    Vector3 CurrentHexCoordinate { get; }         // 当前所在的六边形坐标
    float RemainingMovement { get; set; }         // 剩余移动力（允许系统修改）
    float MaxMovement { get; }                     // 最大移动力
    bool IsMoving { get; }                         // 是否正在移动

    /// <summary>移动到目标六边形</summary>
    void MoveTo(Vector3 targetHex, Enums.MovementPurpose purpose = Enums.MovementPurpose.MoveToDestination);
    /// <summary>取消当前移动</summary>
    void CancelMove();
    /// <summary>重置移动力（新回合时调用）</summary>
    void ResetMovement();

    /// <summary>获取当前单位可达的六边形坐标列表（基于剩余移动力）</summary>
    List<Vector3> GetReachableHexes();

    /// <summary>移动完成时的回调（由系统调用）</summary>
    void OnMoveFinished();
}