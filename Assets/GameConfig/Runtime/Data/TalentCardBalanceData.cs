using System;

namespace GameConfig
{
    /// <summary>
    /// 天赋卡数值（由 game-config.json 导入的只读数据）。
    /// effectType / statId 使用稳定英文代码，由 Provider 映射到既有枚举。
    /// </summary>
    [Serializable]
    public sealed class TalentCardBalanceData
    {
        public string talentId;
        public int legacyId;
        public string talentName;
        public string description;
        public bool enabled;
        public string effectType;
        public string statId;
        public float value;
        public int weight;
        public bool repeatable;
    }
}
