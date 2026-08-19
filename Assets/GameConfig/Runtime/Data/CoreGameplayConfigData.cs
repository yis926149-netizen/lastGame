using System;

namespace GameConfig
{
    /// <summary>
    /// 核心玩法配置（单行，由 game-config.json 导入的只读数据）。
    /// 收口单位手感、兵营/箭塔、中央宝箱、手牌/天赋与公共建筑生成硬编码。
    /// 注意：MapGenerationConfig.asset 及其字段保持为地图生成的既有主源，不在此表内。
    /// </summary>
    [Serializable]
    public sealed class CoreGameplayConfigData
    {
        public string configId;
        public float movementSpeedPerPoint;
        public float attackDashSpeed;
        public float unitRotationSpeed;
        public float attackArrivalThreshold;
        public float attackReturnThreshold;
        public float attackAnimationDuration;
        public float unitDeathDestroyDelay;
        public float buildingHealIntervalFallback;
        public int unitPathSearchThrottle;
        public float barracksSpawnInterval;
        public int barracksFallbackUnitLegacyId;
        public int arrowTowerRange;
        public float arrowTowerAttackInterval;
        public float arrowTowerDamage;
        public float arrowTowerArcHeight;
        public float arrowTowerFlightDuration;
        public float centralChestHp;
        public int handCardLimit;
        public int initialHandCardCount;
        public int tacticalCardSlotCount;
        public int talentOfferCount;
        public int publicBuildingMaxCount;
        public int publicBuildingMinLandNeighbors;
        public int publicBuildingArenaReserveExtraRings;
    }
}
