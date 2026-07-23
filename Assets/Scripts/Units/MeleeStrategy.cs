using UnityEngine;

//****************************************
// 功能说明：近战兵种策略。
//   攻击范围 range = 0（必须走进敌人所在格才能攻击）。
//   ChooseNextStep：朝视野内最近敌方目标方向走一格。
//   CanAttack：本格内有敌方单位时为 true。
//   DoCombat：对同格敌方单位发起攻击（委托 CombatResolver）。
//
// 【检查点 2：搭架子】所有方法为空占位，尚未接入任何逻辑。
//****************************************

public class MeleeStrategy : IUnitStrategy
{
    public Vector3? ChooseNextStep(UnitBrainBase brain)
    {
        // TODO（检查点 3/5 接入）：
        // 1. brain.FindNearestEnemy() 找目标
        // 2. UnitMovementSystem.CalculateMinMovementCostBetweenTwoHexes 找路径
        // 3. 返回路径第一格（相邻格）
        return null;
    }

    public bool CanAttack(UnitBrainBase brain)
    {
        // TODO（检查点 5 接入）：
        // 检查本格（range=0）内是否有敌方单位
        return false;
    }

    public void DoCombat(UnitBrainBase brain)
    {
        // TODO（检查点 5 接入）：
        // CombatResolver.Resolve(brain.Owner, target)
        // 播放攻击动画
    }
}
