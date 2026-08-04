using System.Collections.Generic;
using UnityEngine;

//****************************************
// 【断供方案-阶段4】区域吞并：断供区域与吞并方后勤网络共边相邻即整体易主。
// 判定：仅阵营 0/1；区域不含主城格；批量写入归属（禁止逐格 TransferOwner/SetOwner，
// 其内部重算会嵌套 BFS）；建筑随格易主（公共建筑走 OnCaptured 全量，含外一环，决策 11）。
// 由 LogisticsService.RecalculateAll 尾部调用；吞并后统一一次重算、单次 LogisticsChanged。
//****************************************

public sealed class AnnexationService
{
    private readonly IMapDataService _mapDataService;

    public AnnexationService(IMapDataService mapDataService)
    {
        _mapDataService = mapDataService;
    }

    /// <summary>
    /// 扫描全部断供区域并执行吞并。
    /// </summary>
    /// <returns>是否发生迁移（调用方负责领地字典重建与统一重算）</returns>
    public bool TryAnnex(
        Dictionary<int, HashSet<Vector3>> connectedCells,
        Dictionary<int, HexCellData> mainCityRoots)
    {
        bool any = false;
        var processedBuildings = new HashSet<GameObject>();

        foreach (int f in new[] { 0, 1 }) // 吞并方（仅阵营 0/1，决策 10）
        {
            if (!connectedCells.TryGetValue(f, out var fConnected)) continue;

            foreach (int g in new[] { 0, 1 }) // 断供方
            {
                if (g == f) continue;
                if (!connectedCells.TryGetValue(g, out var gConnected)) continue;

                if (AnnexRegionsOf(f, g, fConnected, gConnected, mainCityRoots, processedBuildings))
                    any = true;
            }
        }

        return any;
    }

    private bool AnnexRegionsOf(
        int f,
        int g,
        HashSet<Vector3> fConnected,
        HashSet<Vector3> gConnected,
        Dictionary<int, HexCellData> mainCityRoots,
        HashSet<GameObject> processedBuildings)
    {
        // 断供格 = 归属 g 且不在 g 后勤网络内（Key>=2 的中立公共建筑格天然不在 0/1 断供集合）
        var unsupplied = new List<HexCellData>();
        foreach (var cell in _mapDataService.GetAllCells())
        {
            if (cell == null) continue;
            if (cell.Player_City_Index.Key != g) continue;
            if (!gConnected.Contains(cell.HexCoordinate)) unsupplied.Add(cell);
        }

        var visited = new HashSet<Vector3>();
        bool any = false;

        foreach (var seed in unsupplied)
        {
            if (!visited.Add(seed.HexCoordinate)) continue;

            var region = FloodFillRegion(seed, g, gConnected, visited);
            if (region == null) continue;

            if (ContainsMainCity(region, mainCityRoots)) continue;
            if (!AdjacentToNetwork(region, fConnected)) continue;

            AnnexRegion(region, f, processedBuildings);
            any = true;
        }

        return any;
    }

    /// <summary>
    /// 6 邻域 flood-fill：只包含"归属 g 且断供"的格。
    /// 中立格（Key<0）、公共建筑伪阵营格（Key>=2）、g 的已连通格都会截断连通分量。
    /// </summary>
    private List<HexCellData> FloodFillRegion(
        HexCellData seed,
        int g,
        HashSet<Vector3> gConnected,
        HashSet<Vector3> visited)
    {
        var region = new List<HexCellData>();
        var queue = new Queue<HexCellData>();
        queue.Enqueue(seed);
        region.Add(seed);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            for (int i = 0; i < 6; i++)
            {
                var neighbor = _mapDataService.GetNeighbor(current, (Enums.HexDirection)i);
                if (neighbor == null || !visited.Add(neighbor.HexCoordinate)) continue;
                if (neighbor.Player_City_Index.Key != g) continue;
                if (gConnected.Contains(neighbor.HexCoordinate)) continue;
                region.Add(neighbor);
                queue.Enqueue(neighbor);
            }
        }

        return region;
    }

    /// <summary>防御性检查：区域不含任何主城格（主城连通分量恒有供应，正常不会命中）</summary>
    private static bool ContainsMainCity(List<HexCellData> region, Dictionary<int, HexCellData> mainCityRoots)
    {
        if (region == null || region.Count == 0 || mainCityRoots == null) return false;
        foreach (var root in mainCityRoots.Values)
        {
            if (root == null) continue;
            foreach (var cell in region)
                if (cell.HexCoordinate == root.HexCoordinate) return true;
        }
        return false;
    }

    /// <summary>区域边界（任意一格）与吞并方后勤网络共边相邻</summary>
    private bool AdjacentToNetwork(List<HexCellData> region, HashSet<Vector3> fConnected)
    {
        if (region == null || fConnected == null) return false;
        foreach (var cell in region)
        {
            for (int i = 0; i < 6; i++)
            {
                var neighbor = _mapDataService.GetNeighbor(cell, (Enums.HexDirection)i);
                if (neighbor != null && fConnected.Contains(neighbor.HexCoordinate)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 整区域原子易主给 f：批量写入归属（Key=(f,0) + ExploreBy），
    /// 建筑随格易主（多格公共建筑只处理一次，走 OnCaptured 全量含外一环）。
    /// </summary>
    private static void AnnexRegion(List<HexCellData> region, int f, HashSet<GameObject> processedBuildings)
    {
        foreach (var cell in region)
        {
            // 【防递归】禁止调用 TransferOwner/SetOwner（内部重算 → 嵌套 BFS，TerritoryService.cs:88）
            cell.Player_City_Index = new KeyValuePair<int, int>(f, 0);
            cell.ExploreBy(f);

            var entry = cell.BulidingTypeOnHex_Building;
            if (entry.Key == Enums.BulidingType.NoBuilding || entry.Value == null) continue;
            if (!processedBuildings.Add(entry.Value)) continue;

            BuildingTransferService.TransferBuilding(entry.Value, f, triggerRecalculate: false);
        }
    }
}
