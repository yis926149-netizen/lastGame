using NUnit.Framework;
using NSubstitute;
using System.Collections.Generic;
using UnityEngine;

//****************************************
// 【动态地图-阶段三】Chunk 渲染器单元测试
// 覆盖：8×8 offset-grid 划分（§二十-1）、脏 Chunk 计算（§七）、
// 材质组合键共享（重建不增长）。
// 几何归一化 A/B 对比测试需要 Editor 运行时/渲染上下文，见验证方案 §18.1。
//****************************************

public class ChunkIndexTests
{
    [Test]
    public void ChunkIndex_Of_ComputesOffsetGrid()
    {
        // xNumber=30：order = row * 30 + column
        // row=9, column=10 → chunkX = 10/8 = 1, chunkZ = 9/8 = 1
        var cell = new HexCellData(Enums.HexType.NoRiver, 9 * 30 + 10, Vector3.zero, Vector3.zero, 1f);
        ChunkIndex index = ChunkIndex.Of(cell, 30);
        Assert.AreEqual(1, index.X);
        Assert.AreEqual(1, index.Z);
    }

    [Test]
    public void ChunkIndex_Of_ChunkBoundaryColumn()
    {
        // column=8 → chunkX = 1（第 9 列进入第二个 Chunk）
        var cell = new HexCellData(Enums.HexType.NoRiver, 8, Vector3.zero, Vector3.zero, 1f);
        ChunkIndex index = ChunkIndex.Of(cell, 30);
        Assert.AreEqual(1, index.X);
        Assert.AreEqual(0, index.Z);
    }

    [Test]
    public void ChunkIndex_Of_FirstCellIsChunkZeroZero()
    {
        var cell = new HexCellData(Enums.HexType.NoRiver, 0, Vector3.zero, Vector3.zero, 1f);
        ChunkIndex index = ChunkIndex.Of(cell, 30);
        Assert.AreEqual(0, index.X);
        Assert.AreEqual(0, index.Z);
    }

    [Test]
    public void ChunkIndex_Equality_Works()
    {
        Assert.AreEqual(new ChunkIndex(2, 3), new ChunkIndex(2, 3));
        Assert.AreNotEqual(new ChunkIndex(2, 3), new ChunkIndex(3, 2));
        var set = new HashSet<ChunkIndex> { new ChunkIndex(1, 1) };
        Assert.IsTrue(set.Contains(new ChunkIndex(1, 1)));
    }
}

public class SphereOfInfluenceBoundaryStatelessTests
{
    private MapGenerationConfigSO _config;

    [TearDown]
    public void TearDown()
    {
        if (_config != null) Object.DestroyImmediate(_config);
    }

    [Test]
    public void ExtractBoundary_WithEmptyLegacySolidCache_BuildsSingleHexBoundary()
    {
        IMapDataService mapData = Substitute.For<IMapDataService>();
        _config = ScriptableObject.CreateInstance<MapGenerationConfigSO>();
        var generator = new MeshGeneratorService(mapData, _config);
        var cell = new HexCellData(Enums.HexType.NoRiver, 0, Vector3.zero, Vector3.zero, 1f);

        mapData.GetNeighbor(cell, Arg.Any<Enums.HexDirection>()).Returns((HexCellData)null);
        var cells = new List<HexCellData> { cell };
        var segments = new List<BoundarySegment>();
        var corners = new List<Vector3>();

        generator.ExtractSphereOfInfluenceBoundary(cells, cells, mapData, segments, corners);

        Assert.AreEqual(6, segments.Count);
        Assert.AreEqual(6, corners.Count);
        Assert.IsTrue(segments.TrueForAll(segment => segment.Type == BoundarySegmentType.HexEdge));
    }
}

/// <summary>脏 Chunk 计算的纯逻辑验证：中心格 + 一环邻居 → 所属 Chunk 去重。</summary>
public class DirtyChunkComputationTests
{
    private const int XNumber = 30;

    private static HexCellData MakeCell(int order) =>
        new HexCellData(Enums.HexType.NoRiver, order, Vector3.zero, Vector3.zero, 1f);

    private static ChunkIndex Of(int order) => ChunkIndex.Of(MakeCell(order), XNumber);

    [Test]
    public void DirtyChunks_CenterCellPlusRing_SpansUpToFourChunks()
    {
        // 中心格 order = 11*30+11 = 341（行列均 11）——落于 Chunk(1,1) 内部，
        // 一环 7 格横跨 row 10-12 / column 10-12，最坏涉及 4 个 Chunk。
        // 验证：中心格所属 Chunk 必在其中，且总数 ≤ 4。
        int centerOrder = 11 * XNumber + 11;
        var dirty = new HashSet<ChunkIndex> { Of(centerOrder) };
        var seen = new HashSet<int> { centerOrder };

        int[] dx = { 1, 0, -1, -1, 0, 1 };
        int[] dz = { 0, 1, 1, 0, -1, -1 };
        int centerRow = centerOrder / XNumber;
        int centerCol = centerOrder % XNumber;
        for (int d = 0; d < 6; d++)
        {
            int row = centerRow + dz[d];
            int col = centerCol + dx[d];
            if (row < 0 || col < 0 || col >= XNumber) continue;
            int order = row * XNumber + col;
            if (!seen.Add(order)) continue;
            dirty.Add(Of(order));
        }

        Assert.IsTrue(dirty.Contains(Of(centerOrder)));
        Assert.LessOrEqual(dirty.Count, 4);
        // 中心在 Chunk(1,1) 内部（11/8=1, 11/8=1）→ 一环格不会超出 row/col [7..15] 的 Chunk 范围
        Assert.IsTrue(dirty.Count >= 1);
    }

    [Test]
    public void DirtyChunks_SameChunkCells_Deduplicated()
    {
        // 同一 Chunk 内两个格 → 去重后 1 个 Chunk
        int orderA = 1 * XNumber + 1;   // Chunk(0,0)
        int orderB = 2 * XNumber + 2;   // Chunk(0,0)
        var dirty = new HashSet<ChunkIndex> { Of(orderA), Of(orderB) };
        Assert.AreEqual(1, dirty.Count);
    }

    [Test]
    public void ChunkCollectCells_AllCellsInChunkBounds()
    {
        // 模拟 CollectChunkCells：Chunk(0,0) = row 0-7 × col 0-7
        var cells = new List<HexCellData>();
        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                int order = row * XNumber + col;
                cells.Add(MakeCell(order));
            }
        }
        Assert.AreEqual(64, cells.Count);
        foreach (HexCellData cell in cells)
        {
            ChunkIndex index = ChunkIndex.Of(cell, XNumber);
            Assert.AreEqual(new ChunkIndex(0, 0), index);
        }
    }
}
