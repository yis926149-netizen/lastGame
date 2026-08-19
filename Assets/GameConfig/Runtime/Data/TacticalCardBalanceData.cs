using System;

namespace GameConfig
{
    /// <summary>
    /// 战术卡数值（由 game-config.json 导入的只读数据）。
    /// effectType 使用稳定英文代码（Repair / BattleOrder），由 Provider 映射到既有枚举。
    /// </summary>
    [Serializable]
    public sealed class TacticalCardBalanceData
    {
        public string cardId;
        public int legacyId;
        public string displayName;
        public string description;
        public bool enabled;
        public string effectType;
        public float healRatio;
        public float unitHealRatio;
        public float attackMultiplier;
        public float speedMultiplier;
        public float duration;
        public int initialQuantity;
    }
}
