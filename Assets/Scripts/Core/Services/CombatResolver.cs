using UnityEngine;

//****************************************
// 功能说明：战斗逻辑结算器（纯数据层，与表现分离）。
//   瞬间结算一次攻击的伤害/死亡，不含任何动画或延时。
//   伤害公式复用现有 UnitMovementController.AttackDataComputation。
//   动画表现由 UnitMovementController.PlayAttackAnim 单独负责。
//
// 设计要点（见 5.5）：
//   - 瞬间结算：调用即完成扣血，动画只是回放。
//   - 无反击：单向施加伤害，防守方不回打。
//   - 可被 buff 装饰器改写（吸血、溅射等，后续检查点）。
//
// 【检查点 2：搭架子】空壳，方法未实现，尚未接入。
//   将在检查点 5 接管伤害结算。
//****************************************

public class CombatResolver
{
    /// <summary>
    /// 结算一次攻击：attacker 对 target 造成一次瞬间伤害。
    /// 【检查点 2】空实现，未接入。
    /// </summary>
    public void Resolve(CharacterData attacker, CharacterData target)
    {
        // TODO（检查点 5 接入）：
        // 1. 计算伤害（复用 AttackDataComputation 公式）
        // 2. target.currentHp -= damage（瞬间结算）
        // 3. 触发死亡检查（currentHp <= 0）
    }

    /// <summary>
    /// 结算一次对建筑的攻击。
    /// 【检查点 2】空实现，未接入。
    /// </summary>
    public void ResolveBuilding(CharacterData attacker, BuildingData target)
    {
        // TODO（检查点 5 接入）
    }
}
