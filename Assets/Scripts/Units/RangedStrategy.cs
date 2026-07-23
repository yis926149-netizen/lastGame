using UnityEngine;

//****************************************
// 功能说明：远程兵种策略。
//   攻击范围 range >= 1（由 UnitData.BasicAttackRange 决定）。
//   ChooseNextStep：朝视野内最近敌方目标方向走一格（缩短至射程内即停止移动）。
//   CanAttack：外扩 range 环内有敌方单位时为 true。
//   DoCombat：对射程内敌方单位发起远程攻击（委托 CombatResolver，不进入目标格）。
//
// 【检查点 2：搭架子】所有方法为空占位，尚未接入任何逻辑。
//****************************************

public class RangedStrategy : IUnitStrategy
{
    public Vector3? ChooseNextStep(UnitBrainBase brain)
    {
        // TODO（检查点 3/5 接入）：
        // 1. brain.FindNearestEnemy() 找目标
        // 2. 若目标已在射程内则不移动（返回 null）
        // 3. 否则朝目标方向走一格
        return null;
    }

    public bool CanAttack(UnitBrainBase brain)
    {
        // TODO（检查点 5 接入）：
        // 扫描外扩 BasicAttackRange 环内是否有敌方单位
        return false;
    }

    public void DoCombat(UnitBrainBase brain)
    {
        // TODO（检查点 5 接入）：
        // 原地发起远程攻击，CombatResolver.Resolve(brain.Owner, target)
    }
}
