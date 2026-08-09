using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>阶段 3.1：确定性 hash、高度场、可见山体判定。</summary>
public class MountainGeometryTests
{
    private MapLandFormSO _mountainForm;

    [SetUp]
    public void SetUp()
    {
        WaterLevelConfig.WaterLevel = 1f;
        WaterLevelConfig.MaxHeight = 5f;
        _mountainForm = ScriptableObject.CreateInstance<MapLandFormSO>();
        _mountainForm.mountainForm = true;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_mountainForm);
    }

    [Test]
    public void Hash_SameKeysAreStable_AndNormalizedKeysIgnoreOrder()
    {
        Assert.AreEqual(MountainHash.Hash(17, 4, 9, -2), MountainHash.Hash(17, 4, 9, -2));
        Assert.AreEqual(MountainHash.EdgeKey(8, 3, 17), MountainHash.EdgeKey(3, 8, 17));
        Assert.AreEqual(MountainHash.CornerKey(8, 3, 5, 17), MountainHash.CornerKey(5, 8, 3, 17));
        Assert.AreNotEqual(MountainHash.PeakKey(3, 17), MountainHash.PeakKey(4, 17));

        float value01 = MountainHash.Hash01(17, 1, 2, 3);
        float signed = MountainHash.HashSigned(17, 1, 2, 3);
        Assert.That(value01, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
        Assert.That(signed, Is.GreaterThanOrEqualTo(-1f).And.LessThan(1f));
    }

    [Test]
    public void ComputeMountainHeight_SameSnapshotAndCellAlwaysReturnsSameValue()
    {
        HexCellData cell = CreateMountainCell(distance: 0f, position: 2.35f);

        float first = MountainGeometryBuilder.ComputeMountainHeight(cell);
        float second = MountainGeometryBuilder.ComputeMountainHeight(cell);

        Assert.AreEqual(first, second);
        Assert.That(first, Is.InRange(0f, cell.mountainRidge.hMax));
    }

    [Test]
    public void ComputeMountainHeight_WidthBoundaryIsZero_AndRidgeUsesFullAttenuation()
    {
        HexCellData ridgeCell = CreateMountainCell(distance: 0f, position: 1f);
        ridgeCell.mountainRidge.cellNoiseScale = 0f;
        HexCellData edgeCell = CreateMountainCell(distance: ridgeCell.mountainRidge.widthRadius, position: 1f);
        edgeCell.mountainRidge.cellNoiseScale = 0f;

        float ridgeNoise = MountainGeometryBuilder.SampleRidgeNoise(ridgeCell.mountainRidge, 1f);
        float expectedRidgeHeight = ridgeCell.mountainRidge.hMax
            * (0.6f + ridgeCell.mountainRidge.ridgeNoiseAmplitude * ridgeNoise);

        Assert.AreEqual(expectedRidgeHeight, MountainGeometryBuilder.ComputeMountainHeight(ridgeCell), 1e-5f);
        Assert.AreEqual(0f, MountainGeometryBuilder.ComputeMountainHeight(edgeCell), 1e-6f);
    }

    [Test]
    public void ComputeMountainHeight_ClampsExtremeNoiseToValidRange()
    {
        HexCellData cell = CreateMountainCell(distance: 0.2f, position: 3.5f);
        cell.mountainRidge.cellNoiseScale = 100f;

        float height = MountainGeometryBuilder.ComputeMountainHeight(cell);

        Assert.That(height, Is.InRange(0f, cell.mountainRidge.hMax));
    }

    [Test]
    public void SampleRidgeNoise_IsContinuousAtLatticeBoundary()
    {
        MountainRidgeData ridge = CreateRidge();

        float left = MountainGeometryBuilder.SampleRidgeNoise(ridge, 1f - 0.0001f);
        float at = MountainGeometryBuilder.SampleRidgeNoise(ridge, 1f);
        float right = MountainGeometryBuilder.SampleRidgeNoise(ridge, 1f + 0.0001f);

        Assert.AreEqual(at, left, 1e-4f);
        Assert.AreEqual(at, right, 1e-4f);
    }

    [Test]
    public void HasVisibleMountain_RequiresEffectiveCellAndMinimumHeight()
    {
        HexCellData visible = CreateMountainCell(distance: 0f, position: 0f);
        visible.mountainRidge.minVisibleHeight = 0.01f;
        Assert.IsTrue(MountainGeometryBuilder.HasVisibleMountain(visible));

        visible.mountainRidge.minVisibleHeight = visible.mountainRidge.hMax + 1f;
        Assert.IsFalse(MountainGeometryBuilder.HasVisibleMountain(visible));

        visible.mountainRidge.minVisibleHeight = 0.01f;
        visible.mountainCleared = true;
        Assert.IsFalse(MountainGeometryBuilder.HasVisibleMountain(visible));

        visible.mountainCleared = false;
        visible.Height = 0.5f;
        Assert.IsFalse(MountainGeometryBuilder.HasVisibleMountain(visible));
    }

    [Test]
    public void DefaultField_AdjacentDistancesStayWithinSnapshotMaxSlope()
    {
        MountainRidgeData ridge = CreateRidge();
        ridge.cellNoiseScale = 0.3f;
        float previous = MountainGeometryBuilder.ComputeMountainHeight(CreateMountainCell(0f, 2f, ridge, 10));

        for (int i = 1; i <= 6; i++)
        {
            float distance = ridge.widthRadius * i / 6f;
            float current = MountainGeometryBuilder.ComputeMountainHeight(
                CreateMountainCell(distance, 2f, ridge, 10 + i));
            Assert.LessOrEqual(Mathf.Abs(previous - current), ridge.maxSlope + 1e-5f);
            previous = current;
        }
    }

    private HexCellData CreateMountainCell(float distance, float position,
        MountainRidgeData ridge = null, int generateOrder = 7)
    {
        var cell = new HexCellData(
            Enums.HexType.NoRiver,
            generateOrder,
            new Vector3(generateOrder, -generateOrder, 0f),
            new Vector3(generateOrder * 5f, 0f, 0f),
            2f);
        cell.landForm = _mountainForm;
        cell.mountainRidge = ridge ?? CreateRidge();
        cell.mountainRidgeStatus = distance <= 0f
            ? Enums.MountainRidgeStatus.RidgeCell
            : Enums.MountainRidgeStatus.SlopeCell;
        cell.mountainDistToRidge = distance;
        cell.mountainPosAlongRidge = position;
        return cell;
    }

    private static MountainRidgeData CreateRidge()
    {
        return new MountainRidgeData
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
    }

    // ── 阶段 3.3~3.8：几何构造与诊断 ─────────────────────────────
    // 网格：A(0,0,0) 与 B=A.NE、C=A.E 为可见山（ridgeNoiseAmplitude/cellNoiseScale=0，
    // γ=1 ⇒ h = hMax×0.6×t），其余格与越界邻居均为空（视为非山）。
    // 夹具基础地形 Y=2 ⇒ 最终顶点 = 2 + 隆起：hA=2.0（hMax=10/3）、hB=1.6（hMax=8/3）、hC=1.0（hMax=2.5, d=0.5）。
    // 3 山格交汇角点 = avg(hA,hB,hC) = 4.6/3 ≈ 1.5333（2026-08-06 角点规则 max→均值修订）。
    // BuildSolidMountain flat 拆分输出 54 顶点，非环序索引；顶点布局 = [peak, ring[j], ring[j+1]] × 18。
    // 环点扇位 k（RingFanOrder 位置）出现在：三角 t=(k+17)%18 的顶点 2 与三角 t=k%18 的顶点 1。

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
            Vector3 target = c.HexCoordinate + TestDeltas[(int)d];
            _byHex.TryGetValue(target, out HexCellData cell);
            return cell;
        };
    }

    private HexCellData CreateFixtureCell(Vector3 hex, int order, Enums.MountainRidgeStatus status,
        float dist, float pos, Enums.HexDirection dirA, Enums.HexDirection dirB)
    {
        float wx = hex.x * 2f * TestInnerRadius + hex.z * TestInnerRadius;
        float wz = hex.z * 1.5f * TestOuterRadius;
        // 几何夹具描述的是陆地山格：基础地形 Y = 2（CenterWorldCoordinate.y 与 Height 同为 2），
        // Height=0 会被 WaterLevel=1 判成水格，令全部山体贡献归零。
        var cell = new HexCellData(Enums.HexType.NoRiver, order, hex, new Vector3(wx, 2f, wz), 2f);
        _byHex[hex] = cell;
        if (status == Enums.MountainRidgeStatus.None) return cell;

        bool isA = hex == new Vector3(0, 0, 0);
        bool isB = hex == new Vector3(0, -1, 1);
        cell.landForm = _mountainForm;
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

    private static void AssertRingVertexY(CellGeometry geometry, int fanIndex, float expected, string label)
    {
        int firstTriangle = (fanIndex + 17) % 18;
        int secondTriangle = fanIndex % 18;
        Assert.AreEqual(expected, geometry.Vertices[firstTriangle * 3 + 2].y, 1e-4f, $"{label}：首面顶点");
        Assert.AreEqual(expected, geometry.Vertices[secondTriangle * 3 + 1].y, 1e-4f, $"{label}：次面顶点（扇位 {fanIndex}）");
    }

    [Test]
    public void SolidMountain_SharedJunctionCornerHeights_AgreeAcrossCells()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);
        CellGeometry geoA = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);
        CellGeometry geoB = MountainGeometryBuilder.BuildSolidMountain(_cellB, solidB, _neighborOf);

        // A 角点 2（扇位 3）与 B 角点 4（扇位 9）= 同一交汇 {A,B,C}，均为 avg(2,1.6,1)=4.6/3
        float hA = MountainGeometryBuilder.CornerLift(_cellA, 2, _neighborOf);
        float hB = MountainGeometryBuilder.CornerLift(_cellB, 4, _neighborOf);
        Assert.AreEqual(4.6f / 3f, hA, 1e-4f);
        Assert.AreEqual(hA, hB, 1e-4f);
        AssertRingVertexY(geoA, 3, 2f + 4.6f / 3f, "A 环角点 2 世界高");
        float aVertex = geoA.Vertices[2 * 3 + 2].y;
        float bVertex = geoB.Vertices[8 * 3 + 2].y;
        Assert.AreEqual(aVertex, bVertex, 1e-4f, "共享交汇两侧环点一致");
    }

    [Test]
    public void CornerHeight_ThreeVisibleMountains_ReturnsMean_PeakStandsAboveRim()
    {
        // 2026-08-06 角点规则修订（max → 均值）：max 会把覆盖区内部每个交汇角点抬到局部最大山高，
        // 扇面主峰（= 本格山高）恒 ≤ 环角点 ⇒ 任何格都无法形成局部高点，整条山脉被抹平成平顶
        // 台地（平顶峰 bug，场景截图验收发现）。均值保留高度场梯度：脊线格主峰成脊、坡面格成坡。
        BuildMountainFixture();
        float hA = MountainGeometryBuilder.ComputeMountainHeight(_cellA);
        float hB = MountainGeometryBuilder.ComputeMountainHeight(_cellB);
        float hC = MountainGeometryBuilder.ComputeMountainHeight(_cellC);
        float corner = MountainGeometryBuilder.CornerLift(_cellA, 2, _neighborOf);

        Assert.AreEqual((hA + hB + hC) / 3f, corner, 1e-5f, "3 山格交汇角点 = 三格山高均值");
        Assert.Less(corner, Mathf.Max(hA, Mathf.Max(hB, hC)), "交汇角点必须低于局部最大山高（否则抹平成平顶）");
        Assert.Greater(hA, corner, "夹具中 A 为局部最高脊线格：主峰高于交汇角点 ⇒ 成脊");

        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        CellGeometry geoA = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);
        float peakY = geoA.Vertices[0].y;
        Assert.Greater(peakY, 2f + corner, "扇面主峰世界高必须高于 {A,B,C} 交汇环角点（防平顶峰回归）");
    }

    [Test]
    public void CornerHeight_RidgeConsecutivePair_ReturnsCrestPairMean()
    {
        // 2026-08-06 脊线连续修订（续20）：同一脊线的相邻脊线格（|Δs|=1）参与的交汇角点
        // 位于脊脊线上，取相邻对两格山高均值，不再压入坡面格 ⇒ 相邻主峰由连续山脊连接，
        // 不再锯齿成"每格一个尖峰"（场景截图验收反馈）。
        BuildMountainFixture();
        _cellB.mountainRidge.ridgeId = 1; // B 并入 A 的脊线（A pos=1、B pos=2 ⇒ 脊线相邻对）
        float hA = MountainGeometryBuilder.ComputeMountainHeight(_cellA);
        float hB = MountainGeometryBuilder.ComputeMountainHeight(_cellB);
        float hC = MountainGeometryBuilder.ComputeMountainHeight(_cellC);
        float corner = MountainGeometryBuilder.CornerLift(_cellA, 2, _neighborOf);

        Assert.IsTrue(MountainGeometryBuilder.IsRidgeConsecutive(_cellA, _cellB), "A(pos1)/B(pos2) 同脊线相邻");
        Assert.IsFalse(MountainGeometryBuilder.IsRidgeConsecutive(_cellA, _cellC), "坡面格不参与脊线相邻对");
        Assert.AreEqual((hA + hB) * 0.5f, corner, 1e-5f, "脊线相邻对交汇角点 = 相邻对两格山高均值");
        Assert.Greater(corner, (hA + hB + hC) / 3f, "脊线角点高于旧三格均值（谷底被抬起，锯齿消除）");
        Assert.Less(corner, Mathf.Max(hA, hB), "最高脊线格主峰仍高于脊线角点（防平顶回归）");
        Assert.GreaterOrEqual(corner, Mathf.Min(hA, hB), "角点介于两峰之间：垭口式单调缓降、无过冲");

        // 脊线公共边带：连续脊线对的两个边点直接 = crest，不再依赖两端第三格是否为山；
        // 这是无宽度化直脊线仍能由 rect 连成山脊的核心契约。
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);
        CellGeometry geoA = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);
        CellGeometry geoB = MountainGeometryBuilder.BuildSolidMountain(_cellB, solidB, _neighborOf);
        AssertRingVertexY(geoA, 1, 2f + corner, "A 边点 7（脊线边带）");
        AssertRingVertexY(geoA, 2, 2f + corner, "A 边点 8（脊线边带）");
        float a8 = geoA.Vertices[1 * 3 + 2].y;
        float b13 = geoB.Vertices[11 * 3 + 2].y;
        Assert.AreEqual(a8, b13, 1e-4f, "脊线边带镜像点 B13 一致（规范化共享边，决策 ㉓）");
    }

    [Test]
    public void RidgeConsecutiveEdge_NoFlankMountains_RectStillFormsCrestBridge()
    {
        // 调试直脊线严格只占 n 格：A/B 是连续脊线格，但公共边两侧第三格均为普通/越界。
        // 两端 corner 必须保持 0（不扩张到侧邻格），两个内部 edge points 必须抬到 pair mean，
        // mountain-mountain rect 因而形成中央高、两端落地的窄脊桥。
        BuildMountainFixture();
        _cellB.mountainRidge.ridgeId = _cellA.mountainRidge.ridgeId;
        _cellC.landForm = null;
        _cellC.mountainRidgeStatus = Enums.MountainRidgeStatus.None;
        _cellC.mountainRidge = null;

        float hA = MountainGeometryBuilder.ComputeMountainHeight(_cellA);
        float hB = MountainGeometryBuilder.ComputeMountainHeight(_cellB);
        float crest = (hA + hB) * 0.5f;
        Assert.AreEqual(0f, MountainGeometryBuilder.CornerLift(_cellA, 1, _neighborOf), 1e-6f,
            "公共边第一端角点含越界格 ⇒ 0");
        Assert.AreEqual(0f, MountainGeometryBuilder.CornerLift(_cellA, 2, _neighborOf), 1e-6f,
            "公共边第二端角点含普通格 ⇒ 0");
        Assert.AreEqual(crest, MountainGeometryBuilder.EdgePointLift(_cellA, 7, _neighborOf), 1e-5f);
        Assert.AreEqual(crest, MountainGeometryBuilder.EdgePointLift(_cellA, 8, _neighborOf), 1e-5f);

        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);
        MountainRectBuild build = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellB, solidA, solidB, Enums.HexDirection.NE, _neighborOf);
        Assert.IsNull(build.PlainRect, "A/B 均为山格：公共 rect 整面进山体槽");
        for (int p = 0; p < 4; p++)
        {
            float expectedLift = p == 1 || p == 2 ? crest : 0f;
            Assert.AreEqual(2f + expectedLift, build.Rect.Profiles[p].Points[0].y, 1e-4f,
                $"A 侧 profile {p}");
            Assert.AreEqual(2f + expectedLift, build.Rect.Profiles[p].Points[1].y, 1e-4f,
                $"B 侧 profile {p}");
        }
        CellGeometry render = MountainGeometryBuilder.RectToRender(build, _cellA, _cellB);
        Assert.AreEqual(0, MountainGeometryBuilder.CountDegenerateTriangles(render.Vertices, render.Indices));
    }

    [Test]
    public void CornerHeight_RidgeBendJunction_TakesMaxPairMean()
    {
        // 拐弯交汇（3 格全为同一脊线脊线格）：C(pos0)—A(pos1)—B(pos2) 存在两对脊线相邻对，
        // 角点取相邻对均值的最大值 ⇒ 山脊绕弯处脊线高度不降。
        BuildMountainFixture();
        _cellB.mountainRidge.ridgeId = 1;
        _cellC.mountainRidgeStatus = Enums.MountainRidgeStatus.RidgeCell;
        _cellC.mountainRidge.ridgeId = 1;
        _cellC.mountainPosAlongRidge = 0f;
        float hA = MountainGeometryBuilder.ComputeMountainHeight(_cellA);
        float hB = MountainGeometryBuilder.ComputeMountainHeight(_cellB);
        float hC = MountainGeometryBuilder.ComputeMountainHeight(_cellC);
        float corner = MountainGeometryBuilder.CornerLift(_cellA, 2, _neighborOf);

        Assert.IsTrue(MountainGeometryBuilder.IsRidgeConsecutive(_cellC, _cellA));
        Assert.IsTrue(MountainGeometryBuilder.IsRidgeConsecutive(_cellA, _cellB));
        Assert.IsFalse(MountainGeometryBuilder.IsRidgeConsecutive(_cellC, _cellB), "pos0/pos2 路径不相邻");
        float expected = Mathf.Max((hA + hB) * 0.5f, (hA + hC) * 0.5f);
        Assert.AreEqual(expected, corner, 1e-5f, "拐弯交汇角点 = 相邻对均值的最大值");
    }

    [Test]
    public void CornerHeight_NonConsecutiveSameRidge_FallsBackToMean()
    {
        // 急转弯自触：同一脊线但路径上不相邻（|Δs| ≥ 2）⇒ 不架脊桥，回落三格均值。
        BuildMountainFixture();
        _cellB.mountainRidge.ridgeId = 1;
        _cellB.mountainPosAlongRidge = 3f; // A(pos1) 与 B(pos3) 路径不相邻
        _cellC.mountainRidgeStatus = Enums.MountainRidgeStatus.RidgeCell;
        _cellC.mountainRidge.ridgeId = 1;
        _cellC.mountainPosAlongRidge = 5f;
        float hA = MountainGeometryBuilder.ComputeMountainHeight(_cellA);
        float hB = MountainGeometryBuilder.ComputeMountainHeight(_cellB);
        float hC = MountainGeometryBuilder.ComputeMountainHeight(_cellC);
        float corner = MountainGeometryBuilder.CornerLift(_cellA, 2, _neighborOf);

        Assert.IsFalse(MountainGeometryBuilder.IsRidgeConsecutive(_cellA, _cellB));
        Assert.IsFalse(MountainGeometryBuilder.IsRidgeConsecutive(_cellA, _cellC));
        Assert.IsFalse(MountainGeometryBuilder.IsRidgeConsecutive(_cellB, _cellC));
        Assert.AreEqual((hA + hB + hC) / 3f, corner, 1e-5f, "非相邻脊线格交汇回落三格均值");
    }

    [Test]
    public void SolidMountain_MirrorEdgePoints_AgreeAcrossCells()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);
        CellGeometry geoA = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);
        CellGeometry geoB = MountainGeometryBuilder.BuildSolidMountain(_cellB, solidB, _neighborOf);

        // A 点7(u=1/3) ↔ B 点14(镜像 u=2/3) = lerp(0, 4.6/3, 1/3)=4.6/9；A 点8 ↔ B 点13 = 9.2/9
        // （两端角点：{A,B,越界}=0 与 {A,B,C}=avg(2,1.6,1)=4.6/3）
        AssertRingVertexY(geoA, 1, 2f + 4.6f / 9f, "A 边点 7");
        float a7 = geoA.Vertices[0 * 3 + 2].y;
        float b14 = geoB.Vertices[10 * 3 + 2].y;
        Assert.AreEqual(a7, b14, 1e-4f, "B 边点 14 镜像一致");
        AssertRingVertexY(geoA, 2, 2f + 9.2f / 9f, "A 边点 8");
        float a8 = geoA.Vertices[1 * 3 + 2].y;
        float b13 = geoB.Vertices[11 * 3 + 2].y;
        Assert.AreEqual(a8, b13, 1e-4f, "B 边点 13 镜像一致");
    }

    [Test]
    public void SolidMountain_MountainPlainEdge_UsesHalfHeight()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        CellGeometry geoA = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);

        // 决策 ⑲：山-非山边环点 = hA × 0.5 = 1.0（SE/SW/W/NW 四条边，扇位 7,8,10,11,13,14,16,17）
        float hA = MountainGeometryBuilder.ComputeMountainHeight(_cellA);
        foreach (int fanIndex in new[] { 7, 8, 10, 11, 13, 14, 16, 17 })
            AssertRingVertexY(geoA, fanIndex, 2f + hA * 0.5f, $"边点扇位 {fanIndex}");
    }

    [Test]
    public void SolidMountain_PeakEccentricityWithinBounds_AndPeakHeight()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        CellGeometry geoA = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);

        // 每面首顶点 = 主峰（偏心量 ∈ [peakEccentricMin, peakEccentricMax]）
        Vector3 peak = geoA.Vertices[0];
        float offset = Mathf.Sqrt((peak.x - solidA[0].x) * (peak.x - solidA[0].x)
            + (peak.z - solidA[0].z) * (peak.z - solidA[0].z));
        Assert.GreaterOrEqual(offset, _cellA.mountainRidge.peakEccentricMin - 1e-4f);
        Assert.LessOrEqual(offset, _cellA.mountainRidge.peakEccentricMax + 1e-4f);
        float hA = MountainGeometryBuilder.ComputeMountainHeight(_cellA);
        Assert.AreEqual(2f + hA, peak.y, 1e-4f, "主峰世界高 = 基础地形 Y(2) + hA");
    }

    [Test]
    public void SolidMountain_Budget54_NoDegenerateTriangles()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        CellGeometry geoA = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);

        Assert.AreEqual(54, geoA.Vertices.Length, "18 面 × 3 = 54 顶点预算");
        Assert.AreEqual(54, geoA.UVs.Length);
        Assert.AreEqual(0, MountainGeometryBuilder.CountDegenerateTriangles(geoA.Vertices, geoA.Indices), "无退化三角");
        for (int i = 0; i < geoA.Indices.Length; i++)
            Assert.Less(geoA.Indices[i], 54, "索引不越界");
        Assert.AreEqual(geoA.Vertices.Length, geoA.Weights.Count, "权重与顶点一一对应");
        // faceTier 三段 UV0.y ∈ {(0.5,1.5,2.5)/3}
        foreach (Vector2 uv in geoA.UVs)
            Assert.AreEqual((Mathf.FloorToInt(uv.y * 3f - 0.5f) + 0.5f) / 3f, uv.y, 1e-5f, "离散色阶 UV");
    }

    [Test]
    public void SolidMountain_Uv0EncodesMaterialContract()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        CellGeometry geoA = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);

        // 阶段 4.1/4.4 契约：几何 UV0 必须与 MountainMaterialContract 编码一致，且同面三顶点同 tier
        AssertUv0Contract(geoA);
    }

    [Test]
    public void MountainRectsAndTriangles_Uv0EncodesMaterialContract()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);
        Vector3[] solidC = CreateSolid(_cellC.CenterWorldCoordinate);

        MountainRectBuild build = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellB, solidA, solidB, Enums.HexDirection.NE, _neighborOf);
        CellGeometry rect = MountainGeometryBuilder.RectToRender(build, _cellA, _cellB);
        AssertUv0Contract(rect);

        var rects = new Dictionary<(int, Enums.HexDirection), RectangleTransitionMeshData>();
        rects[(_cellA.GenerateOrder, Enums.HexDirection.NE)] = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellB, solidA, solidB, Enums.HexDirection.NE, _neighborOf).Rect;
        rects[(_cellB.GenerateOrder, Enums.HexDirection.SE)] = MountainGeometryBuilder.BuildMountainRectData(
            _cellB, _cellC, solidB, solidC, Enums.HexDirection.SE, _neighborOf).Rect;
        rects[(_cellA.GenerateOrder, Enums.HexDirection.E)] = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellC, solidA, solidC, Enums.HexDirection.E, _neighborOf).Rect;
        CellGeometry tri = MountainGeometryBuilder.BuildTriangleMountain(
            _cellA, _neighborOf, (c, d) => rects[(c.GenerateOrder, d)], Enums.HexDirection.NE, Enums.HexDirection.E);
        AssertUv0Contract(tri);
    }

    /// <summary>阶段 4.4 契约：UV0.y 解码仅得 0/1/2 且落在编码档位；UV0.x = ridgeKey01 ∈ [0,1)；同面三顶点同 tier。</summary>
    private static void AssertUv0Contract(CellGeometry geometry)
    {
        foreach (Vector2 uv in geometry.UVs)
        {
            int tier = MountainMaterialContract.DecodeFaceTier(uv.y);
            Assert.AreEqual(MountainMaterialContract.EncodeFaceTier(tier), uv.y, 1e-5f, "UV0.y 必须落在契约档位");
            Assert.That(uv.x, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f), "UV0.x = ridgeKey01 ∈ [0,1)");
        }
        for (int t = 0; t + 2 < geometry.Indices.Length; t += 3)
        {
            float y0 = geometry.UVs[geometry.Indices[t]].y;
            Assert.AreEqual(y0, geometry.UVs[geometry.Indices[t + 1]].y, 1e-6f, "同面三顶点同 tier");
            Assert.AreEqual(y0, geometry.UVs[geometry.Indices[t + 2]].y, 1e-6f, "同面三顶点同 tier");
        }
    }

    /// <summary>按 RingFanOrder 扇位读取 flat 拆分后环点高度（每扇位出现两次，断言其一即可）。</summary>
    private static float GetRingY(CellGeometry geometry, int fanIndex)
    {
        return geometry.Vertices[(fanIndex % 18) * 3 + 1].y;
    }

    [Test]
    public void MountainRect_MountainMountain_ProfilesMatchSolidRings()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);
        CellGeometry geoA = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);
        CellGeometry geoB = MountainGeometryBuilder.BuildSolidMountain(_cellB, solidB, _neighborOf);

        MountainRectBuild build = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellB, solidA, solidB, Enums.HexDirection.NE, _neighborOf);
        RectangleTransitionMeshData rect = build.Rect;
        Assert.AreEqual(4, rect.Profiles.Count);
        Assert.AreEqual(2, rect.Profiles[0].Points.Count, "山-山 profile 长度 2（Slope 直纹面）");

        // profile 端点与两侧 solid 环点逐点一致（角点规则 / 边点规则；flat 拆分后按扇位取值）
        Assert.AreEqual(GetRingY(geoA, 0), rect.Profiles[0].Points[0].y, 1e-4f, "A 角点 1");
        Assert.AreEqual(GetRingY(geoA, 1), rect.Profiles[1].Points[0].y, 1e-4f, "A 边点 7");
        Assert.AreEqual(GetRingY(geoA, 2), rect.Profiles[2].Points[0].y, 1e-4f, "A 边点 8");
        Assert.AreEqual(GetRingY(geoA, 3), rect.Profiles[3].Points[0].y, 1e-4f, "A 角点 2");
        Assert.AreEqual(GetRingY(geoB, 12), rect.Profiles[0].Points[1].y, 1e-4f, "B 角点 5");
        Assert.AreEqual(GetRingY(geoB, 11), rect.Profiles[1].Points[1].y, 1e-4f, "B 边点 14");
        Assert.AreEqual(GetRingY(geoB, 10), rect.Profiles[2].Points[1].y, 1e-4f, "B 边点 13");
        Assert.AreEqual(GetRingY(geoB, 9), rect.Profiles[3].Points[1].y, 1e-4f, "B 角点 4");

        CellGeometry render = MountainGeometryBuilder.RectToRender(build, _cellA, _cellB);
        Assert.AreEqual(18, render.Vertices.Length, "6 三角 flat 拆分 = 18 顶点");
        Assert.AreEqual(0, MountainGeometryBuilder.CountDegenerateTriangles(render.Vertices, render.Indices));
    }

    [Test]
    public void MountainRect_MountainPlain_SplitAtBoundary_PlainHalfReturned()
    {
        // 2026-08-07 格界劈半（决策 ④ 细化）：山-普通 rect 在格界劈成两件——
        // 山侧半边（带坡度折面，进山体槽）+ 普通半边（恒 0 隆起，PlainRect 回地形槽），
        // 山体视觉边界收回到格界线；两件在格界点严格闭合。
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidD = CreateSolid(_cellD.CenterWorldCoordinate);

        MountainRectBuild build = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellD, solidA, solidD, Enums.HexDirection.SE, _neighborOf);
        RectangleTransitionMeshData rect = build.Rect;
        Assert.AreEqual(2, rect.Profiles[0].Points.Count, "劈半后山体件 profile 长度 2（山格环点 → 格界）");
        Assert.IsNotNull(build.PlainRect, "山-普通必须产出普通半边（回地形槽）");
        Assert.AreEqual(2, build.PlainRect.Profiles[0].Points.Count, "普通半边 profile 长度 2（格界 → 普通环点）");

        // 山侧半边：角 profile 起点全 0（交汇含普通格）、边 profile 起点 = hA×0.5；格界恒 0。
        // 普通半边：全 0 隆起。世界 Y = 基础地形 Y(2) + 隆起。
        float hA = MountainGeometryBuilder.ComputeMountainHeight(_cellA);
        for (int p = 0; p < 4; p++)
        {
            var mountainPoints = rect.Profiles[p].Points;
            var plainPoints = build.PlainRect.Profiles[p].Points;
            Assert.AreEqual(p == 0 || p == 3 ? 2f : 2f + hA * 0.5f, mountainPoints[0].y, 1e-4f,
                $"山侧起点 profile {p}");
            Assert.AreEqual(2f, mountainPoints[1].y, 1e-4f, $"格界锚点 profile {p}（决策 ④：隆起 0）");
            Assert.AreEqual(2f, plainPoints[1].y, 1e-4f, $"普通侧环点 profile {p}（恒 0 隆起）");
            Assert.AreEqual(mountainPoints[1], plainPoints[0], $"格界点两件严格闭合 profile {p}（决策 ㉛）");
        }

        // UV.y 区间：山侧半边 ∈ {0, 0.5}、普通半边 ∈ {0.5, 1}（与整面 rect 对应半段逐点一致，
        // 动画权重/材质混合坐标连续）
        foreach (Vector2 uv in rect.UVs)
            Assert.IsTrue(uv.y == 0f || Mathf.Abs(uv.y - 0.5f) < 1e-5f, "山侧半边 UV.y ∈ {0, 0.5}");
        foreach (Vector2 uv in build.PlainRect.UVs)
            Assert.IsTrue(Mathf.Abs(uv.y - 0.5f) < 1e-5f || Mathf.Abs(uv.y - 1f) < 1e-5f,
                "普通半边 UV.y ∈ {0.5, 1}");

        CellGeometry render = MountainGeometryBuilder.RectToRender(build, _cellA, _cellD);
        Assert.AreEqual(18, render.Vertices.Length, "山侧半边 6 三角 flat 拆分 = 18 顶点");
        Assert.AreEqual(0, MountainGeometryBuilder.CountDegenerateTriangles(render.Vertices, render.Indices));
        Assert.AreEqual(0,
            MountainGeometryBuilder.CountDegenerateTriangles(
                build.PlainRect.Vertices.ToArray(), build.PlainRect.Indices.ToArray()),
            "普通半边无退化三角");
    }

    [Test]
    public void MountainRect_NeighborMountain_SplitPicksNeighborHalf()
    {
        // 劈半取向：山格是 neighbor（owner 为普通格）时，山体件 = neighbor 半边（UV.y ∈ {0.5,1}）、
        // PlainRect = owner 半边（UV.y ∈ {0,0.5}）——rect 归属方向只有 {NE,E,SE}，两种取向都必须正确。
        BuildMountainFixture();
        Vector3[] solidC = CreateSolid(_cellC.CenterWorldCoordinate);
        Vector3[] solidD = CreateSolid(_cellD.CenterWorldCoordinate);

        // D（普通）的 NE 邻居 = C（坡面山格）⇒ owner 普通 / neighbor 山
        MountainRectBuild build = MountainGeometryBuilder.BuildMountainRectData(
            _cellD, _cellC, solidD, solidC, Enums.HexDirection.NE, _neighborOf);

        Assert.IsNotNull(build.PlainRect);
        float hC = MountainGeometryBuilder.ComputeMountainHeight(_cellC);
        int raisedProfiles = 0;
        for (int p = 0; p < 4; p++)
        {
            var mountainPoints = build.Rect.Profiles[p].Points;
            var plainPoints = build.PlainRect.Profiles[p].Points;
            Assert.AreEqual(2f, mountainPoints[0].y, 1e-4f, $"格界锚点 profile {p}（neighbor 半边起点 = 格界）");
            Assert.AreEqual(2f, plainPoints[0].y, 1e-4f, $"普通侧环点 profile {p}（owner 半边起点）");
            Assert.AreEqual(2f, plainPoints[1].y, 1e-4f, $"格界锚点 profile {p}（owner 半边终点）");
            Assert.AreEqual(mountainPoints[0], plainPoints[1], $"格界点两件严格闭合 profile {p}");
            if (mountainPoints[1].y > 2f) raisedProfiles++;
        }
        Assert.AreEqual(2, raisedProfiles, "neighbor 半边仅两条边 profile 隆起（hC×0.5），角 profile 恒 0");

        foreach (Vector2 uv in build.Rect.UVs)
            Assert.IsTrue(Mathf.Abs(uv.y - 0.5f) < 1e-5f || Mathf.Abs(uv.y - 1f) < 1e-5f,
                "neighbor 山侧半边 UV.y ∈ {0.5, 1}");
        foreach (Vector2 uv in build.PlainRect.UVs)
            Assert.IsTrue(uv.y == 0f || Mathf.Abs(uv.y - 0.5f) < 1e-5f, "owner 普通半边 UV.y ∈ {0, 0.5}");

        CellGeometry render = MountainGeometryBuilder.RectToRender(build, _cellD, _cellC);
        Assert.AreEqual(18, render.Vertices.Length, "neighbor 山侧半边 6 三角 flat 拆分 = 18 顶点");
        Assert.Greater(hC, 0f, "夹具坡面格高度必须为正（测试前提）");
    }

    [Test]
    public void MountainRect_TerrainBlendData_MountainSideOneBoundaryZero_AndUvPreserved()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidD = CreateSolid(_cellD.CenterWorldCoordinate);
        MountainRectBuild build = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellD, solidA, solidD, Enums.HexDirection.SE, _neighborOf);

        CellGeometry blend = MountainGeometryBuilder.RectToTerrainBlendRender(build, _cellA, _cellD);
        Assert.IsNotNull(blend.BlendData);
        Assert.AreEqual(blend.Vertices.Length, blend.BlendData.Length,
            "UV4 BlendData 必须与 flat 顶点逐一平行");
        for (int i = 0; i < build.Rect.Indices.Count; i++)
        {
            Vector2 rawUv = build.Rect.UVs[build.Rect.Indices[i]];
            Vector4 data = blend.BlendData[i];
            Assert.AreEqual(rawUv.x, data.x, 1e-5f, "terrain UV.x 保留");
            Assert.AreEqual(rawUv.y, data.y, 1e-5f, "terrain UV.y 保留");
            float expected = Mathf.Clamp01(1f - rawUv.y * 2f);
            Assert.AreEqual(expected, data.z, 1e-5f,
                "owner 为山：山侧环点 blend=1，格界 blend=0");
        }

        // 反向取向：owner 普通 / neighbor 山，权重必须随 UV.y 从格界 0 增至山侧 1。
        Vector3[] solidC = CreateSolid(_cellC.CenterWorldCoordinate);
        MountainRectBuild reverseBuild = MountainGeometryBuilder.BuildMountainRectData(
            _cellD, _cellC, solidD, solidC, Enums.HexDirection.NE, _neighborOf);
        CellGeometry reverse = MountainGeometryBuilder.RectToTerrainBlendRender(
            reverseBuild, _cellD, _cellC);
        for (int i = 0; i < reverseBuild.Rect.Indices.Count; i++)
        {
            Vector2 rawUv = reverseBuild.Rect.UVs[reverseBuild.Rect.Indices[i]];
            Assert.AreEqual(Mathf.Clamp01(rawUv.y * 2f - 1f), reverse.BlendData[i].z, 1e-5f,
                "neighbor 为山：格界 blend=0，山侧环点 blend=1");
        }
    }

    [Test]
    public void TriangleMountain_ThreeMountain_FlatAtCornerHeight_NoHole()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);
        Vector3[] solidC = CreateSolid(_cellC.CenterWorldCoordinate);
        var rects = new Dictionary<(int, Enums.HexDirection), RectangleTransitionMeshData>();
        var builds = new List<MountainRectBuild>
        {
            MountainGeometryBuilder.BuildMountainRectData(_cellA, _cellB, solidA, solidB, Enums.HexDirection.NE, _neighborOf),
            MountainGeometryBuilder.BuildMountainRectData(_cellB, _cellC, solidB, solidC, Enums.HexDirection.SE, _neighborOf),
            MountainGeometryBuilder.BuildMountainRectData(_cellA, _cellC, solidA, solidC, Enums.HexDirection.E, _neighborOf),
        };
        rects[(_cellA.GenerateOrder, Enums.HexDirection.NE)] = builds[0].Rect;
        rects[(_cellB.GenerateOrder, Enums.HexDirection.SE)] = builds[1].Rect;
        rects[(_cellA.GenerateOrder, Enums.HexDirection.E)] = builds[2].Rect;

        CellGeometry tri = MountainGeometryBuilder.BuildTriangleMountain(
            _cellA, _neighborOf, (c, d) => rects[(c.GenerateOrder, d)], Enums.HexDirection.NE, Enums.HexDirection.E);

        Assert.AreEqual(3, tri.Vertices.Length, "3 条直 profile ⇒ 单平坦三角（flat 拆分 3 顶点）");
        Assert.AreEqual(0, MountainGeometryBuilder.CountDegenerateTriangles(tri.Vertices, tri.Indices));
        foreach (Vector3 v in tri.Vertices)
            Assert.AreEqual(2f + 4.6f / 3f, v.y, 1e-4f, "3 山格交汇角点 = 基础地形 Y(2) + avg(2,1.6,1) ≈ 3.5333");
        foreach (HexCellData[] weights in tri.Weights)
            Assert.AreEqual(3, weights.Length, "tri 顶点权重 = 3 格");
    }

    [Test]
    public void RidgeEdgeTriangleShoulder_UsesConsecutiveEdge_ThirdCellRemainsPlain()
    {
        // A/B 是连续脊线格，C 是普通格；NEE tri 的 A-B 边生成视觉肩部，C 不成为山格。
        BuildMountainFixture();
        _cellB.mountainRidge.ridgeId = _cellA.mountainRidge.ridgeId;
        _cellC.landForm = null;
        _cellC.mountainRidge = null;
        _cellC.mountainRidgeStatus = Enums.MountainRidgeStatus.None;
        _cellC.movementCost = 1f;

        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);
        Vector3[] solidC = CreateSolid(_cellC.CenterWorldCoordinate);
        var solids = new Dictionary<int, Vector3[]>
        {
            [_cellA.GenerateOrder] = solidA,
            [_cellB.GenerateOrder] = solidB,
            [_cellC.GenerateOrder] = solidC,
        };
        var plainRects = new Dictionary<(int, Enums.HexDirection), RectangleTransitionMeshData>
        {
            [(_cellA.GenerateOrder, Enums.HexDirection.NE)] = BuildPlainRect(
                _cellA, _cellB, Enums.HexDirection.NE, solids),
            [(_cellB.GenerateOrder, Enums.HexDirection.SE)] = BuildPlainRect(
                _cellB, _cellC, Enums.HexDirection.SE, solids),
            [(_cellA.GenerateOrder, Enums.HexDirection.E)] = BuildPlainRect(
                _cellA, _cellC, Enums.HexDirection.E, solids),
        };
        TriangleTransitionMeshData plainTri = BuildPlainTri(
            _cellA, _cellB, _cellC,
            new[] { Enums.HexDirection.NE, Enums.HexDirection.E }, plainRects);
        MountainRectBuild ridgeRect = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellB, solidA, solidB, Enums.HexDirection.NE, _neighborOf);

        CellGeometry shoulder = MountainGeometryBuilder.BuildRidgeEdgeTriangleShoulder(
            _cellA, _cellB, _cellC, plainTri);

        Assert.IsNotNull(shoulder, "连续脊边 + 第三格普通必须生成 tri 肩部");
        Assert.AreEqual(9, shoulder.Vertices.Length, "肩部 = 3 个 flat 三角 = 9 顶点");
        Assert.AreEqual(0, MountainGeometryBuilder.CountDegenerateTriangles(
            shoulder.Vertices, shoulder.Indices));
        Assert.AreEqual(shoulder.Vertices.Length, shoulder.AnimSources.Count);
        Assert.IsTrue(MountainVertexAnimSource.IsValid(shoulder.AnimSources));
        Assert.IsNull(_cellC.landForm, "第三格仍为普通格，不写 landForm");
        Assert.AreEqual(Enums.MountainRidgeStatus.None, _cellC.mountainRidgeStatus);
        Assert.AreEqual(1f, _cellC.movementCost, "第三格玩法资格不变");

        Vector3 rectShoulder = ridgeRect.Rect.Profiles[3].Points[1];
        Vector3 shoulderPeak = shoulder.Vertices.OrderByDescending(v => v.y).First();
        Assert.AreEqual(rectShoulder, shoulderPeak,
            "tri 肩部高点必须逐点复用 A-B rect 相邻角 profile 中点（静态/动画闭合）");
        Assert.Greater(shoulderPeak.y, 2f, "肩部必须高于普通 tri");
        Assert.Less(shoulderPeak.y, 2f + Mathf.Max(
            MountainGeometryBuilder.ComputeMountainHeight(_cellA),
            MountainGeometryBuilder.ComputeMountainHeight(_cellB)),
            "肩部低于主峰，不能形成第二条脊峰");
    }

    [Test]
    public void RidgeEdgeTriangleShoulder_NonConsecutiveOrThirdMountain_ReturnsNull()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);
        Vector3[] solidC = CreateSolid(_cellC.CenterWorldCoordinate);
        var solids = new Dictionary<int, Vector3[]>
        {
            [_cellA.GenerateOrder] = solidA,
            [_cellB.GenerateOrder] = solidB,
            [_cellC.GenerateOrder] = solidC,
        };
        var plainRects = new Dictionary<(int, Enums.HexDirection), RectangleTransitionMeshData>
        {
            [(_cellA.GenerateOrder, Enums.HexDirection.NE)] = BuildPlainRect(
                _cellA, _cellB, Enums.HexDirection.NE, solids),
            [(_cellB.GenerateOrder, Enums.HexDirection.SE)] = BuildPlainRect(
                _cellB, _cellC, Enums.HexDirection.SE, solids),
            [(_cellA.GenerateOrder, Enums.HexDirection.E)] = BuildPlainRect(
                _cellA, _cellC, Enums.HexDirection.E, solids),
        };
        TriangleTransitionMeshData plainTri = BuildPlainTri(
            _cellA, _cellB, _cellC,
            new[] { Enums.HexDirection.NE, Enums.HexDirection.E }, plainRects);

        Assert.IsNull(MountainGeometryBuilder.BuildRidgeEdgeTriangleShoulder(
            _cellA, _cellB, _cellC, plainTri),
            "默认夹具 A/B 不同 ridgeId，非连续脊边不得生成肩部");

        _cellB.mountainRidge.ridgeId = _cellA.mountainRidge.ridgeId;
        Assert.IsNull(MountainGeometryBuilder.BuildRidgeEdgeTriangleShoulder(
            _cellA, _cellB, _cellC, plainTri),
            "第三格也是有效山格时由 3 山格 tri 路由负责，不得叠加肩部");
    }

    [Test]
    public void Determinism_SameInputSameGeometryHash()
    {
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        CellGeometry first = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);
        CellGeometry second = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);

        Assert.AreEqual(
            MountainGeometryBuilder.GeometryHash(first.Vertices, first.Indices),
            MountainGeometryBuilder.GeometryHash(second.Vertices, second.Indices),
            "固定输入重建几何 hash 必须一致（决策 ㉓/㉛）");
    }

    [Test]
    public void Determinism_RebuildAllGeometry_IdenticalHash_AndCollisionTrackUnchanged()
    {
        // 阶段 7.3：同一 Chunk 数据重复构建（solid/rect/tri 全量 + collision 双轨）——
        // 渲染山体（solid/rect/tri 各 7 件）重建 hash 一致；collision 双轨共享的原始 solid
        // 顶点数组在多次构建后不被改写（collision 面 = 原样复用原始地形数据，逐位稳定）。
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);
        Vector3[] solidC = CreateSolid(_cellC.CenterWorldCoordinate);
        Vector3[] beforeA = (Vector3[])solidA.Clone();
        Vector3[] beforeB = (Vector3[])solidB.Clone();
        Vector3[] beforeC = (Vector3[])solidC.Clone();

        BuildAllGeometries(solidA, solidB, solidC, out CellGeometry firstA, out CellGeometry firstB,
            out CellGeometry firstC, out CellGeometry firstAB, out CellGeometry firstBC, out CellGeometry firstAC,
            out CellGeometry firstTri);
        BuildAllGeometries(solidA, solidB, solidC, out CellGeometry secondA, out CellGeometry secondB,
            out CellGeometry secondC, out CellGeometry secondAB, out CellGeometry secondBC, out CellGeometry secondAC,
            out CellGeometry secondTri);

        AssertHashEqual(firstA, secondA, "solid A");
        AssertHashEqual(firstB, secondB, "solid B");
        AssertHashEqual(firstC, secondC, "solid C");
        AssertHashEqual(firstAB, secondAB, "rect A-B");
        AssertHashEqual(firstBC, secondBC, "rect B-C");
        AssertHashEqual(firstAC, secondAC, "rect A-C");
        AssertHashEqual(firstTri, secondTri, "3 山格 tri");

        for (int i = 0; i < beforeA.Length; i++)
        {
            Assert.AreEqual(beforeA[i], solidA[i], $"原始 solidA[{i}] 未被山体构建改写（collision 双轨）");
            Assert.AreEqual(beforeB[i], solidB[i], $"原始 solidB[{i}] 未被山体构建改写（collision 双轨）");
            Assert.AreEqual(beforeC[i], solidC[i], $"原始 solidC[{i}] 未被山体构建改写（collision 双轨）");
        }
    }

    private static void AssertHashEqual(CellGeometry a, CellGeometry b, string label)
    {
        Assert.AreEqual(
            MountainGeometryBuilder.GeometryHash(a.Vertices, a.Indices),
            MountainGeometryBuilder.GeometryHash(b.Vertices, b.Indices),
            $"{label} 固定输入重建几何 hash 必须一致（决策 ㉛）");
    }

    private void BuildAllGeometries(Vector3[] solidA, Vector3[] solidB, Vector3[] solidC,
        out CellGeometry geoA, out CellGeometry geoB, out CellGeometry geoC,
        out CellGeometry rectAB, out CellGeometry rectBC, out CellGeometry rectAC,
        out CellGeometry tri)
    {
        geoA = MountainGeometryBuilder.BuildSolidMountain(_cellA, solidA, _neighborOf);
        geoB = MountainGeometryBuilder.BuildSolidMountain(_cellB, solidB, _neighborOf);
        geoC = MountainGeometryBuilder.BuildSolidMountain(_cellC, solidC, _neighborOf);
        MountainRectBuild buildAB = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellB, solidA, solidB, Enums.HexDirection.NE, _neighborOf);
        MountainRectBuild buildBC = MountainGeometryBuilder.BuildMountainRectData(
            _cellB, _cellC, solidB, solidC, Enums.HexDirection.SE, _neighborOf);
        MountainRectBuild buildAC = MountainGeometryBuilder.BuildMountainRectData(
            _cellA, _cellC, solidA, solidC, Enums.HexDirection.E, _neighborOf);
        rectAB = MountainGeometryBuilder.RectToRender(buildAB, _cellA, _cellB);
        rectBC = MountainGeometryBuilder.RectToRender(buildBC, _cellB, _cellC);
        rectAC = MountainGeometryBuilder.RectToRender(buildAC, _cellA, _cellC);
        var rects = new Dictionary<(int, Enums.HexDirection), RectangleTransitionMeshData>
        {
            [(_cellA.GenerateOrder, Enums.HexDirection.NE)] = buildAB.Rect,
            [(_cellB.GenerateOrder, Enums.HexDirection.SE)] = buildBC.Rect,
            [(_cellA.GenerateOrder, Enums.HexDirection.E)] = buildAC.Rect,
        };
        tri = MountainGeometryBuilder.BuildTriangleMountain(
            _cellA, _neighborOf, (c, d) => rects[(c.GenerateOrder, d)], Enums.HexDirection.NE, Enums.HexDirection.E);
    }

    [Test]
    public void Diagnostics_NonManifoldEdges_Detected()
    {
        // 两条三角共用一条边（usage=2 合法）；第三条再复用该边 ⇒ usage=3 非流形
        int[] indices = { 0, 1, 2, 1, 2, 3, 1, 2, 4 };
        var nonManifold = MountainGeometryBuilder.FindNonManifoldEdges(indices);
        Assert.AreEqual(1, nonManifold.Count);
        Assert.AreEqual(3, nonManifold[0].usage);
    }

    // ── 阶段 7.4：全图拓扑验收（决策 ㉛ 几何项）──────────────────

    private const int ScanGridWidth = 18;
    private const int ScanGridHeight = 18;

    private static readonly Vector3[] GridDeltas =
    {
        new Vector3(0, -1, 1), new Vector3(1, -1, 0), new Vector3(1, 0, -1),
        new Vector3(0, 1, -1), new Vector3(-1, 1, 0), new Vector3(-1, 0, 1),
    };

    /// <summary>plain solid 扇形（44 点布局的 0 中心 + 18 环点，无河路径；山格恒无河，决策 ③）。</summary>
    private static int[] BuildPlainFanIndices()
    {
        var fan = new List<int>(54);
        for (int i = 1; i <= 18; i++)
        {
            fan.Add(0);
            fan.Add(i);
            fan.Add(i == 18 ? 1 : i + 1);
        }
        return fan.ToArray();
    }

    /// <summary>plain rect（原始 solid 点，Slope 直纹面）：复刻 GetGenericRectangleMesh 契约。</summary>
    private static RectangleTransitionMeshData BuildPlainRect(HexCellData owner, HexCellData neighbor,
        Enums.HexDirection direction, IReadOnlyDictionary<int, Vector3[]> solidArrays)
    {
        int[] startIndices;
        int[] endIndices;
        switch (direction)
        {
            case Enums.HexDirection.NE:
                startIndices = new[] { 1, 7, 8, 2 };
                endIndices = new[] { 5, 14, 13, 4 };
                break;
            case Enums.HexDirection.E:
                startIndices = new[] { 2, 9, 10, 3 };
                endIndices = new[] { 6, 16, 15, 5 };
                break;
            default:
                startIndices = new[] { 3, 11, 12, 4 };
                endIndices = new[] { 1, 18, 17, 6 };
                break;
        }
        Vector3[] ownerSolid = solidArrays[owner.GenerateOrder];
        Vector3[] neighborSolid = solidArrays[neighbor.GenerateOrder];
        var starts = new List<Vector3>(4);
        var ends = new List<Vector3>(4);
        for (int p = 0; p < 4; p++)
        {
            starts.Add(ownerSolid[startIndices[p]]);
            ends.Add(neighborSolid[endIndices[p]]);
        }
        return RectangleTransitionMesh.Build(starts, ends, Enums.TransitionEdgeType.Slope, 0, false);
    }

    private static (Vector3 a, Vector3 b, Vector3 c) NormalizeJunction(HexCellData x, HexCellData y, HexCellData z)
    {
        var list = new List<Vector3> { x.HexCoordinate, y.HexCoordinate, z.HexCoordinate };
        list.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x)
            : p.z != q.z ? p.z.CompareTo(q.z) : p.y.CompareTo(q.y));
        return (list[0], list[1], list[2]);
    }

    /// <summary>plain tri（三格交界封口）：复刻 RectangleDrivenTriangleMesh 路由。
    /// 中间 rect = 连接两个邻居的边：对 (NE,E) 归 (c+NE, SE)；对 (E,SE) 归 (c+SE, NE)。</summary>
    private static TriangleTransitionMeshData BuildPlainTri(HexCellData cell, HexCellData neighborA,
        HexCellData neighborB, Enums.HexDirection[] pair,
        IReadOnlyDictionary<(int, Enums.HexDirection), RectangleTransitionMeshData> plainRects)
    {
        if (pair[0] == Enums.HexDirection.NE && pair[1] == Enums.HexDirection.E)
        {
            return RectangleDrivenTriangleMesh.BuildNEE(
                plainRects[(cell.GenerateOrder, Enums.HexDirection.NE)],
                plainRects[(neighborA.GenerateOrder, Enums.HexDirection.SE)],
                plainRects[(cell.GenerateOrder, Enums.HexDirection.E)]);
        }
        HexCellData midCell = neighborB; // (E,SE)：中间边 = c+SE ↔ c+E，归 c+SE 的 NE rect
        return RectangleDrivenTriangleMesh.BuildESE(
            plainRects[(cell.GenerateOrder, Enums.HexDirection.E)],
            plainRects[(midCell.GenerateOrder, Enums.HexDirection.NE)],
            plainRects[(cell.GenerateOrder, Enums.HexDirection.SE)]);
    }

    [Test]
    public void FullMapTopologyScan_RenderAndCollisionTracks_NoDegenerateNoNonManifold()
    {
        // 阶段 7.4：固定 seed 全图拓扑验收——复刻 Chunk 共享顶点数组 + 双轨装配，
        // 渲染槽（地形槽 + 山体槽）与 collision 槽分别扫描：
        //  无退化三角（面积 < 1e-6）、无非流形边（边被 >2 面引用）、索引不越界；
        //  替换式拓扑：collision 索引不得引用山体顶点区间（阶段 5.8 MountainVertexRanges 语义）、
        //  山体渲染索引必须落在山体区间内；山-普通格固定锚点（格界 = 两端原始 solid 中点）。
        var config = ScriptableObject.CreateInstance<MountainConfigSO>();
        var form = ScriptableObject.CreateInstance<MapLandFormSO>();
        config.mountainLandForm = form;
        form.mountainForm = true;
        form.blockBuildingSpawn = true;
        config.ridgeCount = 4;
        config.minRidgeLength = 5;
        config.maxRidgeLength = 12;
        config.widthRadius = 1.5f;
        config.ridgeMinSpacing = 2;
        config.baseHeight = 1.2f;
        config.minHeight = 0.8f;
        config.maxHeight = 2.5f;
        config.heightPerLength = 0.12f;
        config.gamma = 1.2f;
        config.minVisibleHeight = 0.15f;
        config.maxSlopeRatio = 0.8f;
        config.xzPerturbRatio = 0.15f;
        config.peakEccentricMinRatio = 0.05f;
        config.peakEccentricMaxRatio = 0.2f;
        config.scoreHeightWeight = 1f;
        config.scoreDropWeight = 1f;
        config.scoreTurnPenalty = 1f;
        config.flatHeightThreshold = 1f;
        config.ridgeNoiseAmplitude = 0.4f;
        config.cellNoiseScale = 0.3f;

        try
        {
            var cells = new List<HexCellData>(ScanGridWidth * ScanGridHeight);
            var byHex = new Dictionary<Vector3, HexCellData>();
            int order = 0;
            for (int j = 0; j < ScanGridHeight; j++)
            {
                int offset = j / 2;
                for (int i = 0; i < ScanGridWidth; i++)
                {
                    var hex = new Vector3(i - offset, -(i - offset) - j, j);
                    float wx = hex.x * 2f * TestInnerRadius + hex.z * TestInnerRadius;
                    float wz = hex.z * 1.5f * TestOuterRadius;
                    // 起伏基础地形（> 水位），使山-普通固定锚点断言非平凡
                    float terrainY = 1.5f + (order % 3) * 0.25f;
                    var cell = new HexCellData(
                        Enums.HexType.NoRiver, order++, hex, new Vector3(wx, terrainY, wz), terrainY);
                    cells.Add(cell);
                    byHex[hex] = cell;
                }
            }
            System.Func<HexCellData, Enums.HexDirection, HexCellData> neighborOf = (c, d) =>
            {
                if (c == null || d == Enums.HexDirection.None) return null;
                byHex.TryGetValue(c.HexCoordinate + GridDeltas[(int)d], out HexCellData neighbor);
                return neighbor;
            };

            List<MountainRidgeData> ridges = RidgeGenerator.Generate(config, cells,
                c => GetGridNeighbors(cells, c), new System.Random(20260729));
            int mountainCount = cells.Count(c => MountainCellRule.IsMountainCell(c));
            Assert.Greater(ridges.Count, 0, "固定 seed 应生成山脉");
            Assert.Greater(mountainCount, 0, "固定 seed 应产生山格");

            // ── 装配（复刻 BuildChunkTerrain 共享顶点数组 + 双轨）──
            var shared = new List<Vector3>();
            var renderIndices = new List<int>();
            var renderMountainIndices = new List<int>();
            var collisionIndices = new List<int>();
            var mountainRanges = new List<int>();
            var solidArrays = new Dictionary<int, Vector3[]>();
            var plainRects = new Dictionary<(int, Enums.HexDirection), RectangleTransitionMeshData>();
            int mountainPlainEdgeCount = 0;
            int mountainMountainEdgeCount = 0;

            // 1) plain solid（全部格；山格 44 点仅进 collision，与 Chunk :1061-1107 同路由）
            foreach (HexCellData cell in cells)
            {
                Vector3[] solid = CreateSolid(cell.CenterWorldCoordinate);
                solidArrays[cell.GenerateOrder] = solid;
                int start = shared.Count;
                shared.AddRange(solid);
                int[] fan = BuildPlainFanIndices();
                if (MountainGeometryBuilder.HasVisibleMountain(cell))
                {
                    foreach (int i in fan) collisionIndices.Add(i + start);
                    CellGeometry mountain = MountainGeometryBuilder.BuildSolidMountain(cell, solid, neighborOf);
                    int mountainStart = shared.Count;
                    shared.AddRange(mountain.Vertices);
                    mountainRanges.Add(mountainStart);
                    mountainRanges.Add(mountain.Vertices.Length);
                    foreach (int i in mountain.Indices)
                    {
                        renderIndices.Add(i + mountainStart);
                        renderMountainIndices.Add(i + mountainStart);
                    }
                }
                else
                {
                    foreach (int i in fan)
                    {
                        renderIndices.Add(i + start);
                        collisionIndices.Add(i + start);
                    }
                }
            }

            // 2) plain rect（全部边）+ mountain rect（贴山边，替换式）
            Enums.HexDirection[] ownerDirs = { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE };
            foreach (HexCellData cell in cells)
            {
                foreach (Enums.HexDirection dir in ownerDirs)
                {
                    HexCellData neighbor = neighborOf(cell, dir);
                    if (neighbor == null) continue;
                    RectangleTransitionMeshData plain = BuildPlainRect(cell, neighbor, dir, solidArrays);
                    plainRects[(cell.GenerateOrder, dir)] = plain;
                    int start = shared.Count;
                    shared.AddRange(plain.Vertices);
                    bool edgeMountain = MountainGeometryBuilder.HasVisibleMountain(cell)
                        || MountainGeometryBuilder.HasVisibleMountain(neighbor);
                    if (edgeMountain)
                    {
                        bool bothVisible = MountainGeometryBuilder.HasVisibleMountain(cell)
                            && MountainGeometryBuilder.HasVisibleMountain(neighbor);
                        if (bothVisible) mountainMountainEdgeCount++;
                        else mountainPlainEdgeCount++;

                        foreach (int i in plain.Indices) collisionIndices.Add(i + start);
                        MountainRectBuild build = MountainGeometryBuilder.BuildMountainRectData(
                            cell, neighbor, solidArrays[cell.GenerateOrder], solidArrays[neighbor.GenerateOrder],
                            dir, neighborOf);
                        if (!bothVisible)
                        {
                            // 决策 ④ 固定锚点 + 格界劈半（续22）：山侧/普通两件在格界点严格闭合，
                            // 且格界点 = 两端原始 solid 中点；山格可能是 owner 也可能是 neighbor（取向感知）
                            bool ownerIsMountain = MountainGeometryBuilder.HasVisibleMountain(cell);
                            Vector3[] ownerSolid = solidArrays[cell.GenerateOrder];
                            Vector3[] neighborSolid = solidArrays[neighbor.GenerateOrder];
                            (int[] startIndices, int[] endIndices) = ProfileEndpointsForDirection(dir);
                            Assert.IsNotNull(build.PlainRect, "山-普通 rect 必须产出普通半边（回地形槽，续22）");
                            for (int p = 0; p < 4; p++)
                            {
                                float expected = (ownerSolid[startIndices[p]].y + neighborSolid[endIndices[p]].y) * 0.5f;
                                Vector3 boundaryM = build.Rect.Profiles[p].Points[ownerIsMountain ? 1 : 0];
                                Vector3 boundaryP = build.PlainRect.Profiles[p].Points[ownerIsMountain ? 0 : 1];
                                Assert.AreEqual(expected, boundaryM.y, 1e-4f,
                                    $"山-普通格界锚点 = 两端原始 solid 中点（决策 ④）");
                                Assert.AreEqual(boundaryM, boundaryP,
                                    "山侧/普通两件在格界点严格闭合（决策 ㉛）");
                            }
                        }
                        else
                        {
                            Assert.IsNull(build.PlainRect, "山-山 rect 整面山体，无普通半边（续22）");
                        }
                        CellGeometry mountain = MountainGeometryBuilder.RectToRender(build, cell, neighbor);
                        int mountainStart = shared.Count;
                        shared.AddRange(mountain.Vertices);
                        mountainRanges.Add(mountainStart);
                        mountainRanges.Add(mountain.Vertices.Length);
                        foreach (int i in mountain.Indices)
                        {
                            renderIndices.Add(i + mountainStart);
                            renderMountainIndices.Add(i + mountainStart);
                        }

                        // 普通半边回地形槽（续22）：进渲染轨但不进山体区间（与 ChunkMapRenderer 装配一致）
                        if (build.PlainRect != null)
                        {
                            int plainHalfStart = shared.Count;
                            shared.AddRange(build.PlainRect.Vertices);
                            foreach (int i in build.PlainRect.Indices) renderIndices.Add(i + plainHalfStart);
                        }
                    }
                    else
                    {
                        foreach (int i in plain.Indices)
                        {
                            renderIndices.Add(i + start);
                            collisionIndices.Add(i + start);
                        }
                    }
                }
            }

            Assert.Greater(mountainMountainEdgeCount, 0, "固定 seed 地图应含山-山公共边（决策 ④ 规范化共享边）");
            Assert.Greater(mountainPlainEdgeCount, 0, "固定 seed 地图应含山-普通公共边（固定锚点）");

            // 3) plain tri（全部交界）+ mountain tri（3 山格）
            Enums.HexDirection[][] triPairs =
            {
                new[] { Enums.HexDirection.NE, Enums.HexDirection.E },
                new[] { Enums.HexDirection.E, Enums.HexDirection.SE },
            };
            var visitedJunctions = new HashSet<(Vector3, Vector3, Vector3)>();
            foreach (HexCellData cell in cells)
            {
                foreach (Enums.HexDirection[] pair in triPairs)
                {
                    HexCellData neighborA = neighborOf(cell, pair[0]);
                    HexCellData neighborB = neighborOf(cell, pair[1]);
                    if (neighborA == null || neighborB == null) continue;
                    if (!visitedJunctions.Add(NormalizeJunction(cell, neighborA, neighborB))) continue;

                    TriangleTransitionMeshData plain = BuildPlainTri(cell, neighborA, neighborB, pair, plainRects);
                    bool allMountain = MountainGeometryBuilder.HasVisibleMountain(cell)
                        && MountainGeometryBuilder.HasVisibleMountain(neighborA)
                        && MountainGeometryBuilder.HasVisibleMountain(neighborB);
                    if (allMountain)
                    {
                        // 复刻 Chunk 真实装配顺序：山体 tri 先追加（山体区间），plain tri 后追加
                        // （collision 索引必须取 plain 实际偏移——阶段 7.4 曾因复用旧 IndexOffset 落入山体区间）
                        // 山体 tri 复刻 Chunk 路由：三条 mountain rect（GetMountainRectangleMesh(ctx, dir).Rect）
                        // BuildTriangleMountain 内部 neighborA = neighborOf(owner, pair[0] 的镜像)：
                        //   (NE,E) ⇒ 中间 rect = (cell+NE, SE)； (E,SE) ⇒ 中间 rect = (cell+SE, NE)
                        HexCellData midCell = neighborOf(cell,
                            pair[0] == Enums.HexDirection.NE ? Enums.HexDirection.NE : Enums.HexDirection.SE);
                        Enums.HexDirection midRectDir = pair[0] == Enums.HexDirection.NE
                            ? Enums.HexDirection.SE : Enums.HexDirection.NE;
                        HexCellData midNeighbor = neighborOf(midCell, midRectDir);
                        var rects = new Dictionary<(int, Enums.HexDirection), RectangleTransitionMeshData>
                        {
                            [(cell.GenerateOrder, pair[0])] = MountainGeometryBuilder.BuildMountainRectData(
                                cell, neighborA, solidArrays[cell.GenerateOrder], solidArrays[neighborA.GenerateOrder],
                                pair[0], neighborOf).Rect,
                            [(midCell.GenerateOrder, midRectDir)] = MountainGeometryBuilder.BuildMountainRectData(
                                midCell, midNeighbor, solidArrays[midCell.GenerateOrder], solidArrays[midNeighbor.GenerateOrder],
                                midRectDir, neighborOf).Rect,
                            [(cell.GenerateOrder, pair[1])] = MountainGeometryBuilder.BuildMountainRectData(
                                cell, neighborB, solidArrays[cell.GenerateOrder], solidArrays[neighborB.GenerateOrder],
                                pair[1], neighborOf).Rect,
                        };
                        CellGeometry mountain = MountainGeometryBuilder.BuildTriangleMountain(
                            cell, neighborOf, (c, d) => rects[(c.GenerateOrder, d)], pair[0], pair[1]);
                        int mountainStart = shared.Count;
                        shared.AddRange(mountain.Vertices);
                        mountainRanges.Add(mountainStart);
                        mountainRanges.Add(mountain.Vertices.Length);
                        foreach (int i in mountain.Indices)
                        {
                            renderIndices.Add(i + mountainStart);
                            renderMountainIndices.Add(i + mountainStart);
                        }

                        int plainStart = shared.Count;
                        shared.AddRange(plain.Vertices);
                        foreach (int i in plain.Indices) collisionIndices.Add(i + plainStart);
                    }
                    else
                    {
                        int start = shared.Count;
                        shared.AddRange(plain.Vertices);
                        foreach (int i in plain.Indices)
                        {
                            renderIndices.Add(i + start);
                            collisionIndices.Add(i + start);
                        }

                        CellGeometry shoulder = MountainGeometryBuilder.BuildRidgeEdgeTriangleShoulder(
                            cell, neighborA, neighborB, plain);
                        if (shoulder != null)
                        {
                            int shoulderStart = shared.Count;
                            shared.AddRange(shoulder.Vertices);
                            mountainRanges.Add(shoulderStart);
                            mountainRanges.Add(shoulder.Vertices.Length);
                            foreach (int i in shoulder.Indices)
                            {
                                renderIndices.Add(i + shoulderStart);
                                renderMountainIndices.Add(i + shoulderStart);
                            }
                        }
                    }
                }
            }

            // ── 扫描（决策 ㉛）──
            int[] renderArr = renderIndices.ToArray();
            int[] collisionArr = collisionIndices.ToArray();
            Assert.AreEqual(0, MountainGeometryBuilder.CountDegenerateTriangles(shared, renderArr), "渲染槽无退化三角");
            Assert.AreEqual(0, MountainGeometryBuilder.CountDegenerateTriangles(shared, collisionArr), "collision 槽无退化三角");
            Assert.AreEqual(0, MountainGeometryBuilder.FindNonManifoldEdges(renderArr).Count, "渲染槽无非流形边");
            Assert.AreEqual(0, MountainGeometryBuilder.FindNonManifoldEdges(collisionArr).Count, "collision 槽无非流形边");
            foreach (int i in renderArr)
                Assert.Less(i, shared.Count, "渲染索引不越界");
            foreach (int i in collisionArr)
                Assert.Less(i, shared.Count, "collision 索引不越界");

            // 替换式拓扑隔离（阶段 5.8 MountainVertexRanges 语义复刻）：
            // collision 索引不得落入山体顶点区间；山体渲染索引必须落在山体区间内
            foreach (int i in collisionArr)
            {
                for (int r = 0; r + 1 < mountainRanges.Count; r += 2)
                {
                    Assert.IsFalse(i >= mountainRanges[r] && i < mountainRanges[r] + mountainRanges[r + 1],
                        $"collision 索引 {i} 不得引用山体顶点区间（替换式拓扑）");
                }
            }
            Assert.Greater(mountainRanges.Count, 0, "装配应产生山体顶点区间");
            foreach (int i in renderMountainIndices)
            {
                bool inside = false;
                for (int r = 0; r + 1 < mountainRanges.Count; r += 2)
                {
                    if (i >= mountainRanges[r] && i < mountainRanges[r] + mountainRanges[r + 1])
                    {
                        inside = true;
                        break;
                    }
                }
                Assert.IsTrue(inside, $"山体渲染索引 {i} 必须落在山体顶点区间内");
            }
        }
        finally
        {
            Object.DestroyImmediate(form);
            Object.DestroyImmediate(config);
        }
    }

    private static (int[] startIndices, int[] endIndices) ProfileEndpointsForDirection(Enums.HexDirection direction)
    {
        switch (direction)
        {
            case Enums.HexDirection.NE:
                return (new[] { 1, 7, 8, 2 }, new[] { 5, 14, 13, 4 });
            case Enums.HexDirection.E:
                return (new[] { 2, 9, 10, 3 }, new[] { 6, 16, 15, 5 });
            default:
                return (new[] { 3, 11, 12, 4 }, new[] { 1, 18, 17, 6 });
        }
    }

    private static List<HexCellData> GetGridNeighbors(List<HexCellData> cells, HexCellData cell)
    {
        var result = new List<HexCellData>();
        foreach (HexCellData other in cells)
        {
            if (other == cell) continue;
            foreach (Vector3 delta in GridDeltas)
            {
                if (other.HexCoordinate == cell.HexCoordinate + delta)
                {
                    result.Add(other);
                    break;
                }
            }
        }
        return result;
    }

    [Test]
    public void OneOrTwoMountainJunction_CornerLiftIsZero_PlainTriFlatClosed()
    {
        // 阶段 7.4：三格交界 tri 三种情形——1/2 山格时角点恒 0（决策 ⑤），
        // plain tri 保持普通构建（闭合、平坦于基础地形，无洞/无竖裂缝/无薄片）。
        BuildMountainFixture();
        Vector3[] solidA = CreateSolid(_cellA.CenterWorldCoordinate);
        Vector3[] solidB = CreateSolid(_cellB.CenterWorldCoordinate);
        Vector3[] solidC = CreateSolid(_cellC.CenterWorldCoordinate);

        // 2 山格：A、B 山，C 普通（交界 {A,B,C}）
        _cellC.landForm = null;
        Assert.AreEqual(0f, MountainGeometryBuilder.CornerLift(_cellA, 2, _neighborOf), 1e-6f, "2 山格交汇角点恒 0");
        Assert.AreEqual(0f, MountainGeometryBuilder.CornerLift(_cellB, 4, _neighborOf), 1e-6f, "2 山格镜像角点恒 0");
        Assert.AreEqual(0f, MountainGeometryBuilder.CornerLift(_cellC, 6, _neighborOf), 1e-6f, "普通格侧角点恒 0");

        // 1 山格：仅 A 山（交界 {A,B,C} 与 {A,C,D}）
        _cellB.landForm = null;
        Assert.AreEqual(0f, MountainGeometryBuilder.CornerLift(_cellA, 2, _neighborOf), 1e-6f, "1 山格交汇角点恒 0");
        Assert.AreEqual(0f, MountainGeometryBuilder.CornerLift(_cellA, 3, _neighborOf), 1e-6f, "1 山格相邻交汇角点恒 0");
        Assert.AreEqual(0f, MountainGeometryBuilder.CornerLift(_cellA, 1, _neighborOf), 1e-6f, "1 山格 + 越界邻居角点恒 0");

        // plain tri 封口：三条 plain rect（原始 solid 点）→ BuildNEE 闭合（ValidateConnectedEdges < 1e-4）
        // 且平坦于基础地形 Y=2（角点隆起 0 ⇒ 无洞/无竖裂缝/无薄片）
        var plainRects = new Dictionary<(int, Enums.HexDirection), RectangleTransitionMeshData>();
        plainRects[(_cellA.GenerateOrder, Enums.HexDirection.NE)] = BuildPlainRect(_cellA, _cellB, Enums.HexDirection.NE,
            new Dictionary<int, Vector3[]> { [_cellA.GenerateOrder] = solidA, [_cellB.GenerateOrder] = solidB });
        plainRects[(_cellB.GenerateOrder, Enums.HexDirection.SE)] = BuildPlainRect(_cellB, _cellC, Enums.HexDirection.SE,
            new Dictionary<int, Vector3[]> { [_cellB.GenerateOrder] = solidB, [_cellC.GenerateOrder] = solidC });
        plainRects[(_cellA.GenerateOrder, Enums.HexDirection.E)] = BuildPlainRect(_cellA, _cellC, Enums.HexDirection.E,
            new Dictionary<int, Vector3[]> { [_cellA.GenerateOrder] = solidA, [_cellC.GenerateOrder] = solidC });

        TriangleTransitionMeshData tri = RectangleDrivenTriangleMesh.BuildNEE(
            plainRects[(_cellA.GenerateOrder, Enums.HexDirection.NE)],
            plainRects[(_cellB.GenerateOrder, Enums.HexDirection.SE)],
            plainRects[(_cellA.GenerateOrder, Enums.HexDirection.E)]);
        Assert.AreEqual(0, MountainGeometryBuilder.CountDegenerateTriangles(tri.Vertices, tri.Indices), "plain tri 无退化三角");
        foreach (Vector3 v in tri.Vertices)
            Assert.AreEqual(2f, v.y, 1e-4f, "1/2 山格 tri 平坦于基础地形 Y（无隆起封口，无洞）");
    }
}
