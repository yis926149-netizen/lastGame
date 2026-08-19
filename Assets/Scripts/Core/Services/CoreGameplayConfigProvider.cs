using GameConfig;

//****************************************
//功能说明：核心玩法配置规则（阶段6：Excel 唯一主源）。
//         单位手感、兵营/箭塔、中央宝箱、手牌/天赋与公共建筑生成参数仅由 Excel 读取；
//         Excel 未加载时抛异常，暴露配置缺失。消费点分散且含无 DI 注入的 MonoBehaviour，
//         采用静态 Configure 模式（同 BattleFormulaRule）。
//****************************************
public static class CoreGameplayConfigProvider
{
    private static CoreGameplayConfigDatabaseSO _database;

    /// <summary>由 GameInstaller 在绑定阶段配置 Excel 数值库。</summary>
    public static void Configure(CoreGameplayConfigDatabaseSO database)
    {
        _database = database;
    }

    public static CoreGameplayConfigData Config
    {
        get
        {
            if (_database?.Config == null)
                throw new System.InvalidOperationException(
                    "[CoreGameplay] Excel 核心玩法配置未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 CoreGameplayConfigDatabaseSO。");
            return _database.Config;
        }
    }

    public static float MovementSpeedPerPoint => Config.movementSpeedPerPoint;
    public static float AttackDashSpeed => Config.attackDashSpeed;
    public static float UnitRotationSpeed => Config.unitRotationSpeed;
    public static float AttackArrivalThreshold => Config.attackArrivalThreshold;
    public static float AttackReturnThreshold => Config.attackReturnThreshold;
    public static float AttackAnimationDuration => Config.attackAnimationDuration;
    public static float UnitDeathDestroyDelay => Config.unitDeathDestroyDelay;
    public static float BuildingHealIntervalFallback => Config.buildingHealIntervalFallback;
    public static int UnitPathSearchThrottle => Config.unitPathSearchThrottle;
    public static float BarracksSpawnInterval => Config.barracksSpawnInterval;
    public static int BarracksFallbackUnitLegacyId => Config.barracksFallbackUnitLegacyId;
    public static int ArrowTowerRange => Config.arrowTowerRange;
    public static float ArrowTowerAttackInterval => Config.arrowTowerAttackInterval;
    public static float ArrowTowerDamage => Config.arrowTowerDamage;
    public static float ArrowTowerArcHeight => Config.arrowTowerArcHeight;
    public static float ArrowTowerFlightDuration => Config.arrowTowerFlightDuration;
    public static float CentralChestHp => Config.centralChestHp;
    public static int HandCardLimit => Config.handCardLimit;
    public static int InitialHandCardCount => Config.initialHandCardCount;
    public static int TacticalCardSlotCount => Config.tacticalCardSlotCount;
    public static int TalentOfferCount => Config.talentOfferCount;
    public static int PublicBuildingMaxCount => Config.publicBuildingMaxCount;
    public static int PublicBuildingMinLandNeighbors => Config.publicBuildingMinLandNeighbors;
    public static int PublicBuildingArenaReserveExtraRings => Config.publicBuildingArenaReserveExtraRings;
}
