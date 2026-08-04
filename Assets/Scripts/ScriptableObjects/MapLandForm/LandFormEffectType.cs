using UnityEngine;

/// <summary>
/// 地图地貌效果类型。
/// 【地图地貌配置化】由 MapLandFormSO.effectType 配置，执行端为 LandFormEffectRule。
/// 森林/石头第一版配置为 None（纯视觉地貌，无任何效果）。
/// </summary>
public enum LandFormEffectType
{
    None             = 0, // 无效果（森林、石头）
    DefenseBonus     = 1, // 防御加数（大骨阵）
    PeriodicHeal     = 2, // 周期回血（农田）
    GoldIncomeBoost  = 3, // 占领地块时被动金币收入加成（金矿）
}
