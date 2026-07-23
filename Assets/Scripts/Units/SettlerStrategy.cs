using UnityEngine;

//****************************************
// 功能说明：移民兵种策略。
//   CanAttack 恒 false（移民不参与战斗）。
//   ChooseNextStep：在视野内/势力范围内找合法空地建城，朝目标格方向走一格。
//   DoCombat：不实现（永远不会被调用）。
//
// 【检查点 2：搭架子】所有方法为空占位，尚未接入任何逻辑。
//****************************************

public class SettlerStrategy : IUnitStrategy
{
    public Vector3? ChooseNextStep(UnitBrainBase brain)
    {
        // TODO（检查点 3 接入）：
        // 迁移 AITacticalBrain.HandleSettlerTurn 中建城目标选择逻辑（角色对调）
        return null;
    }

    public bool CanAttack(UnitBrainBase brain)
    {
        // 移民不攻击
        return false;
    }

    public void DoCombat(UnitBrainBase brain)
    {
        // 移民无攻击逻辑，永不调用
    }
}
