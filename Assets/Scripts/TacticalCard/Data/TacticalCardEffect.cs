using System;
using UnityEngine;

[System.Serializable]
public struct TacticalCardEffect
{
    [Tooltip("回复比例（维修用）：0.3 = 恢复 30% 最大 HP")]
    public float healRatio;

    [Tooltip("单位回复比例（维修用）：0.6 = 恢复 60% 最大 HP；<=0 时回落到 healRatio")]
    public float unitHealRatio;

    [Tooltip("攻击力提升乘数（战斗号令用）：1.3 = +30%")]
    public float attackMultiplier;

    [Tooltip("移速提升乘数（战斗号令用）：1.2 = +20%")]
    public float speedMultiplier;

    [Tooltip("持续时间（战斗号令用），秒")]
    public float duration;

    [Tooltip("影响范围半径（n 环）。1 = 落点及其周围 6 格，2 = 19 格口径…… 遮罩与结算共用同一值")]
    public int effectRadius;
}
