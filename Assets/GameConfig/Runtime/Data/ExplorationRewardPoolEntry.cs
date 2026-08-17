using System;

namespace GameConfig
{
    /// <summary>
    /// 探索奖励池条目（由 game-config.json 导入的只读数据）。
    /// rewardType 为英文代码（MilitaryUnit / TacticalCard / Building），
    /// configId 跨表引用 单位.unitId / 战术卡.cardId / 建筑.buildingId。
    /// </summary>
    [Serializable]
    public sealed class ExplorationRewardPoolEntry
    {
        public string poolId;
        public string rewardType;
        public string configId;
        public int weight;
        public bool enabled;
    }
}
