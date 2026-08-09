using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 【程序化山脉-阶段 5.1】山体动画来源 DTO 与山体拓扑签名（纯函数基线测试，决策 ㉙/㉛）。
/// 覆盖：动画来源等权归一化与 Weights 平行一致性；拓扑签名同输入重建一致、
/// 仅 Height 变化不变、清除/水淹/恢复/阈值跨越改变、布局与索引内容变化检测。
/// 本阶段只扩 DTO/诊断，不替换 AppendIdentityAnimUV——运行时动画表现零变化。
/// </summary>
public class MountainTopologyTests
{
    private MapLandFormSO _mountainForm;

    /// <summary>懒创建：纯签名/来源测试不依赖 Unity 运行时实例，SetUp 阶段不分配原生对象。</summary>
    private MapLandFormSO MountainForm
    {
        get
        {
            if (_mountainForm == null)
            {
                _mountainForm = ScriptableObject.CreateInstance<MapLandFormSO>();
                _mountainForm.mountainForm = true;
            }
            return _mountainForm;
        }
    }

    [SetUp]
    public void SetUp()
    {
        WaterLevelConfig.WaterLevel = 1f;
        WaterLevelConfig.MaxHeight = 5f;
    }

    [TearDown]
    public void TearDown()
    {
        if (_mountainForm != null)
        {
            Object.DestroyImmediate(_mountainForm);
            _mountainForm = null;
        }
    }

    // ── MountainVertexAnimSource：等权归一化与诊断 ─────────────────

    [Test]
    public void Uniform_ProducesNormalizedWeights_SummingToOne()
    {
        MountainVertexAnimSource[] sources = MountainVertexAnimSource.Uniform(new[] { Cell(1), Cell(2), Cell(3) });

        Assert.AreEqual(3, sources.Length);
        float sum = 0f;
        foreach (MountainVertexAnimSource s in sources)
        {
            Assert.IsNotNull(s.Cell, "来源格不得为 null");
            Assert.AreEqual(1f / 3f, s.Weight, 1e-6f);
            sum += s.Weight;
        }
        Assert.AreEqual(1f, sum, 1e-6f, "权重归一化：总和恒 1");
    }

    [Test]
    public void Uniform_SingleCell_WeightIsOne()
    {
        MountainVertexAnimSource[] sources = MountainVertexAnimSource.Uniform(new[] { Cell(7) });

        Assert.AreEqual(1, sources.Length);
        Assert.AreEqual(1f, sources[0].Weight, 1e-6f);
    }

    [Test]
    public void Uniform_NullAndEmptyHandling()
    {
        Assert.IsNull(MountainVertexAnimSource.Uniform(null));
        Assert.IsEmpty(MountainVertexAnimSource.Uniform(new HexCellData[0]));
    }

    [Test]
    public void IsValid_RejectsMalformedSources()
    {
        HexCellData cell = Cell(1);
        Assert.IsTrue(MountainVertexAnimSource.IsValid(
            new List<MountainVertexAnimSource[]> { new[] { new MountainVertexAnimSource(cell, 1f) } }));

        Assert.IsFalse(MountainVertexAnimSource.IsValid(
            new List<MountainVertexAnimSource[]> { new[] { new MountainVertexAnimSource(cell, 0.5f) } }), "总和 ≠ 1");
        Assert.IsFalse(MountainVertexAnimSource.IsValid(
            new List<MountainVertexAnimSource[]> { new[] { new MountainVertexAnimSource(cell, 1.5f) } }), "权重 > 1");
        Assert.IsFalse(MountainVertexAnimSource.IsValid(
            new List<MountainVertexAnimSource[]> { new MountainVertexAnimSource[0] }), "空顶点来源");
        Assert.IsFalse(MountainVertexAnimSource.IsValid(
            new List<MountainVertexAnimSource[]> { new[] { new MountainVertexAnimSource(null, 1f) } }), "来源格为 null");
        Assert.IsFalse(MountainVertexAnimSource.IsValid(
            new List<MountainVertexAnimSource[]> { null }), "null 顶点来源");
    }

    // ── CellGeometry.AnimSources：规范化共享顶点动画来源（阶段 5.2）──

    [Test]
    public void SolidGeometry_AnimSources_AllUnitOfSelf()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        CellGeometry geo = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);

        Assert.AreEqual(geo.Vertices.Length, geo.Weights.Count);
        Assert.AreEqual(geo.Vertices.Length, geo.AnimSources.Count, "AnimSources 与顶点一一对应");
        Assert.IsTrue(MountainVertexAnimSource.IsValid(geo.AnimSources));

        // 决策 ㉙/5.2：主峰与环点基点 = 本格 solid 顶点 Y（隆起与 Height 无关）⇒ 全部 [本格, 1]
        foreach (MountainVertexAnimSource[] sources in geo.AnimSources)
        {
            Assert.AreEqual(1, sources.Length, "solid 顶点来源 = 单格");
            Assert.AreSame(_cellA, sources[0].Cell);
            Assert.AreEqual(1f, sources[0].Weight, 1e-6f);
        }
    }

    [Test]
    public void RectGeometry_AnimSources_ProfileUWeightedMix()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);

        // 山-山 rect：profile 长度 2（u=0/1）⇒ 端点 [A,1]/[B,1]，无中间点
        MountainRectBuild build = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellB, solidA, solidB, Enums.HexDirection.NE, _neighborOf);
        CellGeometry geo = MountainGeometryBuilder.RectToRender(build, _cellA, _cellB);

        Assert.AreEqual(geo.Vertices.Length, geo.AnimSources.Count);
        Assert.IsTrue(MountainVertexAnimSource.IsValid(geo.AnimSources));
        for (int i = 0; i < geo.AnimSources.Count; i++)
        {
            MountainVertexAnimSource[] sources = geo.AnimSources[i];
            Assert.AreEqual(2, sources.Length, "rect 顶点来源 = owner/neighbor 两格");
            float u = build.Rect.UVs[build.Rect.Indices[i]].y; // profile 进度
            Assert.AreEqual(1f - u, sources[0].Weight, 1e-6f, $"owner 权重 1-u");
            Assert.AreEqual(u, sources[1].Weight, 1e-6f, $"neighbor 权重 u");
            Assert.AreSame(_cellA, sources[0].Cell);
            Assert.AreSame(_cellB, sources[1].Cell);
        }
    }

    [Test]
    public void Rect_MountainPlain_BoundaryAnchor_HalfHalf()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidD = CreateSolid(_cellD.CenterWorldCoordinate);

        // 山-普通 profile 长度 3：u=0 → [A,1]、u=0.5（格界锚点）→ [A:0.5,D:0.5]、u=1 → [D,1]
        MountainRectBuild build = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellD, solidA, solidD, Enums.HexDirection.SE, _neighborOf);
        CellGeometry geo = MountainGeometryBuilder.RectToRender(build, _cellA, _cellD);

        Assert.IsTrue(MountainVertexAnimSource.IsValid(geo.AnimSources));
        for (int i = 0; i < geo.AnimSources.Count; i++)
        {
            int srcIndex = build.Rect.Indices[i];
            float u = build.Rect.UVs[srcIndex].y;
            MountainVertexAnimSource[] sources = geo.AnimSources[i];
            Assert.AreEqual(1f - u, sources[0].Weight, 1e-6f);
            Assert.AreEqual(u, sources[1].Weight, 1e-6f);
            Assert.AreSame(_cellA, sources[0].Cell);
            Assert.AreSame(_cellD, sources[1].Cell);
            if (Mathf.Abs(u - 0.5f) < 1e-6f)
            {
                Assert.AreEqual(0.5f, sources[0].Weight, 1e-6f, "格界锚点 = 两端格基础地形旧高各半，隆起 0");
                Assert.AreEqual(0.5f, sources[1].Weight, 1e-6f);
            }
        }
    }

    [Test]
    public void SharedEdgePosition_RectEndpointAndSolidRing_IdenticalSources()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);

        MountainRectBuild build = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellB, solidA, solidB, Enums.HexDirection.NE, _neighborOf);
        CellGeometry rect = MountainGeometryBuilder.RectToRender(build, _cellA, _cellB);
        CellGeometry solidAGeo = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);
        CellGeometry solidBGeo = MountainGeometryBuilder.BuildSolidMountain(_cellB, solidB, _neighborOf);

        // A 侧端点 = A 环边点 7；B 侧端点 = B 环边点 14（镜像共享位置，决策 ④ 规范化共享边）
        Vector3 aSide = build.Rect.Profiles[1].Points[0];
        Vector3 bSide = build.Rect.Profiles[1].Points[1];
        int rectA = FindVertex(rect.Vertices, aSide);
        int rectB = FindVertex(rect.Vertices, bSide);
        int solidA7 = FindVertex(solidAGeo.Vertices, aSide);
        int solidB14 = FindVertex(solidBGeo.Vertices, bSide);
        Assert.GreaterOrEqual(rectA, 0, "rect 含 A 侧端点");
        Assert.GreaterOrEqual(rectB, 0, "rect 含 B 侧端点");
        Assert.GreaterOrEqual(solidA7, 0, "solidA 含环边点 7");
        Assert.GreaterOrEqual(solidB14, 0, "solidB 含镜像环边点 14");

        // solid 环点由 flat 拆分产生 6 份相同副本；rect 端点只需与任一份共享位置且来源一致（[A,1]/[B,1]）
        Assert.AreSame(_cellA, rect.AnimSources[rectA][0].Cell);
        Assert.AreEqual(1f, rect.AnimSources[rectA][0].Weight, 1e-6f);
        Assert.AreSame(_cellA, solidAGeo.AnimSources[solidA7][0].Cell);
        Assert.AreEqual(1f, solidAGeo.AnimSources[solidA7][0].Weight, 1e-6f);

        Assert.AreSame(_cellB, rect.AnimSources[rectB][0].Cell, "B 侧端点来源格 = B（跨格构建一致）");
        Assert.AreEqual(1f, rect.AnimSources[rectB][0].Weight, 1e-6f);
        Assert.AreSame(_cellB, solidBGeo.AnimSources[solidB14][0].Cell);
        Assert.AreEqual(1f, solidBGeo.AnimSources[solidB14][0].Weight, 1e-6f);
    }

    private static int FindVertex(Vector3[] vertices, Vector3 target)
    {
        for (int i = 0; i < vertices.Length; i++)
        {
            if ((vertices[i] - target).sqrMagnitude < 1e-8f) return i;
        }
        return -1;
    }

    [Test]
    public void TriangleGeometry_AnimSources_InheritCornerProfiles()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);
        Vector3[] solidC = CreateSolid(_cellC.CenterWorldCoordinate);
        var rects = new Dictionary<(int, Enums.HexDirection), RectangleTransitionMeshData>
        {
            [(_cellA.GenerateOrder, Enums.HexDirection.NE)] = MountainGeometryBuilder.BuildMountainRectData(_cellA, _cellB, solidA, solidB, Enums.HexDirection.NE, _neighborOf).Rect,
            [(_cellB.GenerateOrder, Enums.HexDirection.SE)] = MountainGeometryBuilder.BuildMountainRectData(_cellB, _cellC, solidB, solidC, Enums.HexDirection.SE, _neighborOf).Rect,
            [(_cellA.GenerateOrder, Enums.HexDirection.E)] = MountainGeometryBuilder.BuildMountainRectData(_cellA, _cellC, solidA, solidC, Enums.HexDirection.E, _neighborOf).Rect,
        };

        CellGeometry geo = MountainGeometryBuilder.BuildTriangleMountain(
            _cellA, _neighborOf, (c, d) => rects[(c.GenerateOrder, d)], Enums.HexDirection.NE, Enums.HexDirection.E);

        Assert.AreEqual(geo.Vertices.Length, geo.AnimSources.Count);
        Assert.IsTrue(MountainVertexAnimSource.IsValid(geo.AnimSources));
        // 单三角 = 3 条角 profile 端点（A/B/C 交汇各 1），来源 = 各自本格 [X,1]（从 profile 继承，非另算）。
        // 3 山格 tri 的 3 个角在世界 XZ 上重合，flat 拆分产生多份顶点副本；按"来源格集合"断言。
        Assert.AreEqual(3, geo.Vertices.Length, "3 山格 tri = 单平坦三角");
        var cells = new HashSet<HexCellData>();
        foreach (MountainVertexAnimSource[] sources in geo.AnimSources)
        {
            Assert.AreEqual(1, sources.Length, "tri 顶点来源 = 单格（继承角 profile 端点）");
            Assert.AreEqual(1f, sources[0].Weight, 1e-6f);
            cells.Add(sources[0].Cell);
        }
        Assert.AreEqual(3, cells.Count, "3 个端点分属 A/B/C 三格");
        Assert.IsTrue(cells.Contains(_cellA) && cells.Contains(_cellB) && cells.Contains(_cellC));
    }

    // ── 阶段 5.3：真实动画通道（AppendMountainAnimUV 纯函数）────────

    private static MountainAnimTestData BuildChannels(
        CellGeometry geo, float step, float deltaA, float deltaB, float deltaC,
        float delayA = 0f, float delayB = 0f, float delayC = 0f)
    {
        return BuildChannels(geo, step, new Dictionary<int, float>
        {
            [1] = deltaA, [2] = deltaB, [3] = deltaC
        }, new Dictionary<int, float>
        {
            [1] = delayA, [2] = delayB, [3] = delayC
        });
    }

    private static MountainAnimTestData BuildChannels(
        CellGeometry geo, float step, IReadOnlyDictionary<int, float> deltas, IReadOnlyDictionary<int, float> delays)
    {
        var uv2 = new List<Vector2>();
        var uv3 = new List<Vector2>();
        float Delta(HexCellData cell)
        {
            return deltas != null && deltas.TryGetValue(cell.GenerateOrder, out float d) ? d : 0f;
        }
        float Delay(HexCellData cell)
        {
            return delays != null && delays.TryGetValue(cell.GenerateOrder, out float d) ? d : 0f;
        }
        MountainGeometryBuilder.AppendMountainAnimUV(geo,
            c => Delta(c) * step, c => Delay(c), c => Delay(c) + 0.5f, uv2, uv3);
        return new MountainAnimTestData { UV2 = uv2.ToArray(), UV3 = uv3.ToArray() };
    }

    private sealed class MountainAnimTestData
    {
        public Vector2[] UV2;
        public Vector2[] UV3;
    }

    [Test]
    public void AnimChannels_UniformDelta_AllVerticesMoveTogether()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        CellGeometry solid = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);

        MountainAnimTestData data = BuildChannels(solid, 1f, deltaA: 2f, deltaB: 2f, deltaC: 2f);

        for (int i = 0; i < solid.Vertices.Length; i++)
        {
            Assert.AreEqual(solid.Vertices[i].y - 2f, data.UV2[i].x, 1e-5f, $"顶点 {i} startY = targetY − 统一 delta");
            Assert.AreEqual(solid.Vertices[i].y, data.UV2[i].y, 1e-5f, $"顶点 {i} targetY");
        }
    }

    [Test]
    public void AnimChannels_AdjacentDifferentDeltas_RectMixesByProfileU()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);
        MountainRectBuild build = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellB, solidA, solidB, Enums.HexDirection.NE, _neighborOf);
        CellGeometry rect = MountainGeometryBuilder.RectToRender(build, _cellA, _cellB);

        MountainAnimTestData data = BuildChannels(rect, 1f, deltaA: 3f, deltaB: 1f, deltaC: 0f);

        for (int i = 0; i < rect.Vertices.Length; i++)
        {
            float u = build.Rect.UVs[build.Rect.Indices[i]].y;
            float expectedDelta = (1f - u) * 3f + u * 1f;
            Assert.AreEqual(rect.Vertices[i].y - expectedDelta, data.UV2[i].x, 1e-5f,
                $"顶点 {i} 按 profile u={u} 混合两端格 delta");
            // delayEnd − delayStart = 0.5（混合后仍成立）
            Assert.AreEqual(0.5f, data.UV3[i].y - data.UV3[i].x, 1e-5f);
        }
    }

    [Test]
    public void AnimChannels_CrossChunkSharedEdge_IdenticalChannels()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);

        // A Chunk：rect(A,NE) 的 B 侧端点；B Chunk：B 的 solid 环点（镜像共享位置）
        MountainRectBuild build = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellB, solidA, solidB, Enums.HexDirection.NE, _neighborOf);
        CellGeometry rect = MountainGeometryBuilder.RectToRender(build, _cellA, _cellB);
        CellGeometry solidBGeo = MountainGeometryBuilder.BuildSolidMountain(_cellB, solidB, _neighborOf);

        MountainAnimTestData rectData = BuildChannels(rect, 1f, deltaA: 3f, deltaB: 1f, deltaC: 0f);
        MountainAnimTestData solidData = BuildChannels(solidBGeo, 1f, deltaA: 3f, deltaB: 1f, deltaC: 0f);

        // 共享位置 = rect 的 B 侧端点（u=1 → [B,1]）与 B solid 镜像环点（[B,1]）
        Vector3 bSide = build.Rect.Profiles[1].Points[1];
        int rectB = FindVertex(rect.Vertices, bSide);
        int solidB14 = FindVertex(solidBGeo.Vertices, bSide);
        Assert.GreaterOrEqual(rectB, 0);
        Assert.GreaterOrEqual(solidB14, 0);

        Vector2 rectBChannel = rectData.UV2[rectB];
        Vector2 solidBChannel = solidData.UV2[solidB14];
        Assert.AreEqual(rectBChannel.x, solidBChannel.x, 1e-4f, "跨 Chunk 共享位置 startY 一致");
        Assert.AreEqual(rectBChannel.y, solidBChannel.y, 1e-4f, "跨 Chunk 共享位置 targetY 一致");
        Assert.AreEqual(rectData.UV3[rectB].x, solidData.UV3[solidB14].x, 1e-6f, "delayStart 一致");
        Assert.AreEqual(rectData.UV3[rectB].y, solidData.UV3[solidB14].y, 1e-6f, "delayEnd 一致");
    }

    [Test]
    public void AnimChannels_TriangleCorners_OwnCellDelta()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);
        Vector3[] solidC = CreateSolid(_cellC.CenterWorldCoordinate);
        var rects = new Dictionary<(int, Enums.HexDirection), RectangleTransitionMeshData>
        {
            [(_cellA.GenerateOrder, Enums.HexDirection.NE)] = MountainGeometryBuilder.BuildMountainRectData(_cellA, _cellB, solidA, solidB, Enums.HexDirection.NE, _neighborOf).Rect,
            [(_cellB.GenerateOrder, Enums.HexDirection.SE)] = MountainGeometryBuilder.BuildMountainRectData(_cellB, _cellC, solidB, solidC, Enums.HexDirection.SE, _neighborOf).Rect,
            [(_cellA.GenerateOrder, Enums.HexDirection.E)] = MountainGeometryBuilder.BuildMountainRectData(_cellA, _cellC, solidA, solidC, Enums.HexDirection.E, _neighborOf).Rect,
        };
        CellGeometry tri = MountainGeometryBuilder.BuildTriangleMountain(
            _cellA, _neighborOf, (c, d) => rects[(c.GenerateOrder, d)], Enums.HexDirection.NE, Enums.HexDirection.E);

        MountainAnimTestData data = BuildChannels(tri, 1f, deltaA: 3f, deltaB: 1f, deltaC: 2f);

        for (int i = 0; i < tri.Vertices.Length; i++)
        {
            float expectedDelta = tri.AnimSources[i][0].Cell == _cellA ? 3f
                : tri.AnimSources[i][0].Cell == _cellB ? 1f : 2f;
            Assert.AreEqual(tri.Vertices[i].y - expectedDelta, data.UV2[i].x, 1e-5f,
                $"tri 顶点 {i} 按所属格 delta（3 山格交汇角点 XZ 重合，按来源格分派）");
        }
    }

    [Test]
    public void AnimChannels_SameInput_RebuildIdenticalHash()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        CellGeometry geo = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);

        MountainAnimTestData first = BuildChannels(geo, 1f, deltaA: 3f, deltaB: 1f, deltaC: 2f);
        MountainAnimTestData second = BuildChannels(geo, 1f, deltaA: 3f, deltaB: 1f, deltaC: 2f);

        for (int i = 0; i < first.UV2.Length; i++)
        {
            Assert.AreEqual(first.UV2[i].x, second.UV2[i].x, 1e-6f, "通道确定性：固定输入重建一致");
            Assert.AreEqual(first.UV2[i].y, second.UV2[i].y, 1e-6f);
            Assert.AreEqual(first.UV3[i].x, second.UV3[i].x, 1e-6f);
            Assert.AreEqual(first.UV3[i].y, second.UV3[i].y, 1e-6f);
        }
    }

    [Test]
    public void DynamicHeightChange_AnimatedEndState_EqualsDirectBuild()
    {
        // 阶段 7.3：动态路径确定性——拓扑不变时 Height 变化后重建，动画通道的终点（UV2.y）
        // 必须等于"直接按新数据构建"的顶点 Y（动画提交后终态 = 直接构建，决策 ㉛）；
        // 起点（UV2.x）必须等于旧数据构建的顶点 Y（动画从旧地形开始插值）。
        BuildMountainFixture();
        Vector3[] oldA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] oldB = CreateSolid(_cellB.CenterWorldCoordinate);
        Vector3[] oldC = CreateSolid(_cellC.CenterWorldCoordinate);
        Vector3[] newA = OffsetY(oldA, DeltaA);
        Vector3[] newB = OffsetY(oldB, DeltaB);
        Vector3[] newC = OffsetY(oldC, DeltaC);

        CellGeometry oldSolid = MountainGeometryBuilder.BuildSolidMountain(_cellA, oldA, _neighborOf);
        CellGeometry newSolid = MountainGeometryBuilder.BuildSolidMountain(_cellA, newA, _neighborOf);
        MountainRectBuild oldBuild = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellB, oldA, oldB, Enums.HexDirection.NE, _neighborOf);
        MountainRectBuild newBuild = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellB, newA, newB, Enums.HexDirection.NE, _neighborOf);
        CellGeometry oldRect = MountainGeometryBuilder.RectToRender(oldBuild, _cellA, _cellB);
        CellGeometry newRect = MountainGeometryBuilder.RectToRender(newBuild, _cellA, _cellB);
        CellGeometry oldTri = BuildFixtureTriangle(oldA, oldB, oldC);
        CellGeometry newTri = BuildFixtureTriangle(newA, newB, newC);

        AssertChannelEndState(newSolid, oldSolid, "solid A 动画通道");
        AssertChannelEndState(newRect, oldRect, "rect A-B 动画通道");
        AssertChannelEndState(newTri, oldTri, "3 山格 tri 动画通道");
    }

    private const float DeltaA = 3f;
    private const float DeltaB = 1f;
    private const float DeltaC = 2f;

    private static Vector3[] OffsetY(Vector3[] source, float delta)
    {
        var copy = new Vector3[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            copy[i] = source[i];
            copy[i].y += delta;
        }
        return copy;
    }

    private float FixtureDelta(HexCellData cell)
    {
        if (cell == _cellA) return DeltaA;
        if (cell == _cellB) return DeltaB;
        return DeltaC;
    }

    private CellGeometry BuildFixtureTriangle(Vector3[] solidA, Vector3[] solidB, Vector3[] solidC)
    {
        var rects = new Dictionary<(int, Enums.HexDirection), RectangleTransitionMeshData>
        {
            [(_cellA.GenerateOrder, Enums.HexDirection.NE)] = MountainGeometryBuilder.BuildMountainRectData(
                _cellA, _cellB, solidA, solidB, Enums.HexDirection.NE, _neighborOf).Rect,
            [(_cellB.GenerateOrder, Enums.HexDirection.SE)] = MountainGeometryBuilder.BuildMountainRectData(
                _cellB, _cellC, solidB, solidC, Enums.HexDirection.SE, _neighborOf).Rect,
            [(_cellA.GenerateOrder, Enums.HexDirection.E)] = MountainGeometryBuilder.BuildMountainRectData(
                _cellA, _cellC, solidA, solidC, Enums.HexDirection.E, _neighborOf).Rect,
        };
        return MountainGeometryBuilder.BuildTriangleMountain(
            _cellA, _neighborOf, (c, d) => rects[(c.GenerateOrder, d)], Enums.HexDirection.NE, Enums.HexDirection.E);
    }

    private void AssertChannelEndState(CellGeometry newGeo, CellGeometry oldGeo, string label)
    {
        Assert.AreEqual(newGeo.Vertices.Length, oldGeo.Vertices.Length, $"{label}：新旧构建顶点数一致");
        var uv2 = new List<Vector2>();
        var uv3 = new List<Vector2>();
        MountainGeometryBuilder.AppendMountainAnimUV(newGeo, FixtureDelta, c => 0f, c => 0.5f, uv2, uv3);
        Assert.AreEqual(newGeo.Vertices.Length, uv2.Count, $"{label}：通道等长");
        for (int i = 0; i < newGeo.Vertices.Length; i++)
        {
            Assert.AreEqual(newGeo.Vertices[i].y, uv2[i].y, 1e-4f,
                $"{label}：顶点{i} 动画终态 = 直接按新数据构建（决策 ㉛）");
            Assert.AreEqual(oldGeo.Vertices[i].y, uv2[i].x, 1e-4f,
                $"{label}：顶点{i} 动画起点 = 旧数据构建");
        }
    }

    [Test]
    public void CrossChunkSharedEdge_RebuildSide_Uv0AndChannelsMatchExistingSide()
    {
        // 阶段 7.3：跨 Chunk 相邻重建——仅一侧 Chunk 重建时，共享边顶点数据必须与另一侧
        // 现存顶点数值一致（< 1e-4）：位置、UV0.x（同脊线 ridgeKey01）、UV2/UV3 通道。
        // B 并入 A 的脊线（ridgeId 相同）后，rect（A 拥有）与 B solid 在共享位置上的
        // ridgeKey01 必须相同（决策 ㉓/㉛）。
        BuildMountainFixture();
        _cellB.mountainRidge.ridgeId = 1;
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);

        CellGeometry solidAGeo = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);
        CellGeometry solidBGeo = MountainGeometryBuilder.BuildSolidMountain(_cellB, solidB, _neighborOf);
        MountainRectBuild build = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellB, solidA, solidB, Enums.HexDirection.NE, _neighborOf);
        CellGeometry rect = MountainGeometryBuilder.RectToRender(build, _cellA, _cellB);

        // 共享位置：A 侧 = A 环边点 7；B 侧 = B 环边点 14（镜像）
        Vector3 aSide = build.Rect.Profiles[1].Points[0];
        Vector3 bSide = build.Rect.Profiles[1].Points[1];
        int rectA = FindVertex(rect.Vertices, aSide);
        int rectB = FindVertex(rect.Vertices, bSide);
        int solidA7 = FindVertex(solidAGeo.Vertices, aSide);
        int solidB14 = FindVertex(solidBGeo.Vertices, bSide);
        Assert.GreaterOrEqual(rectA, 0, "rect 含 A 侧共享点");
        Assert.GreaterOrEqual(rectB, 0, "rect 含 B 侧共享点");
        Assert.GreaterOrEqual(solidA7, 0, "solidA 含共享点");
        Assert.GreaterOrEqual(solidB14, 0, "solidB 含共享点");

        // 位置一致（FindVertex 容差 1e-8 < 1e-4，决策 ㉛）
        Assert.AreEqual(rect.Vertices[rectA], solidAGeo.Vertices[solidA7], "A 侧共享位置一致");
        Assert.AreEqual(rect.Vertices[rectB], solidBGeo.Vertices[solidB14], "B 侧共享位置一致");

        // UV0.x = ridgeKey01：同脊线 ⇒ 两侧一致
        Assert.AreEqual(rect.UVs[rectA].x, solidAGeo.UVs[solidA7].x, 1e-6f, "A 侧共享位置 ridgeKey01 一致");
        Assert.AreEqual(rect.UVs[rectB].x, solidBGeo.UVs[solidB14].x, 1e-6f, "B 侧共享位置 ridgeKey01 一致");

        // UV2/UV3：跨 Chunk 共享位置通道一致（B 侧端点来源 [B,1] == B solid 环点来源 [B,1]）
        float Delta(HexCellData c) => c.GenerateOrder;
        var rectUV2 = new List<Vector2>();
        var rectUV3 = new List<Vector2>();
        MountainGeometryBuilder.AppendMountainAnimUV(
            rect, Delta, c => c.GenerateOrder * 0.1f, c => c.GenerateOrder * 0.1f + 0.5f, rectUV2, rectUV3);
        var solidUV2 = new List<Vector2>();
        var solidUV3 = new List<Vector2>();
        MountainGeometryBuilder.AppendMountainAnimUV(
            solidBGeo, Delta, c => c.GenerateOrder * 0.1f, c => c.GenerateOrder * 0.1f + 0.5f, solidUV2, solidUV3);
        Assert.AreEqual(rectUV2[rectB].x, solidUV2[solidB14].x, 1e-4f, "B 侧共享位置 startY 一致");
        Assert.AreEqual(rectUV2[rectB].y, solidUV2[solidB14].y, 1e-4f, "B 侧共享位置 targetY 一致");
        Assert.AreEqual(rectUV3[rectB].x, solidUV3[solidB14].x, 1e-6f, "B 侧共享位置 delayStart 一致");
        Assert.AreEqual(rectUV3[rectB].y, solidUV3[solidB14].y, 1e-6f, "B 侧共享位置 delayEnd 一致");
    }

    // ── 阶段 5.7：动画保守 bounds（决策 ㉛）────────────────────────

    [Test]
    public void ConservativeBounds_ContainAllStartTargetAndInterpolatedVertices()
    {
        // 模拟：低起点、高山峰、XZ 偏心 —— bounds 必须包含动画全程（progress 0/0.5/1）顶点
        var vertices = new List<Vector3>
        {
            new Vector3(-5f, 0f, -5f), new Vector3(5f, 0f, 5f), new Vector3(0f, 0f, 0f),
        };
        var uv2 = new List<Vector2>
        {
            new Vector2(-5f, 2f),
            new Vector2(1f, 8f),
            new Vector2(0f, 3f),
        };

        Bounds bounds = MountainGeometryBuilder.ComputeConservativeAnimBounds(vertices, uv2, yMargin: 0f);

        for (int i = 0; i < vertices.Count; i++)
        {
            for (float p = 0f; p <= 1f; p += 0.5f)
            {
                float y = Mathf.Lerp(uv2[i].x, uv2[i].y, p);
                Vector3 v = new Vector3(vertices[i].x, y, vertices[i].z);
                Assert.IsTrue(bounds.Contains(v), $"progress={p} 顶点 {i} ({v}) 必须在保守 bounds 内");
            }
        }
        Assert.GreaterOrEqual(bounds.max.y, 8f, "最高目标（山峰）包含");
        Assert.LessOrEqual(bounds.min.y, -5f, "最低起点包含");
        Assert.LessOrEqual(bounds.min.x, -5f, "XZ 偏心包含");
        Assert.GreaterOrEqual(bounds.max.x, 5f);
    }

    [Test]
    public void ConservativeBounds_YMargin_ExpandsBeyondExactExtents()
    {
        var vertices = new List<Vector3> { new Vector3(0f, 0f, 0f) };
        var uv2 = new List<Vector2> { new Vector2(2f, 4f) };

        Bounds exact = MountainGeometryBuilder.ComputeConservativeAnimBounds(vertices, uv2, 0f);
        Bounds margin = MountainGeometryBuilder.ComputeConservativeAnimBounds(vertices, uv2, 0.5f);

        Assert.AreEqual(2f, exact.min.y, 1e-5f);
        Assert.AreEqual(4f, exact.max.y, 1e-5f);
        Assert.LessOrEqual(margin.min.y, 1.5f, "余量向下扩展");
        Assert.GreaterOrEqual(margin.max.y, 4.5f, "余量向上扩展");
    }

    [Test]
    public void ConservativeBounds_EmptyInput_ReturnsZeroBounds()
    {
        Bounds bounds = MountainGeometryBuilder.ComputeConservativeAnimBounds(new List<Vector3>(), new List<Vector2>());
        Assert.AreEqual(Vector3.zero, bounds.center);
    }

    // ── MountainTopologySignature：纯函数签名 ──────────────────────

    [Test]
    public void Signature_Empty_IsDefaultValue()
    {
        MountainTopologySignature empty = MountainTopologySignature.Empty;
        Assert.AreEqual(default(MountainTopologySignature), empty);
        Assert.IsFalse(empty.HasMountain);
        Assert.AreEqual(0, empty.TotalVertexCount);
        Assert.AreEqual(0, empty.MountainIndexCount);
    }

    [Test]
    public void Signature_Build_NoMountain_ReturnsEmptyRegardlessOfOtherInputs()
    {
        MountainTopologySignature signature = MountainTopologySignature.Build(
            hasMountain: false, 999, 42, new[] { 3, 3, 3 }, new[] { 0, 1, 2 }, new[] { 0, 1 },
            new[] { 1, 2, 3 });

        Assert.AreEqual(MountainTopologySignature.Empty, signature);
    }

    [Test]
    public void Signature_SameInput_RebuildIdentical()
    {
        HexCellData cell = CreateMountainCell(distance: 0f, position: 1f, generateOrder: 5);
        var cells = new List<HexCellData> { cell };

        MountainTopologySignature first = BuildSignature(cells, MountainIndices(12));
        MountainTopologySignature second = BuildSignature(cells, MountainIndices(12));

        Assert.AreEqual(first, second, "相同输入重建签名必须一致（决策 ㉓/㉛）");
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
    }

    [Test]
    public void Signature_OnlyHeightChange_KeepsSignatureIdentical()
    {
        // 签名不包含纯 Y 值：陆地范围内 Height 变化（高度场/有效山格判定均与 Height 无关）
        // 必须保持签名不变，否则阶段 5.5 会把普通高度动画误判为拓扑改变。
        HexCellData cell = CreateMountainCell(distance: 0f, position: 1f, generateOrder: 5);
        cell.Height = 2f;
        MountainTopologySignature low = BuildSignature(new List<HexCellData> { cell });

        cell.Height = 4f;
        MountainTopologySignature high = BuildSignature(new List<HexCellData> { cell });

        Assert.AreEqual(low, high, "纯 Height 变化 ⇒ 签名不变");
        Assert.IsTrue(MountainGeometryBuilder.HasVisibleMountain(cell), "高度场不依赖 Height 的前提断言");
    }

    [Test]
    public void Signature_ClearWaterThreshold_ChangeSignature_AndRestoreReturns()
    {
        HexCellData cell = CreateMountainCell(distance: 0f, position: 1f, generateOrder: 5);
        var cells = new List<HexCellData> { cell };
        MountainTopologySignature original = BuildSignature(cells, MountainIndices(12));

        // 清除（决策 ㉕）：HasVisibleMountain 翻转为 false ⇒ 签名改变
        cell.mountainCleared = true;
        MountainTopologySignature cleared = BuildSignature(cells, MountainIndices(12));
        Assert.AreNotEqual(original, cleared, "清除山体 ⇒ 签名改变");

        // 恢复：清除撤销 ⇒ 回到原签名（相同输入重建一致）
        cell.mountainCleared = false;
        Assert.AreEqual(original, BuildSignature(cells, MountainIndices(12)), "恢复 ⇒ 签名回到原值");

        // 水淹（决策 ⑦）：IsEffectiveMountainCell 翻转为 false ⇒ 签名改变
        cell.Height = 0.5f;
        MountainTopologySignature flooded = BuildSignature(cells, MountainIndices(12));
        Assert.AreNotEqual(original, flooded, "陆→水 ⇒ 签名改变");

        // 阈值跨越（决策 ⑳）：minVisibleHeight 翻转 ⇒ 签名改变
        cell.Height = 2f;
        cell.mountainRidge.minVisibleHeight = cell.mountainRidge.hMax + 1f;
        MountainTopologySignature belowThreshold = BuildSignature(cells, MountainIndices(12));
        Assert.AreNotEqual(original, belowThreshold, "阈值跨越 ⇒ 签名改变");
    }

    [Test]
    public void Signature_VisibleCellSet_IsOrderIndependent()
    {
        HexCellData a = CreateMountainCell(0f, 1f, 3);
        HexCellData b = CreateMountainCell(0f, 2f, 8);

        MountainTopologySignature first = BuildSignature(new List<HexCellData> { a, b }, MountainIndices(12));
        MountainTopologySignature second = BuildSignature(new List<HexCellData> { b, a }, MountainIndices(12));

        Assert.AreEqual(first, second, "有效山格集合摘要与遍历顺序无关（决策 ㉓）");
    }

    [Test]
    public void Signature_SubMeshLayoutChange_ChangesSignature()
    {
        int[] indices = MountainIndices(12);
        MountainTopologySignature withSlot = MountainTopologySignature.Build(
            true, 200, indices.Length, new[] { 30, 30, 30, 10, 12, indices.Length }, indices, new[] { 0, 1, 2 },
            new List<int>());
        MountainTopologySignature withoutSlot = MountainTopologySignature.Build(
            true, 200, indices.Length, new[] { 30, 30, 30, 10, 12 }, indices, new[] { 0, 1, 2 },
            new List<int>());

        Assert.AreNotEqual(withSlot, withoutSlot, "subMesh 布局变化 ⇒ 签名改变");
    }

    [Test]
    public void Signature_IndicesContentChange_ChangesSignature()
    {
        MountainTopologySignature a = BuildSignature(new List<HexCellData>(), MountainIndices(12));
        MountainTopologySignature b = BuildSignature(new List<HexCellData>(), MountainIndices(12, shift: 3));

        Assert.AreNotEqual(a, b, "山体 indices 内容变化 ⇒ 签名改变（即使数量相同）");
    }

    [Test]
    public void Signature_VertexCountChange_ChangesSignature()
    {
        MountainTopologySignature a = MountainTopologySignature.Build(
            true, 200, 36, new[] { 30, 30, 30, 10, 12, 36 }, MountainIndices(12), new[] { 0, 1, 2 },
            new List<int>());
        MountainTopologySignature b = MountainTopologySignature.Build(
            true, 210, 36, new[] { 30, 30, 30, 10, 12, 36 }, MountainIndices(12), new[] { 0, 1, 2 },
            new List<int>());

        Assert.AreNotEqual(a, b, "顶点数量变化 ⇒ 签名改变（索引偏移随之变化）");
    }

    // ── 工具 ─────────────────────────────────────────────────────

    private MountainTopologySignature BuildSignature(IReadOnlyList<HexCellData> cells, int[] mountainIndices = null)
    {
        if (mountainIndices == null) mountainIndices = MountainIndices(12);
        var visible = new List<int>();
        foreach (HexCellData cell in cells)
        {
            if (cell != null && MountainGeometryBuilder.HasVisibleMountain(cell))
                visible.Add(cell.GenerateOrder);
        }
        return MountainTopologySignature.Build(
            hasMountain: true,
            totalVertexCount: 200,
            mountainIndexCount: mountainIndices.Length,
            subMeshIndexCounts: new[] { 30, 30, 30, 10, 12, mountainIndices.Length },
            mountainIndices: mountainIndices,
            collisionIndices: new[] { 0, 1, 2 },
            visibleMountainCellOrders: visible);
    }

    private static int[] MountainIndices(int count, int shift = 0)
    {
        var indices = new int[count];
        for (int i = 0; i < count; i++) indices[i] = 200 + i + shift;
        return indices;
    }

    private static HexCellData Cell(int generateOrder)
    {
        return new HexCellData(
            Enums.HexType.NoRiver, generateOrder,
            new Vector3(generateOrder, -generateOrder, 0f),
            new Vector3(generateOrder * 5f, 0f, 0f),
            2f);
    }

    private HexCellData CreateMountainCell(float distance, float position, int generateOrder)
    {
        HexCellData cell = Cell(generateOrder);
        cell.landForm = MountainForm;
        cell.mountainRidge = new MountainRidgeData
        {
            ridgeId = 3,
            seed = 123456,
            length = 8,
            widthRadius = 1.5f,
            gamma = 1.2f,
            hMax = 2f,
            ridgeNoiseAmplitude = 0.4f,
            cellNoiseScale = 0.3f,
            minVisibleHeight = 0.15f,
            maxSlope = 4f,
        };
        cell.mountainRidgeStatus = distance <= 0f
            ? Enums.MountainRidgeStatus.RidgeCell
            : Enums.MountainRidgeStatus.SlopeCell;
        cell.mountainDistToRidge = distance;
        cell.mountainPosAlongRidge = position;
        return cell;
    }

    // ── 几何夹具（与 MountainGeometryTests 相同的 A/B/C 三山格网格）──

    private const float TestOuterRadius = 3f;
    private static readonly float TestInnerRadius = TestOuterRadius * 0.8660254f;
    private const float SolidAreaRatio = 0.7f;

    private static readonly Vector3[] TestDeltas =
    {
        new Vector3(0, -1, 1), new Vector3(1, -1, 0), new Vector3(1, 0, -1),
        new Vector3(0, 1, -1), new Vector3(-1, 1, 0), new Vector3(-1, 0, 1),
    };

    private HexCellData _cellA;
    private HexCellData _cellB;
    private HexCellData _cellC;
    private HexCellData _cellD;
    private Dictionary<Vector3, HexCellData> _byHex;
    private System.Func<HexCellData, Enums.HexDirection, HexCellData> _neighborOf;

    private void BuildMountainFixture()
    {
        _byHex = new Dictionary<Vector3, HexCellData>();
        _cellA = CreateFixtureCell(new Vector3(0, 0, 0), 1, Enums.MountainRidgeStatus.RidgeCell, 0f, 1f,
            Enums.HexDirection.NE, Enums.HexDirection.E);
        _cellB = CreateFixtureCell(new Vector3(0, -1, 1), 2, Enums.MountainRidgeStatus.RidgeCell, 0f, 2f,
            Enums.HexDirection.SW, Enums.HexDirection.None);
        _cellC = CreateFixtureCell(new Vector3(1, -1, 0), 3, Enums.MountainRidgeStatus.SlopeCell, 0.5f, 1.5f,
            Enums.HexDirection.None, Enums.HexDirection.None);
        _cellD = CreateFixtureCell(new Vector3(1, 0, -1), 4, Enums.MountainRidgeStatus.None, 0f, 0f,
            Enums.HexDirection.None, Enums.HexDirection.None);
        _neighborOf = (c, d) =>
        {
            if (c == null || d == Enums.HexDirection.None) return null;
            _byHex.TryGetValue(c.HexCoordinate + TestDeltas[(int)d], out HexCellData cell);
            return cell;
        };
    }

    private HexCellData CreateFixtureCell(Vector3 hex, int order, Enums.MountainRidgeStatus status,
        float dist, float pos, Enums.HexDirection dirA, Enums.HexDirection dirB)
    {
        float wx = hex.x * 2f * TestInnerRadius + hex.z * TestInnerRadius;
        float wz = hex.z * 1.5f * TestOuterRadius;
        // 几何/动画夹具描述的是陆地山格；Height=0 会被 WaterLevel=1 判成水格。
        var cell = new HexCellData(Enums.HexType.NoRiver, order, hex, new Vector3(wx, 0f, wz), 2f);
        _byHex[hex] = cell;
        if (status == Enums.MountainRidgeStatus.None) return cell;

        bool isA = hex == new Vector3(0, 0, 0);
        bool isB = hex == new Vector3(0, -1, 1);
        cell.landForm = MountainForm;
        cell.mountainRidgeStatus = status;
        cell.mountainDistToRidge = dist;
        cell.mountainPosAlongRidge = pos;
        cell.RidgeDirectionA = dirA;
        cell.RidgeDirectionB = dirB;
        cell.mountainRidge = new MountainRidgeData
        {
            ridgeId = isA ? 1 : (isB ? 2 : 3),
            seed = 777,
            length = 8,
            widthRadius = 1.5f,
            gamma = 1f,
            hMax = isA ? 10f / 3f : (isB ? 8f / 3f : 2.5f),
            ridgeNoiseAmplitude = 0f,
            cellNoiseScale = 0f,
            minVisibleHeight = 0.15f,
            maxSlope = 4f,
            peakEccentricMin = 0.05f * TestInnerRadius,
            peakEccentricMax = 0.2f * TestInnerRadius,
        };
        return cell;
    }

    private static Vector3[] CreateSolid(Vector3 center)
    {
        float i = TestInnerRadius * SolidAreaRatio;
        var solid = new Vector3[44];
        solid[0] = center;
        solid[1] = center + new Vector3(0f, 0f, TestOuterRadius * SolidAreaRatio);
        solid[2] = center + new Vector3(i, 0f, 0.5f * TestOuterRadius * SolidAreaRatio);
        solid[3] = center + new Vector3(i, 0f, -0.5f * TestOuterRadius * SolidAreaRatio);
        solid[4] = center + new Vector3(0f, 0f, -TestOuterRadius * SolidAreaRatio);
        solid[5] = center + new Vector3(-i, 0f, -0.5f * TestOuterRadius * SolidAreaRatio);
        solid[6] = center + new Vector3(-i, 0f, 0.5f * TestOuterRadius * SolidAreaRatio);
        int[] angles = { 7, 5, 1, -1, -5, -7, -11, -13, -17, 17, 13, 11 };
        for (int k = 0; k < 12; k++)
        {
            float rad = Mathf.PI * angles[k] / 18f;
            solid[7 + k] = center + new Vector3(i * Mathf.Cos(rad), 0f, i * Mathf.Sin(rad));
        }
        return solid;
    }
}
