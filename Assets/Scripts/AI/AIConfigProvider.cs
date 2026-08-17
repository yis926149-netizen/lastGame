using GameConfig;

//****************************************
//功能说明：AI 配置提供者。
//         AI 出牌/探索节奏、全局操作间隔、出牌优先级与军事奖励溢出搜索环数
//         优先由 Excel 读取，Excel 未生成时回退 Legacy 默认值（双轨迁移期，阶段6 删除回退）。
//****************************************
public class AIConfigProvider
{
    private readonly AIConfigDatabaseSO _database;   // Excel 数值（可选）

    public AIConfigProvider(AIConfigDatabaseSO database = null)
    {
        _database = database;
    }

    public AIConfigData Config => _database?.Config;

    public float CardPlayInterval => Config?.cardPlayInterval ?? 1.5f;

    public float ExploreInterval => Config?.exploreInterval ?? 1.5f;

    public float GlobalActionMinInterval => Config?.globalActionMinInterval ?? 1f;

    public int SettlerCardPriority => Config?.settlerCardPriority ?? 100;

    public int TechnologyCardPriority => Config?.technologyCardPriority ?? 90;

    public int UnitCardPriority => Config?.unitCardPriority ?? 70;

    public int BuildingCardPriority => Config?.buildingCardPriority ?? 60;

    public int MilitaryRewardOverflowRings => Config?.militaryRewardOverflowRings ?? 5;
}
