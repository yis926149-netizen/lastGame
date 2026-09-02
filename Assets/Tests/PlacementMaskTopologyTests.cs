using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UI.PlacementMask;

//****************************************
// 不可放置区域遮罩 · 拓扑层单元测试（路线三）。
//
// 关键：夹具的 RealCenterWorldCoordinate 刻意注入 ±0.2 的逐格随机扰动，复现
// HexMetrics.Perturb 的真实行为（HexMetrics.cs:42-43）。旧测试用未扰动中心，
// 所以「角点焊接」在夹具里成立、在真机里失败——马赛克 bug 正好从测试缝里溜过去。
// 拓扑层用立方坐标而非浮点坐标做角点身份，因此这些用例在有扰动下依然必须通过。
//****************************************
public class PlacementMaskTopologyTests
{
    private const float OuterRadius = 3f;
    private const float ElevationStep = 3f;
    private static readonly float InnerRadius = OuterRadius * 0.866025404f;

    private Dictionary<Vector3, HexCellData> _cells;

    [SetUp]
    public void SetUp()
    {
        _cells = new Dictionary<Vector3, HexCellData>();
    }

    private static readonly Vector3[] DirOffsets =
    {
        new Vector3(0, -1, 1),  // NE
        new Vector3(1, -1, 0),  // E
        new Vector3(1, 0, -1),  // SE
        new Vector3(0, 1, -1),  // SW
        new Vector3(-1, 1, 0),  // W
        new Vector3(-1, 0, 1),  // NW
    };

    /// <summary>
    /// 建格。CenterWorldCoordinate 用精确格心（拓扑层只读它），
    /// RealCenterWorldCoordinate 注入确定性伪随机扰动 —— 若实现回退到读它，用例立刻失败。
    /// </summary>
    private HexCellData AddCell(int order, Vector3 hex, float height = 1f)
    {
        float wx = hex.x * 2f * InnerRadius + hex.z * InnerRadius;
        float wz = hex.z * 1.5f * OuterRadius;
        var cell = new HexCellData(Enums.HexType.NoRiver, order, hex, new Vector3(wx, 0f, wz), height);

        int h = ((int)hex.x * 73856093) ^ ((int)hex.z * 19349663);
        float jx = ((h & 0xFF) / 255f * 2f - 1f) * 0.2f;
        float jz = (((h >> 8) & 0xFF) / 255f * 2f - 1f) * 0.2f;
        cell.RealCenterWorldCoordinate = new Vector3(wx + jx, height * ElevationStep, wz + jz);

        _cells[hex] = cell;
        return cell;
    }

    private List<HexCellData> Cells(params Vector3[] coords)
    {
        var list = new List<HexCellData>();
        for (int i = 0; i < coords.Length; i++)
            list.Add(_cells[coords[i]]);
        return list;
    }

    // ---------------- 角点共享（马赛克 bug 的直接回归测试） ----------------

    [Test]
    public void Build_TwoAdjacentCells_ShareExactlyTwoCorners()
    {
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(1, -1, 0); // E of a
        AddCell(0, a);
        AddCell(1, b);

        var topo = PlacementMaskTopology.Build(Cells(a, b), OuterRadius, ElevationStep);

        // 2 格各 6 角点，公共边贡献 2 个共享角点 → 去重后 10 个。
        // 这正是旧实现失败之处：浮点焊接下会得到 12 个（各自为政），
        // 相邻格之间既重叠又漏缝 → 马赛克网格纹。
        Assert.AreEqual(10, topo.CornerWorld.Count,
            "相邻两格必须共享 2 个角点（去重后 12-2=10）");
        Assert.AreEqual(2, topo.CellCount);
        Assert.AreEqual(12, topo.CellCorners.Count, "每格 6 个角点下标");
    }

    [Test]
    public void Build_ThreeMutuallyAdjacentCells_ShareOneCommonCorner()
    {
        // 互为邻居的 3 格围出一个公共角点（角点身份三元组的几何依据）。
        var a = new Vector3(0, 0, 0);
        var b = a + DirOffsets[0]; // NE
        var c = a + DirOffsets[1]; // E
        AddCell(0, a); AddCell(1, b); AddCell(2, c);

        var topo = PlacementMaskTopology.Build(Cells(a, b, c), OuterRadius, ElevationStep);

        // 18 个原始角点：3 对两两共享(各减1) + 1 个三格公共(减2) → 18-3-2 = 13
        Assert.AreEqual(13, topo.CornerWorld.Count,
            "三格互邻应共享 1 个公共角点 + 3 组成对共享");

        // 三格公共角点必须在三份角点下标里都出现。
        var counts = new Dictionary<int, int>();
        foreach (int idx in topo.CellCorners)
        {
            counts.TryGetValue(idx, out int n);
            counts[idx] = n + 1;
        }
        int tripleShared = 0;
        foreach (var kv in counts) if (kv.Value == 3) tripleShared++;
        Assert.AreEqual(1, tripleShared, "应恰有 1 个被 3 格共享的角点");
    }

    [Test]
    public void Build_CornerPositionsIgnorePerturbedCenter()
    {
        // 角点必须由未扰动格心推出：共享角点的世界坐标误差应为 0（而非 ±0.2 量级）。
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(1, -1, 0);
        AddCell(0, a); AddCell(1, b);

        var topo = PlacementMaskTopology.Build(Cells(a, b), OuterRadius, ElevationStep);

        // 每个角点到其所属格未扰动中心的距离恒等于 OuterRadius。
        for (int i = 0; i < topo.CellCount; i++)
        {
            Vector3 center = topo.CellCenterWorld[i];
            for (int k = 0; k < 6; k++)
            {
                Vector3 p = topo.CornerWorld[topo.CellCorners[i * 6 + k]];
                float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(center.x, center.z));
                Assert.AreEqual(OuterRadius, d, 1e-3f,
                    "角点必须精确落在未扰动外接圆上（读到扰动中心会明显偏离）");
            }
        }
    }

    // ---------------- 边界闭环 ----------------

    [Test]
    public void Build_SingleCell_ProducesOneHexLoop()
    {
        var a = new Vector3(0, 0, 0);
        AddCell(0, a);

        var topo = PlacementMaskTopology.Build(Cells(a), OuterRadius, ElevationStep);

        Assert.AreEqual(1, topo.Loops.Count, "单格应产出 1 条闭环");
        Assert.AreEqual(6, topo.Loops[0].Count, "单格边界为 6 个角点");
    }

    [Test]
    public void Build_TwoAdjacentCells_ProduceSingleTenPointLoop()
    {
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(1, -1, 0);
        AddCell(0, a); AddCell(1, b);

        var topo = PlacementMaskTopology.Build(Cells(a, b), OuterRadius, ElevationStep);

        // 内部公共边被剔除 → 一条 10 点外环（而非两个 6 点小环）。
        Assert.AreEqual(1, topo.Loops.Count, "相连两格应合并为一条外环");
        Assert.AreEqual(10, topo.Loops[0].Count, "12 边去掉 2 条内部边 → 10 点环");
    }

    [Test]
    public void Build_DisconnectedCells_ProduceSeparateLoops()
    {
        var a = new Vector3(0, 0, 0);
        var far = new Vector3(6, -6, 0);
        AddCell(0, a); AddCell(1, far);

        var topo = PlacementMaskTopology.Build(Cells(a, far), OuterRadius, ElevationStep);

        Assert.AreEqual(2, topo.Loops.Count, "两个不连通格各出一条环");
    }

    [Test]
    public void Build_RingAroundHole_ProducesOuterAndHoleLoops()
    {
        // 中心格的 6 个邻居全在集合内、中心格不在 → 一个洞。
        var center = new Vector3(0, 0, 0);
        AddCell(99, center); // 建但不加入集合
        var ring = new List<Vector3>();
        for (int d = 0; d < 6; d++)
        {
            Vector3 h = center + DirOffsets[d];
            AddCell(d, h);
            ring.Add(h);
        }

        var topo = PlacementMaskTopology.Build(Cells(ring.ToArray()), OuterRadius, ElevationStep);

        // 外环 + 洞环 = 2 条。洞环必须保留（可放置孤岛也要被圈出来）。
        Assert.AreEqual(2, topo.Loops.Count, "环形区域应产出外环 + 洞环");
        // 洞环 = 中心格的 6 个角点。
        int minLen = int.MaxValue;
        foreach (var l in topo.Loops) minLen = Mathf.Min(minLen, l.Count);
        Assert.AreEqual(6, minLen, "洞环应为中心格的 6 个角点");
    }

    [Test]
    public void Build_EveryBoundaryCornerHasDegreeTwo()
    {
        // 度数不变量：边界角点度数恒为 2（不存在 T 型交叉）。
        // 该不变量成立，追踪才无需左手法则——前两版正是栽在这里。
        // 用一块凹形区域（3x3 少一角）确保覆盖凹角。
        var coords = new List<Vector3>();
        for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
            {
                if (x == 2 && z == 2) continue; // 挖掉一角造凹形
                var h = new Vector3(x, -x - z, z);
                AddCell(x * 3 + z, h);
                coords.Add(h);
            }

        var topo = PlacementMaskTopology.Build(Cells(coords.ToArray()), OuterRadius, ElevationStep);

        // 所有环的顶点各出现恰好 1 次（度数 2 ⇒ 每点属于唯一环且只经过一次）。
        var seen = new HashSet<int>();
        int total = 0;
        foreach (var loop in topo.Loops)
            foreach (int idx in loop)
            {
                Assert.IsTrue(seen.Add(idx), "同一角点不应被两条环重复经过（暗示出现 T 型交叉）");
                total++;
            }
        Assert.Greater(total, 0, "凹形区域必须产出边界环");
    }

    // ---------------- 填充三角化（扫描线 + 偶奇） ----------------
    //
    // 填充不再按格扇形，而是对「描边同一批处理后闭环」做扫描线偶奇填充。
    // 下面的用例直接喂 2D 闭环给 PlacementMaskFill，与描边的输入口径一致。
    // 判据一律是「三角面积和 == 期望面积」：有缝会偏小、有叠会偏大，两头都能抓。

    private static List<Vector2> Square(float x0, float y0, float size)
    {
        return new List<Vector2>
        {
            new Vector2(x0, y0), new Vector2(x0 + size, y0),
            new Vector2(x0 + size, y0 + size), new Vector2(x0, y0 + size),
        };
    }

    /// <summary>三角形面积之和 = 多边形面积 ⇒ 既无缝隙也无重叠。</summary>
    private static float TriangleAreaSum(List<Vector2> verts, List<int> tris)
    {
        float s = 0f;
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            Vector2 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
            s += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) * 0.5f;
        }
        return s;
    }

    [Test]
    public void Fill_SimpleLoop_AreaMatchesPolygon()
    {
        var loops = new List<List<Vector2>> { Square(0f, 0f, 10f) };
        var verts = new List<Vector2>();
        var tris = new List<int>();

        new PlacementMaskFill().Triangulate(loops, verts, tris);

        Assert.AreEqual(100f, TriangleAreaSum(verts, tris), 1e-2f,
            "三角面积和必须等于多边形面积（有缝会偏小、有叠会偏大）");
        foreach (int idx in tris)
            Assert.IsTrue(idx >= 0 && idx < verts.Count, "索引须落在顶点表内");
    }

    /// <summary>洞必须真的被挖空 —— 否则被包围的异色孤岛会被填充盖住。</summary>
    [Test]
    public void Fill_LoopWithHole_ExcludesHoleArea()
    {
        var loops = new List<List<Vector2>>
        {
            Square(0f, 0f, 30f),    // 外环
            Square(10f, 10f, 10f),  // 洞
        };
        var verts = new List<Vector2>();
        var tris = new List<int>();

        new PlacementMaskFill().Triangulate(loops, verts, tris);

        Assert.AreEqual(900f - 100f, TriangleAreaSum(verts, tris), 1e-1f,
            "洞的面积必须从填充中扣除");
    }

    /// <summary>洞里再套一块同色区域（深度 2）：该块是外环，必须被填回来。</summary>
    [Test]
    public void Fill_IslandInsideHole_IsFilledAgain()
    {
        var loops = new List<List<Vector2>>
        {
            Square(0f, 0f, 40f),    // 外环         深度 0
            Square(10f, 10f, 20f),  // 洞           深度 1
            Square(15f, 15f, 10f),  // 洞中孤岛      深度 2 → 又是外环
        };
        var verts = new List<Vector2>();
        var tris = new List<int>();

        new PlacementMaskFill().Triangulate(loops, verts, tris);

        Assert.AreEqual(1600f - 400f + 100f, TriangleAreaSum(verts, tris), 1e-1f,
            "偶数深度=外环、奇数深度=洞，嵌套必须逐层交替");
    }

    /// <summary>绕向不定：拓扑层追踪出的环 CW/CCW 随起点而变，三角化必须自己归一化。</summary>
    [Test]
    public void Fill_IsOrientationAgnostic()
    {
        float AreaFor(bool reverseOuter, bool reverseHole)
        {
            var outer = Square(0f, 0f, 30f);
            var hole = Square(10f, 10f, 10f);
            if (reverseOuter) outer.Reverse();
            if (reverseHole) hole.Reverse();

            var verts = new List<Vector2>();
            var tris = new List<int>();
            new PlacementMaskFill().Triangulate(
                new List<List<Vector2>> { outer, hole }, verts, tris);
            return TriangleAreaSum(verts, tris);
        }

        foreach (bool ro in new[] { false, true })
            foreach (bool rh in new[] { false, true })
                Assert.AreEqual(800f, AreaFor(ro, rh), 1e-1f,
                    $"绕向组合 outer={ro} hole={rh} 下面积应不变");
    }

    /// <summary>不连通的两块各自成环，两块都要填。</summary>
    [Test]
    public void Fill_DisjointLoops_BothFilled()
    {
        var loops = new List<List<Vector2>> { Square(0f, 0f, 10f), Square(50f, 50f, 20f) };
        var verts = new List<Vector2>();
        var tris = new List<int>();

        new PlacementMaskFill().Triangulate(loops, verts, tris);

        Assert.AreEqual(100f + 400f, TriangleAreaSum(verts, tris), 1e-1f);
    }

    /// <summary>
    /// 多个洞同时存在（回归用例）。
    ///
    /// 早期实现走「桥接 + 耳切」：每个洞用一条退化双向边缝进外环。缝合口附近会出现
    /// 整圈顶点全为凹的中间状态，一个耳都找不到 → 循环带着十几个没消费的顶点提前退出，
    /// 这些顶点围出的面积被**静默丢弃**，同时已切出的三角互相重叠，
    /// 面积反而比外环还大（随机多洞用例稳定 9/4000 命中，最差偏差 24%）。
    /// 现在的扫描线不做桥接，多洞与单洞走的是同一条路径。
    /// </summary>
    [Test]
    public void Fill_MultipleHoles_AllExcludedWithoutOverlap()
    {
        var loops = new List<List<Vector2>>
        {
            Square(0f, 0f, 60f),
            Square(5f, 5f, 10f),
            Square(25f, 5f, 10f),
            Square(5f, 30f, 10f),
            Square(30f, 30f, 15f),
        };
        var verts = new List<Vector2>();
        var tris = new List<int>();

        new PlacementMaskFill().Triangulate(loops, verts, tris);

        Assert.AreEqual(3600f - 100f - 100f - 100f - 225f,
            TriangleAreaSum(verts, tris), 1e-1f,
            "每个洞都要挖掉，且三角不能互相重叠（偏大 = 有叠，偏小 = 丢面积）");
    }

    /// <summary>
    /// 重复顶点不能让填充退化。DedupClosed 已在上游去重，但圆角与投影仍可能留下
    /// 距离极近的点；扫描线对此免疫（重复点只是多切一条零高度的带），
    /// 这里钉住该性质，避免日后换实现时又栽在同一处。
    /// </summary>
    [Test]
    public void Fill_DuplicateVertices_StillFillsFullArea()
    {
        var loops = new List<List<Vector2>>
        {
            new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(20f, 0f), new Vector2(20f, 0f),
                new Vector2(20f, 20f), new Vector2(0f, 20f), new Vector2(0f, 20f),
            },
        };
        var verts = new List<Vector2>();
        var tris = new List<int>();

        new PlacementMaskFill().Triangulate(loops, verts, tris);

        Assert.AreEqual(400f, TriangleAreaSum(verts, tris), 1e-1f);
    }

    /// <summary>
    /// 端到端：真实六边形集合走完整流水线（拓扑 → 简化 → 圆角）后仍能三角化，
    /// 且填充边界与描边输入是同一批点（本次修复的核心不变量）。
    /// </summary>
    [Test]
    public void Fill_ConsumesSameLoopsAsStroke_ForRealHexRegion()
    {
        var coords = new List<Vector3>();
        for (int x = 0; x < 4; x++)
            for (int z = 0; z < 4; z++)
            {
                if (x == 3 && z == 3) continue; // 凹角
                var h = new Vector3(x, -x - z, z);
                AddCell(x * 4 + z, h);
                coords.Add(h);
            }

        var topo = PlacementMaskTopology.Build(Cells(coords.ToArray()), OuterRadius, ElevationStep);

        var loops = new List<List<Vector2>>();
        foreach (var loop in topo.Loops)
        {
            var world = new List<Vector3>();
            foreach (int idx in loop) world.Add(topo.CornerWorld[idx]);

            var simp = new List<Vector3>();
            PlacementMaskOutline.SimplifyClosed(world, 0.6f * OuterRadius, simp);
            var rounded = new List<Vector3>();
            PlacementMaskOutline.RoundCorners(simp, 0.55f * OuterRadius, 5, rounded);

            var flat = new List<Vector2>();
            foreach (var p in rounded) flat.Add(new Vector2(p.x, p.z));
            var dedup = new List<Vector2>();
            PlacementMaskOutline.DedupClosed(flat, 0.5f, dedup);
            if (dedup.Count >= 3) loops.Add(dedup);
        }
        Assert.Greater(loops.Count, 0, "真实区域应产出可用闭环");

        var verts = new List<Vector2>();
        var tris = new List<int>();
        new PlacementMaskFill().Triangulate(loops, verts, tris);

        Assert.Greater(tris.Count, 0, "拟合后的闭环必须能三角化（否则填充整块消失）");
        Assert.AreEqual(0, tris.Count % 3);
        foreach (int idx in tris)
            Assert.IsTrue(idx >= 0 && idx < verts.Count, "索引须落在顶点表内");

        // 填充顶点集合 ⊇ 闭环上的点：两层同源，描边中线上的每个点都是填充边界点。
        var fillSet = new HashSet<Vector2>(verts);
        foreach (var loop in loops)
            foreach (var p in loop)
                Assert.IsTrue(fillSet.Contains(p),
                    "描边路径上的点必须同时是填充边界点（两层几何同源）");

        float area = TriangleAreaSum(verts, tris);
        Assert.Greater(area, 0f, "填充面积必须为正");
    }

    // ---------------- 描边缎带 ----------------

    [Test]
    public void BuildRibbon_EmitsFiveVertsPerPointAndEightTrisPerSegment()
    {
        var path = new List<Vector2>
        {
            new Vector2(0f, 0f), new Vector2(10f, 0f),
            new Vector2(10f, 10f), new Vector2(0f, 10f),
        };
        var verts = new List<Vector2>();
        var colors = new List<Color32>();
        var tris = new List<int>();

        PlacementMaskOutline.BuildRibbon(path, 2f, Color.red, verts, colors, tris);

        Assert.AreEqual(path.Count * 5, verts.Count, "每点 5 列顶点（外缘/外芯边/中线/内芯边/内缘）");
        Assert.AreEqual(verts.Count, colors.Count, "顶点色须与顶点一一对应");
        Assert.AreEqual(path.Count * 8 * 3, tris.Count, "每段 8 个三角形（闭合环，段数=点数）");
        foreach (int idx in tris)
            Assert.IsTrue(idx >= 0 && idx < verts.Count);
    }

    [Test]
    public void BuildRibbon_EdgeVertsAreTransparent_CoreIsOpaque()
    {
        var path = new List<Vector2>
        {
            new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(5f, 8f),
        };
        var verts = new List<Vector2>();
        var colors = new List<Color32>();
        var tris = new List<int>();

        PlacementMaskOutline.BuildRibbon(path, 2f, new Color(1f, 0f, 0f, 1f), verts, colors, tris,
            coreRatio: 0.5f);

        for (int i = 0; i < colors.Count; i += 5)
        {
            Assert.AreEqual(0, colors[i].a, "外缘应全透明（羽化）");
            Assert.AreEqual(255, colors[i + 1].a, "外芯边应取 tint 的 alpha");
            Assert.AreEqual(255, colors[i + 2].a, "中线应取 tint 的 alpha");
            Assert.AreEqual(255, colors[i + 3].a, "内芯边应取 tint 的 alpha");
            Assert.AreEqual(0, colors[i + 4].a, "内缘应全透明（羽化）");
        }
    }

    /// <summary>coreRatio=0 时芯边与中线重合 → 几何上等价于旧的三列纯羽化缎带。</summary>
    [Test]
    public void BuildRibbon_ZeroCoreRatio_CollapsesCoreOntoCenterline()
    {
        var path = new List<Vector2>
        {
            new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(5f, 8f),
        };
        var verts = new List<Vector2>();
        var colors = new List<Color32>();
        var tris = new List<int>();

        PlacementMaskOutline.BuildRibbon(path, 2f, new Color(1f, 0f, 0f, 1f), verts, colors, tris,
            coreRatio: 0f);

        for (int i = 0; i < verts.Count; i += 5)
        {
            Assert.AreEqual(verts[i + 2], verts[i + 1], "coreRatio=0 时外芯边应与中线重合");
            Assert.AreEqual(verts[i + 2], verts[i + 3], "coreRatio=0 时内芯边应与中线重合");
        }
    }

    /// <summary>coreRatio=1 时芯边推到外缘 → 整条缎带实心（顶点重合、无渐变区）。</summary>
    [Test]
    public void BuildRibbon_FullCoreRatio_LeavesNoFeatherBand()
    {
        var path = new List<Vector2>
        {
            new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(5f, 8f),
        };
        var verts = new List<Vector2>();
        var colors = new List<Color32>();
        var tris = new List<int>();

        PlacementMaskOutline.BuildRibbon(path, 2f, new Color(1f, 0f, 0f, 1f), verts, colors, tris,
            coreRatio: 1f);

        for (int i = 0; i < verts.Count; i += 5)
        {
            Assert.AreEqual(verts[i], verts[i + 1], "coreRatio=1 时外芯边应与外缘重合（羽化带宽度为 0）");
            Assert.AreEqual(verts[i + 4], verts[i + 3], "coreRatio=1 时内芯边应与内缘重合");
        }
    }

    [Test]
    public void SmoothClosed_IncreasesPointCountAndStaysFinite()
    {
        var loop = new List<Vector2>
        {
            new Vector2(0f, 0f), new Vector2(10f, 0f),
            new Vector2(10f, 10f), new Vector2(0f, 10f),
        };
        var outLoop = new List<Vector2>();

        PlacementMaskOutline.SmoothClosed(loop, 4, 0.5f, outLoop);

        Assert.AreEqual(loop.Count * 4, outLoop.Count, "每段插 4 点");
        foreach (var p in outLoop)
        {
            Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y), "平滑不应产出 NaN");
            Assert.IsTrue(Mathf.Abs(p.x) < 100f && Mathf.Abs(p.y) < 100f, "样条不应外溢");
        }
    }

    [Test]
    public void SmoothClosed_DedupesNearDuplicatePoints()
    {
        var loop = new List<Vector2>
        {
            new Vector2(0f, 0f), new Vector2(0.01f, 0f), // 近重复
            new Vector2(10f, 0f), new Vector2(10f, 10f),
        };
        var outLoop = new List<Vector2>();

        PlacementMaskOutline.SmoothClosed(loop, 1, 0.5f, outLoop);

        Assert.AreEqual(3, outLoop.Count, "近重复点应被合并（否则样条退化出尖刺）");
    }

    // ---------------- 轮廓拟合：简化 + 圆角 ----------------

    /// <summary>本 feature 的核心诉求：一排格子的直边不该是锯齿，简化后应逼近直线。</summary>
    [Test]
    public void SimplifyClosed_FlattensSawtoothAlongStraightRun()
    {
        // 沿 E 方向排 12 格：上下两条边界本是锯齿（振幅 = R - R*cos60° = 0.5R）。
        var coords = new List<Vector3>();
        for (int x = 0; x < 12; x++)
        {
            var h = new Vector3(x, -x, 0);
            AddCell(x, h);
            coords.Add(h);
        }

        var topo = PlacementMaskTopology.Build(Cells(coords.ToArray()), OuterRadius, ElevationStep);
        Assert.AreEqual(1, topo.Loops.Count, "一条连通带应只有一个外环");

        var loopWorld = new List<Vector3>();
        foreach (int idx in topo.Loops[0]) loopWorld.Add(topo.CornerWorld[idx]);
        Assert.AreEqual(50, loopWorld.Count, "12 格连排 = 72 - 11*2 = 50 个角点");

        var simplified = new List<Vector3>();
        PlacementMaskOutline.SimplifyClosed(loopWorld, 0.6f * OuterRadius, simplified);

        // 锯齿被当噪声删掉后只剩「长条」的几个转折。留 8 的余量给两端的斜切。
        Assert.LessOrEqual(simplified.Count, 8, "直边上的锯齿应被抹平（否则等于没简化）");
        Assert.GreaterOrEqual(simplified.Count, 3, "简化结果仍必须是合法闭环");
    }

    /// <summary>容差不能大到把真实特征也吃掉：边界缺一格的凹口必须留下。</summary>
    [Test]
    public void SimplifyClosed_PreservesSingleCellNotch()
    {
        int SimplifiedCount(bool withNotch)
        {
            _cells.Clear();
            var coords = new List<Vector3>();
            for (int x = 0; x < 5; x++)
                for (int z = 0; z < 5; z++)
                {
                    if (withNotch && x == 0 && z == 2) continue; // 边界上挖掉一格 → 凹口
                    var h = new Vector3(x, -x - z, z);
                    AddCell(x * 5 + z, h);
                    coords.Add(h);
                }

            var t = PlacementMaskTopology.Build(Cells(coords.ToArray()), OuterRadius, ElevationStep);
            var world = new List<Vector3>();
            int longest = 0;
            for (int i = 1; i < t.Loops.Count; i++)
                if (t.Loops[i].Count > t.Loops[longest].Count) longest = i;
            foreach (int idx in t.Loops[longest]) world.Add(t.CornerWorld[idx]);

            var simp = new List<Vector3>();
            PlacementMaskOutline.SimplifyClosed(world, 0.6f * OuterRadius, simp);
            return simp.Count;
        }

        int plain = SimplifiedCount(false);
        int notched = SimplifiedCount(true);
        Assert.Greater(notched, plain, "凹口是真实特征，简化后必须比无凹口版本多出转折点");
    }

    [Test]
    public void RoundCorners_EmitsBezierPerVertex_AndStaysFinite()
    {
        var poly = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f),
            new Vector3(10f, 0f, 10f), new Vector3(0f, 0f, 10f),
        };
        var outPoly = new List<Vector3>();

        PlacementMaskOutline.RoundCorners(poly, 1f, 4, outPoly);

        Assert.AreEqual(poly.Count * 5, outPoly.Count, "每个顶点出 segments+1 个贝塞尔采样点");
        foreach (var p in outPoly)
            Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.z), "圆角不应产出 NaN");
    }

    /// <summary>切点按相邻边半长夹取 ⇒ 圆角永不外溢，即使半径远大于边长或遇到极锐角。</summary>
    [Test]
    public void RoundCorners_NeverOvershootsInputBounds()
    {
        // 带一根尖刺的多边形：锐角处最容易外溢。
        var poly = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f),
            new Vector3(5f, 0f, 0.5f),                          // 尖刺
            new Vector3(10f, 0f, 10f), new Vector3(0f, 0f, 10f),
        };

        foreach (float radius in new[] { 2f, 5f, 40f })
        {
            var outPoly = new List<Vector3>();
            PlacementMaskOutline.RoundCorners(poly, radius, 4, outPoly);

            foreach (var p in outPoly)
            {
                Assert.IsTrue(p.x >= -1e-3f && p.x <= 10f + 1e-3f, $"r={radius} 时 x 外溢：{p.x}");
                Assert.IsTrue(p.z >= -1e-3f && p.z <= 10f + 1e-3f, $"r={radius} 时 z 外溢：{p.z}");
            }
        }
    }

    [Test]
    public void DedupClosed_MergesNearDuplicatesIncludingWrapAround()
    {
        var loop = new List<Vector2>
        {
            new Vector2(0f, 0f),
            new Vector2(0.01f, 0f),   // 与前一点近重复
            new Vector2(10f, 0f),
            new Vector2(10f, 10f),
            new Vector2(0.02f, 0.02f) // 与首点近重复（首尾环绕）
        };
        var outLoop = new List<Vector2>();

        PlacementMaskOutline.DedupClosed(loop, 0.5f, outLoop);

        Assert.AreEqual(3, outLoop.Count, "相邻重复与首尾重合都应被合并");
    }
}
