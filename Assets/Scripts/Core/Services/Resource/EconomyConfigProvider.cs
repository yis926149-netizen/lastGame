using GameConfig;

//****************************************
//功能说明：经济配置提供者。
//         起始金币、基础被动收入、AI 补贴、收入结算周期与费用兜底优先由 Excel 读取，
//         Excel 未生成时回退 Legacy 默认值（双轨迁移期，阶段6 删除回退）。
//****************************************
public class EconomyConfigProvider
{
    private readonly EconomyConfigDatabaseSO _database;   // Excel 数值（可选）

    public EconomyConfigProvider(EconomyConfigDatabaseSO database = null)
    {
        _database = database;
    }

    public EconomyConfigData Config => _database?.Config;

    public int StartingGold => Config?.startingGold ?? 100;

    public int BaseIncomePerTick => Config?.baseIncomePerTick ?? 2;

    public int AIIncomeBonusPerTick => Config?.aiIncomeBonusPerTick ?? 6;

    public float IncomeTickInterval => Config?.incomeTickInterval ?? 1f;

    public int ExplorationCostFallback => Config?.explorationCostFallback ?? 50;

    public int CardCostFallback => Config?.cardCostFallback ?? 10;
}
