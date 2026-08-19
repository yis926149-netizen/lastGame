using GameConfig;

//****************************************
//功能说明：战斗公式共享系数规则（阶段6：Excel 唯一主源）。
//         收口 CombatResolver/BuildingBase/UnitMovementController 3 处重复的河流/雕像/高低地系数，
//         以及高地射程 4 处重复与近战警戒范围。系数仅由 Excel（BattleFormulaConfigDatabaseSO，
//         由 GameInstaller 启动时 Configure）读取；Excel 未加载时抛异常，暴露配置缺失。
//****************************************
public static class BattleFormulaRule
{
    private static BattleFormulaConfigDatabaseSO _database;

    /// <summary>由 GameInstaller 在绑定阶段配置 Excel 数值库。</summary>
    public static void Configure(BattleFormulaConfigDatabaseSO database)
    {
        _database = database;
    }

    public static BattleFormulaConfigData Config
    {
        get
        {
            if (_database?.Config == null)
                throw new System.InvalidOperationException(
                    "[BattleFormula] Excel 战斗公式配置未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 BattleFormulaConfigDatabaseSO。");
            return _database.Config;
        }
    }

    /// <summary>河流防御惩罚（负值，加入防御倍率项）。</summary>
    public static float RiverDefensePenalty => Config.riverDefensePenalty;

    /// <summary>攻击雕像加成（每座，可叠加）。</summary>
    public static float AttackStatueBonus => Config.attackStatueBonus;

    /// <summary>高低地攻击加成绝对值（攻高 +、攻低 -）。</summary>
    public static float HighGroundAttackBonus => Config.highGroundAttackBonus;

    /// <summary>高地射程加成（格）。</summary>
    public static int HighGroundRangeBonus => Config.highGroundRangeBonus;

    /// <summary>近战警戒范围（格）。</summary>
    public static int MeleeAlertRange => Config.meleeAlertRange;
}
