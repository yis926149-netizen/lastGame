using UnityEngine;

//****************************************
// 功能说明：单位行为策略接口（按兵种组合注入 UnitBrainBase）。
//           每个兵种一个基础策略实现；buff 生效时用装饰器链包裹（见后续检查点）。
//
// 【检查点 2：搭架子】当前仅定义接口，尚未接入任何逻辑。
//****************************************

public interface IUnitStrategy
{
    /// <summary>
    /// 计算完整路径到目标（棋盘坐标列表）。无可走目标时返回 null。
    /// 返回的路径为 [下一格, 下下格, ..., 终点]，调用方可逐格消费。
    /// </summary>
    System.Collections.Generic.List<Vector3> ChooseNextPath(UnitBrainBase brain);

    /// <summary>当前是否可以发起攻击（近战：同格；远程：射程内有目标）。</summary>
    bool CanAttack(UnitBrainBase brain);

    /// <summary>执行一次攻击（逻辑结算委托 CombatResolver，表现委托控制器）。</summary>
    void DoCombat(UnitBrainBase brain);
}
