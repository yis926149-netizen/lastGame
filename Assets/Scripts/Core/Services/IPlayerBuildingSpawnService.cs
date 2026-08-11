using UnityEngine;

/// <summary>
/// 玩家建筑生成服务接口。
/// 用于探索奖励系统等模块在指定位置生成玩家建筑。
/// </summary>
public interface IPlayerBuildingSpawnService
{
    /// <summary>
    /// 在指定世界坐标生成玩家建筑。调用方需自行校验目标格建造资格
    /// （本方法复刻卡牌路径的 SpawnBuilding，内部不做放置校验）。
    /// </summary>
    /// <param name="buildingID">建筑 ID（BuildingConfigSO.buildingId）</param>
    /// <param name="worldPosition">生成的世界坐标</param>
    /// <returns>生成成功返回 true，失败返回 false</returns>
    bool SpawnPlayerBuilding(int buildingID, Vector3 worldPosition);
}
