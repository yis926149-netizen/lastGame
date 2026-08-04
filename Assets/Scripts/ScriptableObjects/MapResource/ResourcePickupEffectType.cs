using UnityEngine;

/// <summary>
/// 地图资源拾取效果类型。
/// 【地图资源配置化】由 MapResourceSO.pickupEffectType 配置，执行端为 MapResourceCollectionService。
/// </summary>
public enum ResourcePickupEffectType
{
    None          = 0, // 拾取无效果
    AttackBoost   = 1, // 提升下一次攻击力（动物）
    Heal          = 2, // 立即回血（植物）
    DefenseBoost  = 3, // 提升下一次防御力（矿物）
    Gold          = 4, // 获得金币（宝箱）
}
