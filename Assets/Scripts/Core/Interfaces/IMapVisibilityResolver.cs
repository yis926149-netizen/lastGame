//****************************************
// 【动态地图-阶段二】统一地图可见性解析器（IMapVisibilityResolver）
// 永久可见性（归属/探索/后勤） || 临时可见性（VisibilityLease）。
// ChunkMapRenderer 迷雾目标与 BuildingBase 血条显隐统一查询本接口，
// 使竞技场/技能等"视觉点亮"不写地块探索位。
//****************************************

public interface IMapVisibilityResolver
{
    /// <summary>某观察方视角下该格是否可见（永久可见性 || 临时 lease）。</summary>
    bool IsVisibleToFaction(HexCellData cell, int factionId);
}
