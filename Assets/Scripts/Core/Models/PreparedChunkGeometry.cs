using System.Collections.Generic;

//****************************************
// Chunk 后端 staging 批次（PrepareChunkGeometry → CommitChunkGeometry）。
// 每个脏 Chunk 一份构建产物：Terrain/River/Water/Grid 几何。
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

    internal TerrainGeometry Terrain;
    internal RiverGeometry River;
    internal WaterGeometry Water;
    internal GridGeometry Grid;
    internal bool AnimationReturnsToStart;
}
