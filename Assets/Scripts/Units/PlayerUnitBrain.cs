using UnityEngine;

//****************************************
// 功能说明：玩家阵营单位 Brain。
//   目标查询使用玩家迷雾（HexCellData.IsVisible），而非 AIFogService。
//
// 【检查点 2：搭架子】FindNearestEnemy / FindNearestEnemyBuilding 为空占位，
//   不接入任何现有逻辑。
//****************************************

public class PlayerUnitBrain : UnitBrainBase
{
    // 依赖通过属性注入或 Initialize 传入（检查点 3 接入）
    private IMapDataService _mapDataService;
    private IUnitRepository _unitRepository;

    public void Initialize(CharacterData owner, IUnitStrategy strategy,
                           IMapDataService mapDataService, IUnitRepository unitRepository)
    {
        Owner = owner;
        activeStrategy = strategy;
        _mapDataService = mapDataService;
        _unitRepository = unitRepository;
    }

    /// <summary>
    /// 在玩家已探索的视野（IsVisible）内找最近敌方单位。
    /// 【检查点 2】空占位，始终返回 null。
    /// </summary>
    public override Vector3? FindNearestEnemy()
    {
        // TODO（检查点 3 接入）：
        // 遍历 _unitRepository.AllEnemyUnitGroups，
        // 过滤 _mapDataService.GetCellByWorldPosition(pos).IsVisible，
        // 返回 Dijkstra 代价最小的坐标。
        return null;
    }

    /// <summary>
    /// 在玩家视野内找最近敌方建筑。
    /// 【检查点 2】空占位，始终返回 null。
    /// </summary>
    public override Vector3? FindNearestEnemyBuilding()
    {
        // TODO（检查点 3 接入）
        return null;
    }
}
