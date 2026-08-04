using System;
using UnityEngine;

[System.Serializable]
public struct TacticalCardEffect
{
    [Tooltip("回复比例（维修用）：0.3 = 恢复 30% 最大 HP")]
    public float healRatio;

    [Tooltip("攻击力提升乘数（战斗号令用）：1.3 = +30%")]
    public float attackMultiplier;

    [Tooltip("移速提升乘数（战斗号令用）：1.2 = +20%")]
    public float speedMultiplier;

    [Tooltip("持续时间（战斗号令用），秒")]
    public float duration;
}
