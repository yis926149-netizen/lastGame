using System;
using UnityEngine;

//****************************************
// 功能说明：伤害表现事件总线（数据层 → 表现层）。
//   CombatResolver（单位）/ BuildingBase（建筑）/ ArrowTowerShooter（箭塔）在结算伤害后发布；
//   DamageFloatTextRenderer 订阅后在屏幕空间播放飘字。
//   数据层只依赖本总线，不依赖任何 UI，保持结算与表现分离（对齐批次 D 约定）。
//****************************************

public class DamageEventBroker
{
    /// <summary>
    /// 伤害已结算事件。
    /// 参数：受击者头顶世界坐标（飘字锚点）、伤害值、是否暴击（当前公式无暴击，预留）、
    ///       受击者阵营（0 = 我方；-1 = 中立；>=1 = 敌方）。
    /// </summary>
    public event Action<Vector3, float, bool, int> DamageDealt;

    public void RaiseDamage(Vector3 anchorWorldPosition, float damage, bool isCrit = false, int targetFaction = -1)
    {
        DamageDealt?.Invoke(anchorWorldPosition, damage, isCrit, targetFaction);
    }

    /// <summary>
    /// 受击表现事件。参数：受击对象根节点（单位为 model，建筑为自身 gameObject）。
    /// 与 DamageDealt 分离：飘字与受击反馈的过滤规则（如迷雾）、生命周期、参数各不相同。
    /// </summary>
    public event Action<GameObject> UnitHit;

    public void RaiseHit(GameObject target)
    {
        if (target == null) return;
        UnitHit?.Invoke(target);
    }
}
