using System;

namespace GameConfig
{
    /// <summary>
    /// 普通卡池条目（由 game-config.json 导入的只读数据）。
    /// cardId 引用单位表或建筑表的稳定 ID（unit.* / building.*），cardType 决定查哪张表。
    /// </summary>
    [Serializable]
    public sealed class NormalCardPoolEntry
    {
        public string cardId;
        public string cardType;
        public int weight;
        public bool enabled;
        public bool guaranteedFirst;
    }
}
