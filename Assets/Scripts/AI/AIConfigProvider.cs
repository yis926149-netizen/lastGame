using GameConfig;

//****************************************
//功能说明：AI 配置提供者（阶段6：Excel 唯一主源）。
//         AI 出牌/探索节奏、全局操作间隔、出牌优先级与军事奖励溢出搜索环数
//         仅由 Excel 读取；Excel 未生成/未绑定时抛异常，暴露配置缺失。
//****************************************
public class AIConfigProvider
{
    private readonly AIConfigDatabaseSO _database;

    public AIConfigProvider(AIConfigDatabaseSO database = null)
    {
        _database = database;
    }

    public AIConfigData Config
    {
        get
        {
            if (_database?.Config == null)
                throw new System.InvalidOperationException(
                    "[AI] Excel AI 配置未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 AIConfigDatabaseSO。");
            return _database.Config;
        }
    }

    public float CardPlayInterval => Config.cardPlayInterval;
    public float ExploreInterval => Config.exploreInterval;
    public float GlobalActionMinInterval => Config.globalActionMinInterval;
    public int SettlerCardPriority => Config.settlerCardPriority;
    public int TechnologyCardPriority => Config.technologyCardPriority;
    public int UnitCardPriority => Config.unitCardPriority;
    public int BuildingCardPriority => Config.buildingCardPriority;
    public int MilitaryRewardOverflowRings => Config.militaryRewardOverflowRings;
}
