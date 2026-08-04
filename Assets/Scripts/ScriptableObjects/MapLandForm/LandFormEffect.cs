using UnityEngine;

/// <summary>
/// 地图地貌效果参数。
/// 对齐 ResourcePickupEffect 模式：内含所有效果类型的参数，按 effectType 取用。
/// </summary>
[System.Serializable]
public struct LandFormEffect
{
    [Tooltip("防御加数（DefenseBonus 用）：0.3 = 防御系数 +0.3")]
    public float defenseBonus;

    [Tooltip("每次回复最大生命比例（PeriodicHeal 用）：0.1 = 10%")]
    public float healRatio;

    [Tooltip("周期回血间隔（PeriodicHeal 用），秒")]
    public float healInterval;

    [Tooltip("占领该地貌所在格时每秒金币加成（GoldIncomeBoost 用）：2 = +2 金币/秒")]
    public float goldIncomePerSecond;
}
