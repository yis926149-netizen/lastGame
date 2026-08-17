using System;

namespace GameConfig
{
    /// <summary>
    /// 地图地貌数值（由 game-config.json 导入的只读数据）。
    /// 模型/浮标等资源引用保留在手工资源 SO（MapLandFormSO），数值进本表。
    /// effectType 使用英文代码（None / DefenseBonus / PeriodicHeal / GoldIncomeBoost）。
    /// 山脉（mountainForm）独立配置，不入本散落地貌表。
    /// </summary>
    [Serializable]
    public sealed class MapLandFormBalanceData
    {
        public string landFormId;
        public string landFormName;
        public string description;
        public bool enabled;
        public string effectType;
        public float defenseBonus;
        public float healRatio;
        public float healInterval;
        public float goldIncomePerSecond;
        public bool blockBuildingSpawn;
        public int spawnWeight;
        public bool clusterSpawn;
        public int clusterCount;
        public int clusterTargetSize;
        public float clusterFillProbability;
        public int clusterMinSpacing;
        public int clusterMaxRadius;
    }
}
