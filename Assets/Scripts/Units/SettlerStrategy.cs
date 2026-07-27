using System.Collections.Generic;
using System.Linq;
using UnityEngine;

//****************************************
// 功能说明：移民兵种策略。
//   CanAttack 恒 false（移民不参与战斗）。
//   ChooseNextPath：先尝试在当前格建城；不满足则找合法建城目标格并返回完整路径。
//   DoCombat：不实现（永远不会被调用）。
//
// 【批次 B】接入实际逻辑，迁移自 AITacticalBrain.HandleSettlerTurn（玩家视野版）。
//****************************************

public class SettlerStrategy : IUnitStrategy
{
    public List<Vector3> ChooseNextPath(UnitBrainBase brain)
    {
        // 【探索重构-阶段5.5】建城功能移除，移民单位暂时停留原地（选项C）。
        // 后续可改造为"自动探索"兵种：移动到边界未探索格并调用 ExplorationService.TryExplore。
        return null;
    }

    public bool CanAttack(UnitBrainBase brain) => false;

    public void DoCombat(UnitBrainBase brain)
    {
        // 移民不攻击，永不调用
    }
}
