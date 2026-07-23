using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 三态记忆迷雾 - 视野计算服务。
/// 每次己方行动/回合刷新时重算所有地块的 IsVisible（当前是否在己方视野内）：
///   1. 全格 IsVisible = false
///   2. 己方单位：以所在格为中心，按 ViewPoints 圈六边形 BFS 点亮
///   3. 己方领土（势力范围）：整片可见，并作为视野锚点点亮周边 CityViewRadius 圈
///   4. 首次点亮的格子若未探索则 ExploreThisHexCell()（第一次看到 = 永久探索）
/// 状态语义：未探索=!IsExplored；记忆区=IsExplored&&!IsVisible；可见=IsVisible。
/// </summary>
public class FieldOfViewService
{
    private readonly IMapDataService _mapDataService;
    private readonly IUnitRepository _unitRepository;
    private readonly PlayerModelManager _playerModelManager;

    private const int CityViewRadius = 1;

    private int[] _visitedGen;
    private int _currentGen;
    private List<HexCellData> _frontierA;
    private List<HexCellData> _frontierB;
    private List<HexCellData> _visibleBuffer;

    public FieldOfViewService(
        IMapDataService mapDataService,
        IUnitRepository unitRepository,
        PlayerModelManager playerModelManager)
    {
        _mapDataService = mapDataService;
        _unitRepository = unitRepository;
        _playerModelManager = playerModelManager;
    }

    private void EnsureBuffers()
    {
        if (_visitedGen != null) return;
        var all = _mapDataService.GetAllCells();
        if (all == null || all.Count == 0) return;
        int max = 0;
        foreach (var c in all)
            if (c.GenerateOrder > max) max = c.GenerateOrder;
        _visitedGen = new int[max + 1];
        _frontierA = new List<HexCellData>(64);
        _frontierB = new List<HexCellData>(64);
        _visibleBuffer = new List<HexCellData>(128);
    }

    /// <summary>
    /// 重算全图 IsVisible。两态迷雾：已探索 = 可见，未探索 = 不可见。
    /// </summary>
    public void Recompute()
    {
        var allCells = _mapDataService.GetAllCells();
        if (allCells == null) return;

        foreach (var cell in allCells)
            cell.IsVisible = cell.IsExplored;
    }

    private void RevealRing(HexCellData center, int radius)
    {
        _visibleBuffer.Clear();
        CollectVisibleCellsBuffered(center, radius, _visibleBuffer);
        foreach (var cell in _visibleBuffer)
            Reveal(cell);
    }

    private void CollectVisibleCellsBuffered(HexCellData center, int radius, List<HexCellData> into)
    {
        if (center == null || into == null) return;

        _frontierA.Clear();
        into.Add(center);
        _visitedGen[center.GenerateOrder] = _currentGen;
        _frontierA.Add(center);

        for (int ring = 0; ring < radius; ring++)
        {
            var next = (ring % 2 == 0) ? _frontierB : _frontierA;
            next.Clear();
            var frontier = (ring % 2 == 0) ? _frontierA : _frontierB;

            foreach (var cell in frontier)
            {
                var neighbors = _mapDataService.GetNeighbors(cell);
                if (neighbors == null) continue;
                foreach (var nb in neighbors)
                {
                    if (nb == null || _visitedGen[nb.GenerateOrder] == _currentGen) continue;
                    _visitedGen[nb.GenerateOrder] = _currentGen;
                    next.Add(nb);
                    into.Add(nb);
                }
            }

            if (next.Count == 0) break;
        }
    }

    public static void CollectVisibleCells(IMapDataService map, HexCellData center, int radius, HashSet<HexCellData> into)
    {
        if (map == null || center == null || into == null) return;

        var visited = new HashSet<HexCellData> { center };
        var frontier = new List<HexCellData> { center };
        into.Add(center);

        for (int ring = 0; ring < radius; ring++)
        {
            var next = new List<HexCellData>();
            foreach (var cell in frontier)
            {
                var neighbors = map.GetNeighbors(cell);
                if (neighbors == null) continue;
                foreach (var nb in neighbors)
                {
                    if (nb == null || visited.Contains(nb)) continue;
                    visited.Add(nb);
                    next.Add(nb);
                    into.Add(nb);
                }
            }
            frontier = next;
            if (frontier.Count == 0) break;
        }
    }

    private void Reveal(HexCellData cell)
    {
        cell.IsVisible = true;
        if (!cell.IsExplored)
            cell.ExploreThisHexCell();
    }
}
