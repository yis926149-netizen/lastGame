//****************************************
// 【动态地图-阶段二】地图交互闸门（IMapInteractionGate）
// 动画/事务期间锁定受影响格，统一门控移动/探索/部署/卡牌等交互入口（§12.6）。
// 不得用 IsUnexplorable 兼作动画演出锁（它是永久玩法状态）。
// 阶段二 Duration=0：锁只在同步 Commit 期间持有；阶段四动画期间按受影响格锁定。
//****************************************

public enum MapInteractionType
{
    Move,
    Explore,
    Deploy
}

public interface IMapInteractionGate
{
    /// <summary>查询某格当前是否被锁定（禁止指定类型的交互）。</summary>
    bool IsLocked(HexCellData cell, MapInteractionType type);
}
