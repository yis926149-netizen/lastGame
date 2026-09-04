using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

//****************************************
// 战术卡影响范围 · n 环枚举工具单元测试（HexRange）。
//
// 用真实 HexMapService（无 Zenject / 无 NSubstitute）构建六边形范围图，
// 保证反射测试宿主（Temp/runner）可直接运行，不依赖 DI 容器或 Unity 原生对象。
//****************************************
public class HexRangeTests
{
    private HexMapService _map;

    [SetUp]
    public void SetUp()
    {
        _map = BuildMap(10);
    }

    /// <summary>
    /// 构建以原点为中心、立方距离 ≤ radius 的六边形图（格数 = 3r²+3r+1）。
    /// mapGameObject 传 null：HexRange 只走 GetNeighbor/GetCell，不触碰地图对象，
    /// 传 null 避免反射宿主里 new GameObject() 触发 Unity 原生绑定。
    /// </summary>
    private static HexMapService BuildMap(int radius)
    {
        var hexToCell = new Dictionary<Vector3, HexCellData>();
        var orderToCell = new Dictionary<int, HexCellData>();
        var centerWorld = new List<Vector3> { Vector3.zero };
        var worldToHex = new Dictionary<Vector3, Vector3>();

        int order = 0;
        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                int z = -x - y;
                if (z < -radius || z > radius) continue;
                Vector3 hex = new Vector3(x, y, z);
                var cell = new HexCellData(Enums.HexType.NoRiver, order, hex, Vector3.zero, 0f);
                hexToCell[hex] = cell;
                orderToCell[order] = cell;
                order++;
            }
        }

        var map = new HexMapService();
        map.Initialize(hexToCell, orderToCell, centerWorld, worldToHex, null, new Vector3[0]);
        return map;
    }

    private HexCellData Center() => _map.GetCell(Vector3.zero);

    private List<HexCellData> Collect(int radius)
    {
        var list = new List<HexCellData>();
        HexRange.CollectInRange(_map, Center(), radius, list);
        return list;
    }

    // ---------------- 距离 ----------------

    [Test]
    public void Distance_MatchesKnownFormula()
    {
        // 抽样对拍：与仓库内 (|dx|+|dy|+|dz|)*0.5f 副本公式一致。
        Vector3[] samples =
        {
            new Vector3(0, 0, 0),
            new Vector3(1, -1, 0),
            new Vector3(0, -1, 1),
            new Vector3(2, -1, -1),
            new Vector3(-3, 1, 2),
            new Vector3(5, -2, -3),
        };

        foreach (Vector3 a in samples)
            foreach (Vector3 b in samples)
            {
                float expect = (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z)) * 0.5f;
                Assert.AreEqual(Mathf.RoundToInt(expect), HexRange.Distance(a, b),
                    $"Distance({a}, {b}) 应与既有公式一致");
            }
    }

    [Test]
    public void Distance_SelfIsZero_And_NeighborIsOne()
    {
        Vector3 a = new Vector3(2, -1, -1);
        Assert.AreEqual(0, HexRange.Distance(a, a), "自身距离应为 0");

        Vector3 b = new Vector3(2, -2, 0); // a 的 NW 邻居
        Assert.AreEqual(1, HexRange.Distance(a, b), "相邻格距离应为 1");
    }

    // ---------------- 环数（地图中心，不触边） ----------------

    [Test]
    public void CollectInRange_CenterCounts()
    {
        Assert.AreEqual(1, Collect(0).Count, "n=0 → 1 格");
        Assert.AreEqual(7, Collect(1).Count, "n=1 → 7 格");
        Assert.AreEqual(19, Collect(2).Count, "n=2 → 19 格");
        Assert.AreEqual(37, Collect(3).Count, "n=3 → 37 格");
    }

    [Test]
    public void CollectInRange_EveryCellWithinRadius()
    {
        List<HexCellData> list = Collect(3);
        HexCellData center = Center();
        foreach (HexCellData c in list)
        {
            Assert.IsNotNull(c);
            Assert.LessOrEqual(HexRange.Distance(center.HexCoordinate, c.HexCoordinate), 3,
                "结果内每格到中心距离必须 ≤ n");
        }
    }

    [Test]
    public void CollectInRange_CoversExactlyAllCellsWithinRadius()
    {
        List<HexCellData> list = Collect(3);
        var set = new HashSet<HexCellData>(list);
        HexCellData center = Center();

        // 图半径 10，中心 3 环完全在内部：必须恰好覆盖所有距离 ≤ 3 的格，且不含更远的格。
        foreach (HexCellData cell in _map.GetAllCells())
        {
            int d = HexRange.Distance(center.HexCoordinate, cell.HexCoordinate);
            if (d <= 3)
                Assert.IsTrue(set.Contains(cell), $"距离 {d} ≤ 3 的格必须被覆盖：{cell.HexCoordinate}");
            else
                Assert.IsFalse(set.Contains(cell), $"距离 {d} > 3 的格不应被覆盖：{cell.HexCoordinate}");
        }
    }

    [Test]
    public void CollectInRange_NoDuplicates()
    {
        List<HexCellData> list = Collect(3);
        var set = new HashSet<HexCellData>(list);
        Assert.AreEqual(list.Count, set.Count, "结果不应含重复格");
    }

    // ---------------- 贴边缘被裁 ----------------

    [Test]
    public void CollectInRange_AtEdge_IsClippedWithoutNull()
    {
        var map = BuildMap(2); // 距离 ≤ 2，共 19 格；边界格满 2 环会越界
        HexCellData edge = map.GetCell(new Vector3(2, 0, -2));
        Assert.IsNotNull(edge, "测试格必须存在于图中");

        var list = new List<HexCellData>();
        HexRange.CollectInRange(map, edge, 2, list);

        Assert.Greater(list.Count, 0);
        Assert.Less(list.Count, 19, "贴边时格数应小于满环 19（被图边界裁掉）");
        foreach (HexCellData c in list)
        {
            Assert.IsNotNull(c, "结果不应含 null（GetNeighbor 对图外返回 null，须被跳过）");
            Assert.LessOrEqual(HexRange.Distance(edge.HexCoordinate, c.HexCoordinate), 2);
        }
    }

    // ---------------- 空输入 ----------------

    [Test]
    public void CollectInRange_NullCenter_ReturnsEmpty()
    {
        var list = new List<HexCellData> { _map.GetCell(Vector3.zero) };
        HexRange.CollectInRange(_map, null, 3, list);
        Assert.AreEqual(0, list.Count, "中心为 null 应清空结果");
    }
}
