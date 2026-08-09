using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 【程序化山脉】脊线生成器纯函数测试（决策 ⑯/⑰/⑱/㉑/㉔）。
/// 覆盖：同种子确定性、脊线长度范围、脊线路径相邻性、山格数据固化（d/s/快照）、
/// 水域排除、跨脊线互斥、宽度化坡面格、折线投影距离。
/// </summary>
public class RidgeGeneratorTests
{
    private const int Width = 16;
    private const int Height = 16;
    private const float OuterRadius = 3f;
    private static readonly float InnerRadius = OuterRadius * 0.8660254f;
    private const float LandHeight = 2f;
    private const float WaterHeight = 0.5f;

    [SetUp]
    public void SetUp()
    {
        WaterLevelConfig.WaterLevel = 1f;
        WaterLevelConfig.MaxHeight = 5f;
    }

    private static MountainConfigSO CreateConfig(int ridgeCount = 3, int minLen = 5, int maxLen = 12, int minSpacing = 2)
    {
        var config = ScriptableObject.CreateInstance<MountainConfigSO>();
        config.mountainLandForm = ScriptableObject.CreateInstance<MapLandFormSO>();
        config.mountainLandForm.mountainForm = true;
        config.mountainLandForm.blockBuildingSpawn = true;
        config.ridgeCount = ridgeCount;
        config.minRidgeLength = minLen;
        config.maxRidgeLength = maxLen;
        config.widthRadius = 1.5f;
        config.ridgeMinSpacing = minSpacing;
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
        return config;
    }

    private static void DestroyConfig(MountainConfigSO config)
    {
        if (config == null) return;
        if (config.mountainLandForm != null)
            Object.DestroyImmediate(config.mountainLandForm);
        Object.DestroyImmediate(config);
    }

    private static List<HexCellData> BuildGrid(int width, int height, float heightValue)
    {
        var cells = new List<HexCellData>(width * height);
        int order = 0;
        for (int j = 0; j < height; j++)
        {
            int offset = j / 2;
            for (int i = 0; i < width; i++)
            {
                var hex = new Vector3(i - offset, -(i - offset) - j, j);
                float wx = hex.x * 2f * InnerRadius + hex.z * InnerRadius;
                float wz = hex.z * 1.5f * OuterRadius;
                cells.Add(new HexCellData(Enums.HexType.NoRiver, order++, hex, new Vector3(wx, 0f, wz), heightValue));
            }
        }
        return cells;
    }

    private static List<HexCellData> GetGridNeighbors(List<HexCellData> cells, HexCellData cell)
    {
        var deltas = new[]
        {
            new Vector3(0, -1, 1), new Vector3(1, -1, 0), new Vector3(1, 0, -1),
            new Vector3(0, 1, -1), new Vector3(-1, 1, 0), new Vector3(-1, 0, 1),
        };
        var result = new List<HexCellData>();
        foreach (HexCellData other in cells)
        {
            if (other == cell) continue;
            foreach (Vector3 delta in deltas)
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

    private static int HexDistance(List<HexCellData> cells, HexCellData a, HexCellData b)
    {
        var frontier = new Queue<HexCellData>();
        var distances = new Dictionary<HexCellData, int>();
        frontier.Enqueue(a);
        distances[a] = 0;
        while (frontier.Count > 0)
        {
            HexCellData current = frontier.Dequeue();
            if (current == b) return distances[current];
            foreach (HexCellData neighbor in GetGridNeighbors(cells, current))
            {
                if (distances.ContainsKey(neighbor)) continue;
                distances[neighbor] = distances[current] + 1;
                frontier.Enqueue(neighbor);
            }
        }
        return int.MaxValue;
    }

    private static IEnumerable<HexCellData> MountainCells(List<HexCellData> cells)
    {
        return cells.Where(c => c != null && MountainCellRule.IsMountainCell(c));
    }

    // ── 测试 ─────────────────────────────────────────────

    [Test]
    public void Generate_NoMountainLandForm_ReturnsEmpty()
    {
        MountainConfigSO config = CreateConfig();
        MapLandFormSO form = config.mountainLandForm;
        config.mountainLandForm = null;
        try
        {
            List<HexCellData> cells = BuildGrid(Width, Height, LandHeight);
            List<MountainRidgeData> ridges = RidgeGenerator.Generate(
                config, cells, c => GetGridNeighbors(cells, c), new System.Random(42));

            Assert.AreEqual(0, ridges.Count);
            Assert.AreEqual(0, MountainCells(cells).Count());
        }
        finally
        {
            config.mountainLandForm = form;
            DestroyConfig(config);
        }
    }

    [Test]
    public void Generate_SameSeed_ProducesIdenticalMountainCells()
    {
        MountainConfigSO config = CreateConfig();
        try
        {
            List<HexCellData> cellsA = BuildGrid(Width, Height, LandHeight);
            List<HexCellData> cellsB = BuildGrid(Width, Height, LandHeight);

            List<MountainRidgeData> ridgesA = RidgeGenerator.Generate(
                config, cellsA, c => GetGridNeighbors(cellsA, c), new System.Random(12345));
            List<MountainRidgeData> ridgesB = RidgeGenerator.Generate(
                config, cellsB, c => GetGridNeighbors(cellsB, c), new System.Random(12345));

            var coordsA = new HashSet<Vector3>(MountainCells(cellsA).Select(c => c.HexCoordinate));
            var coordsB = new HashSet<Vector3>(MountainCells(cellsB).Select(c => c.HexCoordinate));
            Assert.Greater(coordsA.Count, 0, "同种子应生成山脉");
            Assert.IsTrue(coordsA.SetEquals(coordsB), "同种子山格集合必须一致（决策 ㉓）");

            Assert.AreEqual(ridgesA.Count, ridgesB.Count, "同种子脊线数量一致");
            for (int i = 0; i < ridgesA.Count; i++)
            {
                Assert.AreEqual(ridgesA[i].length, ridgesB[i].length);
                Assert.AreEqual(ridgesA[i].seed, ridgesB[i].seed);
                Assert.AreEqual(ridgesA[i].hMax, ridgesB[i].hMax);
                Assert.AreEqual(ridgesA[i].ridgeNoiseAmplitude, ridgesB[i].ridgeNoiseAmplitude);
                Assert.AreEqual(ridgesA[i].cellNoiseScale, ridgesB[i].cellNoiseScale);
                Assert.AreEqual(ridgesA[i].mountainCellCount, ridgesB[i].mountainCellCount);
            }
        }
        finally
        {
            DestroyConfig(config);
        }
    }

    [Test]
    public void Generate_SameSeed_RepeatedRuns_IdenticalRidgePaths()
    {
        // 阶段 7.3：固定 seed 重复生成 N=3 次——脊线路径逐格、山格集合、固化参数逐项一致
        // （决策 ㉓：全部随机量走确定性流；决策 ㉛：固定种子重建 hash 一致）。
        MountainConfigSO config = CreateConfig();
        try
        {
            const int seed = 20260729;
            const int runs = 3;
            var paths = new List<List<List<Vector3>>>();
            var cellSets = new List<HashSet<Vector3>>();
            var snapshots = new List<List<MountainRidgeData>>();
            for (int r = 0; r < runs; r++)
            {
                List<HexCellData> cells = BuildGrid(Width, Height, LandHeight);
                List<MountainRidgeData> ridges = RidgeGenerator.Generate(
                    config, cells, c => GetGridNeighbors(cells, c), new System.Random(seed));
                snapshots.Add(ridges);
                paths.Add(ridges.Select(rd => new List<Vector3>(rd.ridgeHexes)).ToList());
                cellSets.Add(new HashSet<Vector3>(MountainCells(cells).Select(c => c.HexCoordinate)));
            }

            Assert.Greater(paths[0].Count, 0, "固定 seed 应生成山脉");
            for (int r = 1; r < runs; r++)
            {
                Assert.AreEqual(paths[0].Count, paths[r].Count, $"run{r} 脊线数量一致");
                Assert.IsTrue(cellSets[0].SetEquals(cellSets[r]), $"run{r} 山格集合一致（决策 ㉓）");
                for (int i = 0; i < paths[0].Count; i++)
                {
                    Assert.AreEqual(paths[0][i].Count, paths[r][i].Count, $"run{r} 脊线{i} 长度一致");
                    for (int k = 0; k < paths[0][i].Count; k++)
                        Assert.AreEqual(paths[0][i][k], paths[r][i][k],
                            $"run{r} 脊线{i} 第{k}格路径一致（决策 ㉛ 固定种子重建一致）");

                    MountainRidgeData a = snapshots[0][i];
                    MountainRidgeData b = snapshots[r][i];
                    Assert.AreEqual(a.ridgeId, b.ridgeId);
                    Assert.AreEqual(a.seed, b.seed);
                    Assert.AreEqual(a.length, b.length);
                    Assert.AreEqual(a.hMax, b.hMax, 1e-6f);
                    Assert.AreEqual(a.ridgeNoiseAmplitude, b.ridgeNoiseAmplitude, 1e-6f);
                    Assert.AreEqual(a.cellNoiseScale, b.cellNoiseScale, 1e-6f);
                    Assert.AreEqual(a.mountainCellCount, b.mountainCellCount);
                }
            }
        }
        finally
        {
            DestroyConfig(config);
        }
    }

    [Test]
    public void Generate_MountainCells_HaveFrozenDataAndImpassableCost()
    {
        MountainConfigSO config = CreateConfig();
        try
        {
            List<HexCellData> cells = BuildGrid(Width, Height, LandHeight);
            List<MountainRidgeData> ridges = RidgeGenerator.Generate(
                config, cells, c => GetGridNeighbors(cells, c), new System.Random(7));

            Assert.Greater(ridges.Count, 0, "默认配置下 16x16 地图应能生成山脉");
            Assert.LessOrEqual(ridges.Count, config.ridgeCount);

            foreach (HexCellData cell in MountainCells(cells))
            {
                Assert.AreSame(config.mountainLandForm, cell.landForm, "山格占用标记 = 山脉地貌 SO");
                Assert.IsNotNull(cell.mountainRidge, "山格必须持有固化参数快照（决策 ②）");
                Assert.AreEqual(config.ridgeNoiseAmplitude, cell.mountainRidge.ridgeNoiseAmplitude, 1e-6f);
                Assert.AreEqual(config.cellNoiseScale, cell.mountainRidge.cellNoiseScale, 1e-6f);
                Assert.AreEqual(float.MaxValue, cell.movementCost, "山格不可通行（决策 ①）");
                Assert.IsFalse(cell.mountainCleared);
                Assert.LessOrEqual(cell.mountainDistToRidge, config.widthRadius + 1e-3f, "d 必须在宽度化半径内");

                if (cell.mountainRidgeStatus == Enums.MountainRidgeStatus.RidgeCell)
                    Assert.AreEqual(0f, cell.mountainDistToRidge, "脊线格 d = 0");
                else
                    Assert.AreEqual(Enums.MountainRidgeStatus.SlopeCell, cell.mountainRidgeStatus, "非脊线格必须是坡面格");
            }
        }
        finally
        {
            DestroyConfig(config);
        }
    }

    [Test]
    public void Generate_RidgeLengthsWithinBounds_AndPathAdjacent()
    {
        MountainConfigSO config = CreateConfig(ridgeCount: 4, minLen: 5, maxLen: 12);
        try
        {
            List<HexCellData> cells = BuildGrid(Width, Height, LandHeight);
            List<MountainRidgeData> ridges = RidgeGenerator.Generate(
                config, cells, c => GetGridNeighbors(cells, c), new System.Random(99));

            Assert.GreaterOrEqual(ridges.Count, 1);
            foreach (MountainRidgeData ridge in ridges)
            {
                Assert.GreaterOrEqual(ridge.length, config.minRidgeLength, "脊线长度不得低于最小值");
                Assert.LessOrEqual(ridge.length, config.maxRidgeLength, "脊线长度不得高于最大值");

                // 脊线路径相邻性：连续两格必须是六边形邻居
                for (int i = 0; i < ridge.ridgeHexes.Count - 1; i++)
                {
                    Enums.HexDirection dir = RidgeGenerator.DirectionFromTo(ridge.ridgeHexes[i], ridge.ridgeHexes[i + 1]);
                    Assert.AreNotEqual(Enums.HexDirection.None, dir, "脊线路径必须相邻");
                }

                // 端点在格数据上的方向字段：端点 1 个方向，中段 2 个方向
                foreach (HexCellData cell in cells)
                {
                    if (cell.mountainRidge != ridge || cell.mountainRidgeStatus != Enums.MountainRidgeStatus.RidgeCell)
                        continue;
                    int dirs = (cell.RidgeDirectionA != Enums.HexDirection.None ? 1 : 0)
                             + (cell.RidgeDirectionB != Enums.HexDirection.None ? 1 : 0);
                    bool isEndpoint = ridge.ridgeHexes.First() == cell.HexCoordinate || ridge.ridgeHexes.Last() == cell.HexCoordinate;
                    Assert.AreEqual(isEndpoint ? 1 : 2, dirs, "脊线格方向字段：端点 1、中段 2（决策 ⑯ 无向）");
                }
            }
        }
        finally
        {
            DestroyConfig(config);
        }
    }

    [Test]
    public void FlatWalkWeight_PrefersStraight_AndDecreasesWithTurn()
    {
        // 2026-08-06 决策 ⑰ 修订：平坦区游走按转向惩罚加权（优先直行/缓弯）——
        // 均匀随机会让平地脊线蜷缩成团，宽度化后呈圆形"酱饼"（场景截图验收发现）。
        float straight = RidgeGenerator.ComputeFlatWalkWeight(1f, 0);
        float gentle = RidgeGenerator.ComputeFlatWalkWeight(1f, 1);
        float sharp = RidgeGenerator.ComputeFlatWalkWeight(1f, 2);
        Assert.AreEqual(1f, straight, 1e-6f, "直行权重 = exp(0) = 1");
        Assert.Greater(straight, gentle, "直行权重必须大于 60° 转弯");
        Assert.Greater(gentle, sharp, "60° 转弯权重必须大于 120° 转弯");
        Assert.AreEqual(straight, RidgeGenerator.ComputeFlatWalkWeight(0f, 2), 1e-6f,
            "转向惩罚为 0 时退化为均匀随机（兼容旧行为）");
    }

    [Test]
    public void Generate_FlatTerrain_RidgeStaysElongated()
    {
        // 平坦全图（高度恒定 ⇒ 全程走平坦游走分支）：端点距离必须接近路径长度，
        // 行为级锁定"转向权重 ⇒ 脊线延展"（均匀游走会频繁打转，端点距离显著偏短）。
        MountainConfigSO config = CreateConfig(ridgeCount: 3, minLen: 8, maxLen: 12);
        try
        {
            List<HexCellData> cells = BuildGrid(Width, Height, LandHeight);
            List<MountainRidgeData> ridges = RidgeGenerator.Generate(
                config, cells, c => GetGridNeighbors(cells, c), new System.Random(42));

            Assert.GreaterOrEqual(ridges.Count, 1, "平坦图上也应生成脊线");
            foreach (MountainRidgeData ridge in ridges)
            {
                HexCellData first = cells.First(c => c.HexCoordinate == ridge.ridgeHexes[0]);
                HexCellData last = cells.First(c => c.HexCoordinate == ridge.ridgeHexes[ridge.ridgeHexes.Count - 1]);
                int span = HexDistance(cells, first, last);
                Assert.GreaterOrEqual(span, ridge.length / 2,
                    $"脊线端点距离 {span} 不得显著短于路径长度 {ridge.length}（防脊线蜷缩成酱饼）");
            }
        }
        finally
        {
            DestroyConfig(config);
        }
    }

    [Test]
    public void Generate_SkipsWaterCells()
    {
        MountainConfigSO config = CreateConfig(ridgeCount: 3);
        try
        {
            List<HexCellData> cells = BuildGrid(Width, Height, LandHeight);
            // 中部矩形挖成水域
            for (int j = 6; j < 10; j++)
            {
                int offset = j / 2;
                for (int i = 6; i < 10; i++)
                {
                    var hex = new Vector3(i - offset, -(i - offset) - j, j);
                    cells.First(c => c.HexCoordinate == hex).Height = WaterHeight;
                }
            }

            RidgeGenerator.Generate(config, cells, c => GetGridNeighbors(cells, c), new System.Random(3));

            foreach (HexCellData cell in MountainCells(cells))
                Assert.IsFalse(WaterLevelConfig.IsWater(cell), "山格不得生成在水域（决策 ⑦）");
        }
        finally
        {
            DestroyConfig(config);
        }
    }

    [Test]
    public void Generate_RidgesDoNotOverlap()
    {
        MountainConfigSO config = CreateConfig(ridgeCount: 4, minLen: 5, maxLen: 10, minSpacing: 2);
        try
        {
            List<HexCellData> cells = BuildGrid(Width, Height, LandHeight);
            List<MountainRidgeData> ridges = RidgeGenerator.Generate(
                config, cells, c => GetGridNeighbors(cells, c), new System.Random(2024));

            Assert.GreaterOrEqual(ridges.Count, 2, "16x16 地图应能生成多条脊线");

            // 每个山格只属于一条脊线；总计数一致
            var ownerCount = new Dictionary<MountainRidgeData, int>();
            foreach (HexCellData cell in MountainCells(cells))
            {
                Assert.IsNotNull(cell.mountainRidge);
                ownerCount.TryGetValue(cell.mountainRidge, out int count);
                ownerCount[cell.mountainRidge] = count + 1;
            }
            Assert.AreEqual(ridges.Count, ownerCount.Count, "脊线数量与山格归属脊线数量一致（无重叠）");
            foreach (MountainRidgeData ridge in ridges)
                Assert.AreEqual(ridge.mountainCellCount, ownerCount[ridge], "mountainCellCount 必须与实际山格数一致");

            // 起点与更早山脉的最小距离 >= ridgeMinSpacing（禁粘连）
            var allCells = new List<HexCellData>(cells);
            for (int i = 1; i < ridges.Count; i++)
            {
                HexCellData startCell = allCells.First(c => c.mountainRidge == ridges[i]
                    && c.mountainRidgeStatus == Enums.MountainRidgeStatus.RidgeCell
                    && c.mountainPosAlongRidge < 0.5f);
                foreach (HexCellData earlier in allCells)
                {
                    if (earlier.mountainRidge != ridges[i - 1]) continue;
                    Assert.GreaterOrEqual(HexDistance(allCells, startCell, earlier), config.ridgeMinSpacing,
                        "新脊线起点不得进入已有山脉的禁区圈");
                }
            }
        }
        finally
        {
            DestroyConfig(config);
        }
    }

    [Test]
    public void PointToPolylineDistance_StraightLine_ReturnsDistanceAndArcLength()
    {
        var poly = new[] { new Vector2(0f, 0f), new Vector2(10f, 0f) };
        float d = RidgeGenerator.PointToPolylineDistance(new Vector2(5f, 3f), poly, out float arc);
        Assert.AreEqual(3f, d, 1e-5f);
        Assert.AreEqual(5f, arc, 1e-5f, "投影点弧长 = 起点到投影距离");

        float d2 = RidgeGenerator.PointToPolylineDistance(new Vector2(-2f, 4f), poly, out float arc2);
        Assert.AreEqual(Mathf.Sqrt(20f), d2, 1e-5f, "超出线段端点的投影应钳制在端点上");
        Assert.AreEqual(0f, arc2, 1e-5f, "钳制在起点时弧长为 0");
    }

    [Test]
    public void HMax_ScalesWithRidgeLength()
    {
        MountainConfigSO config = CreateConfig(ridgeCount: 1, minLen: 5, maxLen: 12);
        try
        {
            List<HexCellData> cells = BuildGrid(Width, Height, LandHeight);
            List<MountainRidgeData> ridges = RidgeGenerator.Generate(
                config, cells, c => GetGridNeighbors(cells, c), new System.Random(11));

            Assert.GreaterOrEqual(ridges.Count, 1);
            float expected = Mathf.Clamp(
                config.baseHeight + config.heightPerLength * (ridges[0].length - config.minRidgeLength),
                config.minHeight, config.maxHeight);
            Assert.AreEqual(expected, ridges[0].hMax, 1e-5f, "决策 ㉔：H_max = clamp(baseH + k·(len − minLen))");
            Assert.IsTrue(ridges[0].hMax > 0f);
        }
        finally
        {
            DestroyConfig(config);
        }
    }

    [Test]
    public void Generate_DebugComparison_SingleCellPlusStraightRidge()
    {
        // 2026-08-07 调试对照模式（MountainConfigSO.debugSingleCellAndStraightRidge）：
        // 绕过正常生成规律（数量/间距/评分行走/宽度化），同图生成 1 个孤立山脉地块
        // + 1 条严格占 debugStraightRidgeLength 格的直脊线。
        // 正常代码路径保留不删（既有测试全部继续覆盖）。
        MountainConfigSO config = CreateConfig();
        config.debugSingleCellAndStraightRidge = true;
        config.debugStraightRidgeLength = 8;
        try
        {
            List<HexCellData> cells = BuildGrid(Width, Height, LandHeight);
            List<MountainRidgeData> ridges = RidgeGenerator.Generate(
                config, cells, c => GetGridNeighbors(cells, c), new System.Random(42));

            Assert.AreEqual(2, ridges.Count, "调试对照 = 1 单格山 + 1 直脊线，恰好 2 条记录");

            // A：单个山脉地块——1 格脊线、无坡面格、方向字段为空
            MountainRidgeData singleRidge = ridges[0];
            Assert.AreEqual(1, singleRidge.length, "单格山脊线长度 = 1");
            Assert.AreEqual(1, singleRidge.mountainCellCount, "单格山不得有坡面格（path<2 时宽度化不生效）");
            HexCellData singleCell = cells.First(c => c.mountainRidge == singleRidge);
            Assert.AreEqual(Enums.MountainRidgeStatus.RidgeCell, singleCell.mountainRidgeStatus);
            Assert.AreEqual(Enums.HexDirection.None, singleCell.RidgeDirectionA);
            Assert.AreEqual(Enums.HexDirection.None, singleCell.RidgeDirectionB);
            Assert.AreEqual(0f, singleCell.mountainDistToRidge);

            // B：直脊线——长度 = 配置、方向恒定（直线）、不含宽度化坡面格
            MountainRidgeData straightRidge = ridges[1];
            Assert.AreEqual(config.debugStraightRidgeLength, straightRidge.length, "直脊线长度 = 调试配置");
            Assert.AreEqual(straightRidge.length, straightRidge.mountainCellCount,
                "调试直脊线不宽度化：占地格数必须严格等于配置长度");
            Assert.AreEqual(config.debugStraightRidgeLength + 1, MountainCells(cells).Count(),
                "调试地图总山格数 = 单格山 1 + 直脊线 n");
            Assert.IsFalse(cells.Any(c => c.mountainRidge == straightRidge
                && c.mountainRidgeStatus == Enums.MountainRidgeStatus.SlopeCell),
                "调试直脊线不得生成坡面格");
            Enums.HexDirection straightDir = RidgeGenerator.DirectionFromTo(
                straightRidge.ridgeHexes[0], straightRidge.ridgeHexes[1]);
            Assert.AreNotEqual(Enums.HexDirection.None, straightDir);
            for (int i = 1; i < straightRidge.ridgeHexes.Count - 1; i++)
            {
                Assert.AreEqual(straightDir,
                    RidgeGenerator.DirectionFromTo(straightRidge.ridgeHexes[i], straightRidge.ridgeHexes[i + 1]),
                    $"直脊线第 {i} 步方向必须恒定（直线）");
            }

            // 两座山体分离：B 起点与 A 的六边形距离 ≥ length+3（生成契约）
            HexCellData straightStart = cells.First(c => c.mountainRidge == straightRidge
                && c.mountainRidgeStatus == Enums.MountainRidgeStatus.RidgeCell
                && c.mountainPosAlongRidge < 0.5f);
            Assert.GreaterOrEqual(HexDistance(cells, singleCell, straightStart),
                config.debugStraightRidgeLength + 3, "B 起点必须与 A 保持距离（同图分离对照）");

            // 确定性：同 seed 重跑，山格集合一致（决策 ㉓）
            List<HexCellData> cellsRetry = BuildGrid(Width, Height, LandHeight);
            RidgeGenerator.Generate(config, cellsRetry, c => GetGridNeighbors(cellsRetry, c), new System.Random(42));
            Assert.IsTrue(
                new HashSet<Vector3>(MountainCells(cells).Select(c => c.HexCoordinate)).SetEquals(
                    MountainCells(cellsRetry).Select(c => c.HexCoordinate)),
                "调试对照同 seed 山格集合一致");
        }
        finally
        {
            DestroyConfig(config);
        }
    }

    [Test]
    public void Generate_HeightScale_MultipliesHMax_PathUnchanged()
    {
        // 2026-08-06 地图设置 SO 暴露高度缩放（MapGenerationConfigSO.mountainHeightScale）：
        // hMax = clamp 公式 × scale；缩放不消耗随机流 ⇒ 同 seed 脊线路径/山格集合不变（决策 ㉓）。
        MountainConfigSO config = CreateConfig(ridgeCount: 1, minLen: 5, maxLen: 12);
        try
        {
            List<HexCellData> cellsA = BuildGrid(Width, Height, LandHeight);
            List<HexCellData> cellsB = BuildGrid(Width, Height, LandHeight);
            List<MountainRidgeData> baseline = RidgeGenerator.Generate(
                config, cellsA, c => GetGridNeighbors(cellsA, c), new System.Random(11));
            List<MountainRidgeData> scaled = RidgeGenerator.Generate(
                config, cellsB, c => GetGridNeighbors(cellsB, c), new System.Random(11), heightScale: 2f);

            Assert.GreaterOrEqual(baseline.Count, 1);
            Assert.AreEqual(baseline.Count, scaled.Count, "缩放不改变脊线数量");
            for (int i = 0; i < baseline.Count; i++)
            {
                Assert.AreEqual(baseline[i].hMax * 2f, scaled[i].hMax, 1e-5f, "hMax = clamp 公式 × heightScale");
                Assert.AreEqual(baseline[i].length, scaled[i].length, "缩放不改变脊线长度");
                Assert.AreEqual(baseline[i].seed, scaled[i].seed, "缩放不改变随机流消耗（seed 一致）");
                for (int k = 0; k < baseline[i].ridgeHexes.Count; k++)
                    Assert.AreEqual(baseline[i].ridgeHexes[k], scaled[i].ridgeHexes[k], "缩放不改变脊线路径");
            }
        }
        finally
        {
            DestroyConfig(config);
        }
    }
}
