using GameConfig;

//****************************************
//功能说明：战斗公式共享系数规则（纯函数）。
//         收口 CombatResolver/BuildingBase/UnitMovementController 3 处重复的河流/雕像/高低地系数，
//         以及高地射程 4 处重复与近战警戒范围。系数优先由 Excel（BattleFormulaConfigDatabaseSO，
//         由 GameInstaller 启动时 Configure）读取，缺失回退 Legacy 默认值（双轨迁移期）。
//****************************************
public static class BattleFormulaRule
{
    private static BattleFormulaConfigDatabaseSO _database;

    /// <summary>由 GameInstaller 在绑定阶段配置 Excel 数值库（可选，未生成时为 null 回退 Legacy）。</summary>
    public static void Configure(BattleFormulaConfigDatabaseSO database)
    {
        _database = database;
    }

    /// <summary>河流防御惩罚（负值，加入防御倍率项）。</summary>
    public static float RiverDefensePenalty => _database?.Config?.riverDefensePenalty ?? -0.5f;

    /// <summary>攻击雕像加成（每座，可叠加）。</summary>
    public static float AttackStatueBonus => _database?.Config?.attackStatueBonus ?? 0.7f;

    /// <summary>高低地攻击加成绝对值（攻高 +、攻低 -）。</summary>
    public static float HighGroundAttackBonus => _database?.Config?.highGroundAttackBonus ?? 0.5f;

    /// <summary>高地射程加成（格）。</summary>
    public static int HighGroundRangeBonus => _database?.Config?.highGroundRangeBonus ?? 1;

    /// <summary>近战警戒范围（格）。</summary>
    public static int MeleeAlertRange => _database?.Config?.meleeAlertRange ?? 3;
}
