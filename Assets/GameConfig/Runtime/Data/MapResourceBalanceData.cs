using System;

namespace GameConfig
{
    /// <summary>
    /// 地图资源数值（由 game-config.json 导入的只读数据）。
    /// 图标/模型/特效/音效等资源引用保留在手工资源 SO（MapResourceSO），数值进本表。
    /// pickupEffectType 使用英文代码（None / AttackBoost / Heal / DefenseBoost / Gold）。
    /// </summary>
    [Serializable]
    public sealed class MapResourceBalanceData
    {
        public string resourceId;
        public string resourceName;
        public string description;
        public bool enabled;
        public string pickupEffectType;
        public float attackBonus;
        public float healRatio;
        public float defenseBonus;
        public int goldAmount;
        public int explorationGoldBonus;
        public int spawnWeight;
    }
}
