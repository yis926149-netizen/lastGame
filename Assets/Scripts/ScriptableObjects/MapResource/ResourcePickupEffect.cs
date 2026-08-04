using UnityEngine;

/// <summary>
/// 地图资源拾取效果参数。
/// 对齐 TacticalCardEffect 模式：内含所有效果类型的参数，按 pickupEffectType 取用。
/// </summary>
[System.Serializable]
public struct ResourcePickupEffect
{
    [Tooltip("攻击力固定加成（AttackBoost 用）：0.7 = +0.7 攻")]
    public float attackBonus;

    [Tooltip("回复比例（Heal 用）：0.25 = 回 25% 最大 HP")]
    public float healRatio;

    [Tooltip("防御力固定加成（DefenseBoost 用）：0.25 = +0.25 防")]
    public float defenseBonus;

    [Tooltip("金币数量（Gold 用）：50 = +50 金")]
    public int goldAmount;
}
