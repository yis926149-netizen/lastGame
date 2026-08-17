using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 【金矿扎堆】簇生成规则纯函数测试。
/// 覆盖：固定堆数、目标格数预算、概率生长不规则性、水域/河流排除、
/// 堆心最小间距、同种子确定性、散落拦截（簇外掷中该地貌改写为空白）。
/// </summary>
public class LandFormClusterSpawnRuleTests
{
    private const int GridWidth = 15;
    private const int GridHeight = 15;
    private const float LandHeight = 2f;    // > WaterLevel，陆地
    private const float WaterHeight = 0.5f; // <= WaterLevel，水域

    [SetUp]
    public void SetUp()
    {
        WaterLevelConfig.WaterLevel = 1f;
    }

    [Test]
    public void FindClusterForm_ReturnsOnlyClusterEnabledForm()
    {
        var database = ScriptableObject.CreateInstance<MapLandFormDatabaseSO>();
        var provider = new MapLandFormProvider(database);
        var forest = ScriptableObject.CreateInstance<MapLandFormSO>();
        var goldMine = ScriptableObject.CreateInstance<MapLandFormSO>();
        goldMine.clusterSpawn = true;

        try
        {
            database.landForms.Add(forest);
            Assert.IsNull(LandFormClusterSpawnRule.FindClusterForm(provider), "无簇地貌应返回 null");

            database.landForms.Add(goldMine);
            Assert.AreSame(goldMine, LandFormClusterSpawnRule.FindClusterForm(provider));
        }
        finally
        {
            Object.DestroyImmediate(database);
            Object.DestroyImmediate(forest);
            Object.DestroyImmediate(goldMine);
        }
    }

    [Test]
    public void SelectCenters_FixedSuccessCount_RespectsMinSpacingAndEligibility()
    {
        List<HexCellData> cells = BuildGrid(GridWidth, GridHeight, LandHeight);
        cells[0].Height = WaterHeight; // 水域格不能被选为堆心
        MapLandFormSO form = CreateClusterForm(clusterCount: 5, targetSize: 4, fill: 1f, minSpacing: 4, maxRadius: 2);

        try
        {
            List<HexCellData> centers = LandFormClusterSpawnRule.SelectCenters(
                LegacyProvider(), form, cells, c => GetGridNeighbors(cells, c), new System.Random(42));

            Assert.AreEqual(5, centers.Count, "固定成功数");
            foreach (HexCellData center in centers)
                Assert.IsFalse(WaterLevelConfig.IsWater(center), "堆心不得为水域");

            for (int i = 0; i < centers.Count; i++)
            {
                for (int k = i + 1; k < centers.Count; k++)
                {
                    Assert.GreaterOrEqual(HexDistance(cells, centers[i], centers[k]), 4,
                        "任意两堆心间距不得小于 clusterMinSpacing");
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(form);
        }
    }

    [Test]
    public void GrowCluster_StopsAtTargetBudget_AndRespectsMaxRadius()
    {
        List<HexCellData> cells = BuildGrid(GridWidth, GridHeight, LandHeight);
        HexCellData center = cells[GridWidth * GridHeight / 2];
        MapLandFormSO form = CreateClusterForm(clusterCount: 1, targetSize: 8, fill: 1f, minSpacing: 1, maxRadius: 2);

        try
        {
            HashSet<HexCellData> cluster = LandFormClusterSpawnRule.GrowCluster(
                LegacyProvider(), form, center, c => GetGridNeighbors(cells, c), new System.Random(1));

            Assert.AreEqual(8, cluster.Count, "fill=1 时应精确长满预算");
            Assert.IsTrue(cluster.Contains(center), "簇必须包含堆心");
            foreach (HexCellData cell in cluster)
                Assert.LessOrEqual(HexDistance(cells, center, cell), 2, "簇内格不得超过最大半径");
        }
        finally
        {
            Object.DestroyImmediate(form);
        }
    }

    [Test]
    public void GrowCluster_SkipsWaterAndRiverCells()
    {
        List<HexCellData> cells = BuildGrid(GridWidth, GridHeight, LandHeight);
        HexCellData center = cells[GridWidth * GridHeight / 2];
        foreach (HexCellData neighbor in GetGridNeighbors(cells, center))
            neighbor.Height = WaterHeight; // 堆心邻居全部变水域
        MapLandFormSO form = CreateClusterForm(clusterCount: 1, targetSize: 100, fill: 1f, minSpacing: 1, maxRadius: 3);

        try
        {
            HashSet<HexCellData> cluster = LandFormClusterSpawnRule.GrowCluster(
                LegacyProvider(), form, center, c => GetGridNeighbors(cells, c), new System.Random(1));

            Assert.AreEqual(1, cluster.Count, "水域邻居不得被填充，堆只剩堆心");
            foreach (HexCellData cell in cluster)
                Assert.IsTrue(LandFormClusterSpawnRule.IsEligible(cell), "簇内不得出现水域/河流格");
        }
        finally
        {
            Object.DestroyImmediate(form);
        }
    }

    [Test]
    public void GrowCluster_LowFillProbability_IsSmallerThanFullHexagon()
    {
        List<HexCellData> cells = BuildGrid(GridWidth, GridHeight, LandHeight);
        HexCellData center = cells[GridWidth * GridHeight / 2];
        MapLandFormSO form = CreateClusterForm(clusterCount: 1, targetSize: 1000, fill: 0.3f, minSpacing: 1, maxRadius: 3);

        try
        {
            HashSet<HexCellData> cluster = LandFormClusterSpawnRule.GrowCluster(
                LegacyProvider(), form, center, c => GetGridNeighbors(cells, c), new System.Random(7));

            const int fullHexagonRadius3 = 1 + 6 + 12 + 18; // 37
            Assert.GreaterOrEqual(cluster.Count, 1);
            Assert.Less(cluster.Count, fullHexagonRadius3, "fill<1 时不可能长满整个六边形");
        }
        finally
        {
            Object.DestroyImmediate(form);
        }
    }

    [Test]
    public void PlaceClusters_ClaimsFixedNumberOfPiles_AndAppliesForm()
    {
        List<HexCellData> cells = BuildGrid(GridWidth, GridHeight, LandHeight);
        MapLandFormSO form = CreateClusterForm(clusterCount: 4, targetSize: 8, fill: 1f, minSpacing: 4, maxRadius: 3);

        try
        {
            HashSet<HexCellData> claimed = LandFormClusterSpawnRule.PlaceClusters(
                LegacyProvider(), form, cells, c => GetGridNeighbors(cells, c), new System.Random(42));

            Assert.AreEqual(4, CountPiles(cells, claimed), "必须生成固定 4 堆");
            foreach (HexCellData cell in claimed)
                Assert.AreSame(form, cell.landForm, "簇内格必须写回 form");
            foreach (HexCellData cell in cells)
                Assert.AreEqual(claimed.Contains(cell), cell.landForm == form, "只有簇内格持有 form");
        }
        finally
        {
            Object.DestroyImmediate(form);
        }
    }

    [Test]
    public void PlaceClusters_SameSeed_ProducesSameClusters()
    {
        List<HexCellData> cellsA = BuildGrid(GridWidth, GridHeight, LandHeight);
        List<HexCellData> cellsB = BuildGrid(GridWidth, GridHeight, LandHeight);
        MapLandFormSO form = CreateClusterForm(clusterCount: 3, targetSize: 6, fill: 0.7f, minSpacing: 3, maxRadius: 3);

        try
        {
            HashSet<HexCellData> a = LandFormClusterSpawnRule.PlaceClusters(
                LegacyProvider(), form, cellsA, c => GetGridNeighbors(cellsA, c), new System.Random(123));
            HashSet<HexCellData> b = LandFormClusterSpawnRule.PlaceClusters(
                LegacyProvider(), form, cellsB, c => GetGridNeighbors(cellsB, c), new System.Random(123));

            var coordsA = new HashSet<Vector3>(a.Select(c => c.HexCoordinate));
            var coordsB = new HashSet<Vector3>(b.Select(c => c.HexCoordinate));
            Assert.AreEqual(a.Count, b.Count, "同种子簇格数一致");
            Assert.IsTrue(coordsA.SetEquals(coordsB), "同种子簇格坐标一致");
        }
        finally
        {
            Object.DestroyImmediate(form);
        }
    }

    [Test]
    public void RemoveScatteredForm_ClearsOnlyScatteredCellsOutsideClusters()
    {
        List<HexCellData> cells = BuildGrid(GridWidth, GridHeight, LandHeight);
        MapLandFormSO form = CreateClusterForm(clusterCount: 2, targetSize: 5, fill: 1f, minSpacing: 3, maxRadius: 2);

        try
        {
            HashSet<HexCellData> claimed = LandFormClusterSpawnRule.PlaceClusters(
                LegacyProvider(), form, cells, c => GetGridNeighbors(cells, c), new System.Random(5));
            HexCellData scattered = cells.First(c => !claimed.Contains(c));
            scattered.landForm = form; // 模拟散落掷中金矿但不在堆内

            LandFormClusterSpawnRule.RemoveScatteredForm(form, cells, claimed);

            Assert.IsNull(scattered.landForm, "簇外散落金矿必须改写为空白");
            foreach (HexCellData cell in claimed)
                Assert.AreSame(form, cell.landForm, "簇内格不受拦截影响");
        }
        finally
        {
            Object.DestroyImmediate(form);
        }
    }

    // ── 测试辅助 ──────────────────────────────

    // 簇方法只读地貌的 Legacy 字段（无 Excel balance 时 Provider 直接回退 form），
    // 故用空库 Provider 即可驱动 SelectCenters/GrowCluster/PlaceClusters。
    private static MapLandFormProvider LegacyProvider() => new MapLandFormProvider(null);

    private static MapLandFormSO CreateClusterForm(int clusterCount, int targetSize, float fill, int minSpacing, int maxRadius)
    {
        var form = ScriptableObject.CreateInstance<MapLandFormSO>();
        form.clusterSpawn = true;
        form.clusterCount = clusterCount;
        form.clusterTargetSize = targetSize;
        form.clusterFillProbability = fill;
        form.clusterMinSpacing = minSpacing;
        form.clusterMaxRadius = maxRadius;
        return form;
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
                cells.Add(new HexCellData(Enums.HexType.NoRiver, order++, hex, Vector3.zero, heightValue));
            }
        }
        return cells;
    }

    private static List<HexCellData> GetGridNeighbors(List<HexCellData> cells, HexCellData cell)
    {
        // 方向偏移与 HexMapService.GetNeighbor / HexCellData.GetAttackerSlotDirection 一致
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

    private static int CountPiles(List<HexCellData> cells, HashSet<HexCellData> claimed)
    {
        int piles = 0;
        var visited = new HashSet<HexCellData>();
        foreach (HexCellData start in claimed)
        {
            if (visited.Contains(start)) continue;

            piles++;
            var queue = new Queue<HexCellData>();
            queue.Enqueue(start);
            visited.Add(start);
            while (queue.Count > 0)
            {
                HexCellData current = queue.Dequeue();
                foreach (HexCellData neighbor in GetGridNeighbors(cells, current))
                {
                    if (!claimed.Contains(neighbor) || visited.Contains(neighbor)) continue;
                    visited.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }
        }
        return piles;
    }
}
