using GameConfig;

//****************************************
//功能说明：经济配置提供者（阶段6：Excel 唯一主源）。
//         起始金币、基础被动收入、AI 补贴、收入结算周期与费用兜底仅由 Excel 读取；
//         Excel 未生成/未绑定时抛异常，暴露配置缺失（不再静默回退 Legacy）。
//****************************************
public class EconomyConfigProvider
{
    private readonly EconomyConfigDatabaseSO _database;

    public EconomyConfigProvider(EconomyConfigDatabaseSO database = null)
    {
        _database = database;
    }

    public EconomyConfigData Config
    {
        get
        {
            if (_database?.Config == null)
                throw new System.InvalidOperationException(
                    "[Economy] Excel 经济配置未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 EconomyConfigDatabaseSO。");
            return _database.Config;
        }
    }

    public int StartingGold => Config.startingGold;
    public int BaseIncomePerTick => Config.baseIncomePerTick;
    public int AIIncomeBonusPerTick => Config.aiIncomeBonusPerTick;
    public float IncomeTickInterval => Config.incomeTickInterval;
    public int ExplorationCostFallback => Config.explorationCostFallback;
    public int CardCostFallback => Config.cardCostFallback;
}
