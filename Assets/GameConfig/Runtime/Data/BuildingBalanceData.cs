using System;

namespace GameConfig
{
    /// <summary>
    /// 建筑平衡数值（由 game-config.json 导入的只读数据）。
    /// 数值唯一来源是 Excel；buildingType 使用稳定英文代码（对应 Enums.BulidingType 成员名），
    /// 由 Provider 映射到既有枚举；producedUnitId 为跨表引用单位表稳定 ID，非兵营为空。
    /// </summary>
    [Serializable]
    public sealed class BuildingBalanceData
    {
        public string buildingId;
        public int legacyId;
        public string displayName;
        public string buildingType;
        public float hp;
        public bool blocksMovement;
        public int cardCost;
        public string producedUnitId;
        public float goldIncomePerSecond;
    }
}
