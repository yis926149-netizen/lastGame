using UnityEngine;

/// <summary>
/// 玩家单位生成服务接口。
/// 用于探索奖励系统等模块在指定位置生成玩家单位。
/// </summary>
public interface IPlayerUnitSpawnService
{
    /// <summary>
    /// 在指定世界坐标生成玩家单位。
    /// </summary>
    /// <param name="unitID">单位 ID（UnitDatabase 中的索引）</param>
    /// <param name="worldPosition">生成的世界坐标</param>
    /// <returns>生成的单位 GameObject，失败返回 null</returns>
    GameObject SpawnPlayerUnit(int unitID, Vector3 worldPosition);
}
