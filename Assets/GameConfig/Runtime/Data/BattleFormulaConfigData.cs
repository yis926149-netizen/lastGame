using System;

namespace GameConfig
{
    /// <summary>
    /// 战斗公式共享系数（单行，由 game-config.json 导入的只读数据）。
    /// 收口 CombatResolver / BuildingBase / UnitMovementController 3 处重复的战斗公式系数，
    /// 以及高地射程 4 处重复与近战警戒范围。
    /// </summary>
    [Serializable]
    public sealed class BattleFormulaConfigData
    {
        public string configId;
        public float riverDefensePenalty;
        public float attackStatueBonus;
        public float highGroundAttackBonus;
        public int highGroundRangeBonus;
        public int meleeAlertRange;
    }
}
