using UnityEngine;

//****************************************
// 功能说明：AI 阵营单位 Brain。
//   目标查询使用 AIFogService（逻辑迷雾），而非玩家 IsVisible。
//   现有 AITacticalBrain 的战术逻辑将在检查点 4 迁移至此。
//
// 【检查点 2：搭架子】FindNearestEnemy / FindNearestEnemyBuilding 为空占位，
//   不接入任何现有逻辑，AITacticalBrain 仍正常运行。
//****************************************

public class AIUnitBrain : UnitBrainBase
{
    private IMapDataService _mapDataService;
    private IUnitRepository _unitRepository;
    private AIFogService _aiFog;

    public void Initialize(CharacterData owner, IUnitStrategy strategy,
                           IMapDataService mapDataService,
                           IUnitRepository unitRepository,
                           AIFogService aiFog)
    {
        Owner = owner;
        activeStrategy = strategy;
        _mapDataService = mapDataService;
        _unitRepository = unitRepository;
        _aiFog = aiFog;
    }

    /// <summary>
    /// 在 AI 逻辑迷雾（AIFogService.ComputeVisible）内找最近玩家单位。
    /// 【检查点 2】空占位，始终返回 null。
    /// </summary>
    public override Vector3? FindNearestEnemy()
    {
        // TODO（检查点 4 接入）：
        // 迁移 AITacticalBrain.HandleSingleUnitTurn 中目标查找逻辑。
        return null;
    }

    /// <summary>
    /// 在 AI 逻辑迷雾内找最近玩家建筑。
    /// 【检查点 2】空占位，始终返回 null。
    /// </summary>
    public override Vector3? FindNearestEnemyBuilding()
    {
        // TODO（检查点 4 接入）
        return null;
    }
}
