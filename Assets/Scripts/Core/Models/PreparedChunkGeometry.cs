using System.Collections.Generic;

//****************************************
// 【动态地图-阶段三】Chunk 后端 staging 产物（PrepareChunkGeometry → CommitChunkGeometry）。
// 每个脏 Chunk 一份构建产物：Terrain/River/Water 几何 + 该 Chunk 内格子的局部顶点范围映射。
// 复用 WholeMap 的几何数据结构（MapRenderer.TerrainGeometry 等）——同程序集 internal 可见。
// 阶段五：DTO 公开（纯数据，无副作用），供分帧提交与测试构造。
//****************************************

public sealed class PreparedChunkGeometry
{
    public readonly List<ChunkStagingGeometry> Chunks = new List<ChunkStagingGeometry>();
}

/// <summary>单个脏 Chunk 的 staging 产物（纯 DTO）。</summary>
public sealed class ChunkStagingGeometry
{
    public ChunkIndex Index;

    internal MapRenderer.TerrainGeometry Terrain;
    internal MapRenderer.RiverGeometry River;
    internal MapRenderer.WaterGeometry Water;

    /// <summary>本 Chunk 内格 → 局部顶点范围（迷雾顶点色回写用，§6-1）。</summary>
    internal readonly Dictionary<int, CellVertexRanges> CellRanges = new Dictionary<int, CellVertexRanges>();
}
