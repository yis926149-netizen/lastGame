using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UI.PlacementMask;

//****************************************
// 不可放置区域红色遮罩 · 几何算法单元测试。
// 覆盖：连通分组、单格/多格外轮廓、三角化有效性。
// 使用与 HexMapServiceTests 相同的立方坐标 + 世界坐标夹具口径。
//****************************************
public class PlacementMaskGeometryTests
{
    private const float OuterRadius = 3f;
    private static readonly float InnerRadius = OuterRadius * 0.866025404f;

    private Dictionary<Vector3, HexCellData> _cells;

    [SetUp]
    public void SetUp()
    {
        _cells = new Dictionary<Vector3, HexCellData>();
    }

    // 与 HexMapService.GetNeighbor 一致的立方坐标方向偏移。
    private static readonly Vector3[] DirOffsets =
    {
        new Vector3(0, -1, 1),  // NE
        new Vector3(1, -1, 0),  // E
        new Vector3(1, 0, -1),  // SE
        new Vector3(0, 1, -1),  // SW
        new Vector3(-1, 1, 0),  // W
        new Vector3(-1, 0, 1),  // NW
    };

    private HexCellData AddCell(int order, Vector3 hex)
    {
        float wx = hex.x * 2f * InnerRadius + hex.z * InnerRadius;
        float wz = hex.z * 1.5f * OuterRadius;
        var cell = new HexCellData(Enums.HexType.NoRiver, order, hex, new Vector3(wx, 0f, wz), 1f);
        cell.RealCenterWorldCoordinate = new Vector3(wx, 0f, wz);
        _cells[hex] = cell;
        return cell;
    }

    private HexCellData NeighborOf(HexCellData cell, Enums.HexDirection dir)
    {
        if (dir == Enums.HexDirection.None) return null;
        Vector3 nHex = cell.HexCoordinate + DirOffsets[(int)dir];
        return _cells.TryGetValue(nHex, out var c) ? c : null;
    }

    [Test]
    public void GroupIntoRegions_TwoDisconnectedClusters_ReturnsTwoRegions()
    {
        // 簇 A：原点 + 其 NE 邻居（相连）
        var a0 = AddCell(0, new Vector3(0, 0, 0));
        var a1 = AddCell(1, new Vector3(0, -1, 1)); // NE of a0
        // 簇 B：远处孤立格（与 A 不相邻）
        var b0 = AddCell(2, new Vector3(5, -5, 0));

        var list = new List<HexCellData> { a0, a1, b0 };
        var regions = PlacementMaskGeometry.GroupIntoRegions(list, NeighborOf);

        Assert.AreEqual(2, regions.Count, "两个不连通簇应分成两个区域");
        int total = 0;
        foreach (var r in regions) total += r.Cells.Count;
        Assert.AreEqual(3, total, "所有格都应被分入某个区域");
    }

    [Test]
    public void BuildRegionOutlines_SingleCell_ProducesHexagonRing()
    {
        var c = AddCell(0, new Vector3(0, 0, 0));
        var region = new PlacementMaskGeometry.Region();
        region.Cells.Add(c);

        PlacementMaskGeometry.BuildRegionOutlines(region, NeighborOf, OuterRadius);

        Assert.AreEqual(1, region.OutlinesWorld.Count, "单格应产出一个闭合环");
        Assert.AreEqual(6, region.OutlinesWorld[0].Count, "六边形单格外轮廓应有 6 个角点");
    }

    [Test]
    public void BuildRegionOutlines_SingleCell_RingRadiusMatchesOuterRadius()
    {
        var c = AddCell(0, new Vector3(0, 0, 0));
        var region = new PlacementMaskGeometry.Region();
        region.Cells.Add(c);

        PlacementMaskGeometry.BuildRegionOutlines(region, NeighborOf, OuterRadius);
        var ring = region.OutlinesWorld[0];

        Vector3 center = c.RealCenterWorldCoordinate;
        foreach (var p in ring)
        {
            float dist = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(center.x, center.z));
            Assert.AreEqual(OuterRadius, dist, 0.01f, "角点应在外接圆半径上");
        }
    }

    [Test]
    public void Triangulate_Hexagon_ProducesFourTriangles()
    {
        var c = AddCell(0, new Vector3(0, 0, 0));
        var region = new PlacementMaskGeometry.Region();
        region.Cells.Add(c);
        PlacementMaskGeometry.BuildRegionOutlines(region, NeighborOf, OuterRadius);

        var tris = PlacementMaskGeometry.Triangulate(region.OutlinesWorld[0]);

        // n 边形三角化产出 n-2 个三角形 → 六边形 = 4 个 = 12 个索引。
        Assert.AreEqual(12, tris.Count, "六边形应三角化为 4 个三角形");
        foreach (int idx in tris)
            Assert.IsTrue(idx >= 0 && idx < 6, "索引应落在多边形顶点范围内");
    }

    [Test]
    public void SmoothClosedLoop_IncreasesVertexCount()
    {
        // 2x1 相连两格 → 一个更大的外轮廓
        var c0 = AddCell(0, new Vector3(0, 0, 0));
        var c1 = AddCell(1, new Vector3(1, -1, 0)); // E of c0
        var region = new PlacementMaskGeometry.Region();
        region.Cells.Add(c0);
        region.Cells.Add(c1);
        PlacementMaskGeometry.BuildRegionOutlines(region, NeighborOf, OuterRadius);

        Assert.GreaterOrEqual(region.OutlinesWorld.Count, 1);
        var raw = region.OutlinesWorld[0];
        var smoothed = PlacementMaskGeometry.SmoothClosedLoop(raw, 6, 0.05f);

        Assert.Greater(smoothed.Count, raw.Count, "平滑后顶点数应增加");
    }
}
