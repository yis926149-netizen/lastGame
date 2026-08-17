using System;

namespace GameConfig
{
    /// <summary>
    /// AI 配置（单行，由 game-config.json 导入的只读数据）。
    /// 收口 AICardTicker / AIAutoExplorer / AICardBrain / ExplorationRewardSystem 的节奏与出牌优先级硬编码。
    /// </summary>
    [Serializable]
    public sealed class AIConfigData
    {
        public string configId;
        public float cardPlayInterval;
        public float exploreInterval;
        public float globalActionMinInterval;
        public int settlerCardPriority;
        public int technologyCardPriority;
        public int unitCardPriority;
        public int buildingCardPriority;
        public int militaryRewardOverflowRings;
    }
}
