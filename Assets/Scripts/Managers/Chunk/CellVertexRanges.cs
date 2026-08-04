using System.Collections.Generic;

//****************************************
// 【动态地图-阶段三】单格在 Chunk 局部 mesh 中的顶点范围元数据（§6-1 修订）。
// Chunk 化后各 Chunk 顶点偏移独立，不再写回 HexCellData 的全局索引字段；
// 迷雾顶点色回写/标签定位一律经本映射查询（Chunk 局部索引）。
//****************************************

public sealed class CellVertexRanges
{
    /// <summary>实心区域 44 顶点起始索引（Chunk 局部）。</summary>
    public int SolidStart = -1;

    /// <summary>实心区域顶点数（恒 44，含河道）。</summary>
    public int SolidCount;

    /// <summary>过渡区域（矩形+三角）范围列表（Chunk 局部）。</summary>
    public List<(int start, int count)> TransitionRanges = new List<(int start, int count)>();

    /// <summary>水面顶点范围（Chunk 局部；非水格为空）。</summary>
    public List<(int start, int count)> WaterRanges = new List<(int start, int count)>();

    /// <summary>河流顶点范围（Chunk 局部；无河格为空）。</summary>
    public List<(int start, int count)> RiverRanges = new List<(int start, int count)>();

    public bool HasTerrain => SolidStart >= 0;
}
