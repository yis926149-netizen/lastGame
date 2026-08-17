using System;

namespace GameConfig
{
    /// <summary>
    /// 探索奖励配置（单行，由 game-config.json 导入的只读数据）。
    /// 类型权重、金币/单位数量档位与探索费用。
    /// 档位为逗号分隔字符串（如 "5,5,10,15,20,25"），由 Provider 解析为数组。
    /// </summary>
    [Serializable]
    public sealed class ExplorationRewardConfigData
    {
        public string configId;
        public int noneWeight;
        public int goldWeight;
        public int militaryWeight;
        public int tacticalWeight;
        public int buildingWeight;
        public string goldTiers;
        public string unitCountTiers;
        public int costNone;
        public int costGold;
        public int costMilitary;
        public int costTactical;
        public int costBuilding;
    }
}
