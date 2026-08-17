using System;

namespace GameConfig
{
    /// <summary>
    /// 全局经济配置（单行，由 game-config.json 导入的只读数据）。
    /// 收口 GoldWallet / GoldIncomeService 的起始金币、被动收入、AI 补贴、结算周期与费用兜底。
    /// </summary>
    [Serializable]
    public sealed class EconomyConfigData
    {
        public string configId;
        public int startingGold;
        public int baseIncomePerTick;
        public int aiIncomeBonusPerTick;
        public float incomeTickInterval;
        public int explorationCostFallback;
        public int cardCostFallback;
    }
}
