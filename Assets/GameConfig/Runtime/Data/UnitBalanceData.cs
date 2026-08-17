using System;

namespace GameConfig
{
    /// <summary>
    /// 单位平衡数值（由 game-config.json 导入的只读数据）。
    /// 数值的唯一来源是 Excel；本类只承载导入结果，不做任何运行时推断。
    /// strategyType 使用稳定英文代码（Melee / Ranged / Settler），由 Provider 映射到既有枚举。
    /// </summary>
    [Serializable]
    public sealed class UnitBalanceData
    {
        public string unitId;
        public int legacyId;
        public string displayName;
        public bool enabled;
        public string strategyType;
        public float hp;
        public float attack;
        public float defense;
        public int attackRange;
        public float movementPoints;
        public int viewPoints;
        public float attackIntervalSeconds;
        public int cardCost;
    }
}
