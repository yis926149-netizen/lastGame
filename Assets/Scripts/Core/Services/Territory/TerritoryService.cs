using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 势力范围服务实现：基于领地集合的新模型。
/// 【探索重构-阶段5.5】替代旧的"城市圈"模型。
/// 势力范围 = 主城固有范围（初始化时圈入）+ 探索占领 + 公共建筑占领
/// </summary>
public class TerritoryService : ITerritoryService
{
    private readonly IMapDataService _mapDataService;
    private readonly PlayerModelManager _playerModelManager;
    private readonly MapVisualEventSO _mapVisualEvent;

    // 玩家主城 KeyValuePair（0,0），用于 Player_City_Index 赋值
    private static readonly KeyValuePair<int, int> PlayerOwner = new KeyValuePair<int, int>(0, 0);

    public TerritoryService(
        IMapDataService mapDataService,
        PlayerModelManager playerModelManager,
        MapVisualEventSO mapVisualEvent)
    {
        _mapDataService = mapDataService;
        _playerModelManager = playerModelManager;
        _mapVisualEvent = mapVisualEvent;
    }

    /// <summary>
    /// 将地块圈入玩家势力范围。
    /// 同时同步到 PlayerModelManager.SphereOfInfluence_HexC_HexCellData（兼容 SphereOfInfluenceRenderer）。
    /// </summary>
    public void Claim(HexCellData cell)
    {
        if (cell == null) return;
        var coord = cell.HexCoordinate;

        cell.Player_City_Index = PlayerOwner;

        _playerModelManager.SphereOfInfluence_HexC_HexCellData[coord] = cell;

        if (!_playerModelManager.SingleCity_SphereOfInfluence_HexC_HexCellData.TryGetValue(0, out var cityDict))
        {
            cityDict = new Dictionary<Vector3, HexCellData>();
            _playerModelManager.SingleCity_SphereOfInfluence_HexC_HexCellData[0] = cityDict;
        }
        cityDict[coord] = cell;
    }

    /// <summary>
    /// 检查地块是否在玩家势力范围内。
    /// </summary>
    public bool IsInPlayerTerritory(HexCellData cell)
    {
        if (cell == null) return false;
        // 主城标记：PlayerIndex == 0
        return cell.Player_City_Index.Key == 0;
    }
}

public sealed class LogisticsService : ILogisticsService
{
    private readonly IMapDataService _mapDataService;
    private readonly Dictionary<int, HexCellData> _mainCityRoots =
        new Dictionary<int, HexCellData>();
    private readonly Dictionary<int, HashSet<Vector3>> _connectedCells =
        new Dictionary<int, HashSet<Vector3>>();

    public event System.Action LogisticsChanged;

    public int Version { get; private set; }

    public LogisticsService(IMapDataService mapDataService)
    {
        _mapDataService = mapDataService;
    }

    public void RegisterMainCity(int factionId, HexCellData rootCell)
    {
        if (factionId < 0 || rootCell == null) return;
        _mainCityRoots[factionId] = rootCell;
    }

    public void SetOwner(HexCellData cell, int factionId)
    {
        if (!CanOwn(cell, factionId)) return;
        cell.Player_City_Index = new KeyValuePair<int, int>(factionId, 0);
        cell.ExploreBy(factionId);
        RecalculateAll();
    }

    public void TransferOwner(HexCellData cell, int factionId)
    {
        SetOwner(cell, factionId);
    }

    public void ClearOwner(HexCellData cell)
    {
        if (cell == null) return;
        cell.Player_City_Index = new KeyValuePair<int, int>(-1, -1);
        RecalculateAll();
    }

    public void RecalculateAll()
    {
        _connectedCells.Clear();

        foreach (var entry in _mainCityRoots)
        {
            int factionId = entry.Key;
            HexCellData root = entry.Value;
            var connected = new HashSet<Vector3>();
            _connectedCells[factionId] = connected;

            if (!CanTraverse(root, factionId)) continue;

            var queue = new Queue<HexCellData>();
            connected.Add(root.HexCoordinate);
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                HexCellData current = queue.Dequeue();
                for (int direction = 0; direction < 6; direction++)
                {
                    HexCellData neighbor = _mapDataService.GetNeighbor(
                        current,
                        (Enums.HexDirection)direction);
                    if (!CanTraverse(neighbor, factionId)) continue;
                    if (!connected.Add(neighbor.HexCoordinate)) continue;
                    queue.Enqueue(neighbor);
                }
            }
        }

        Version++;
        LogisticsChanged?.Invoke();
    }

    public bool IsOwnedBy(HexCellData cell, int factionId)
    {
        return cell != null && cell.Player_City_Index.Key == factionId;
    }

    public bool IsLogisticsConnected(HexCellData cell, int factionId)
    {
        return cell != null &&
               _connectedCells.TryGetValue(factionId, out var connected) &&
               connected.Contains(cell.HexCoordinate);
    }

    public bool IsExploredByFaction(HexCellData cell, int factionId)
    {
        return cell != null && cell.IsExploredBy(factionId);
    }

    public bool IsVisibleToFaction(HexCellData cell, int viewerFactionId)
    {
        if (cell == null) return false;

        int ownerFactionId = cell.Player_City_Index.Key;
        if (ownerFactionId >= 0)
        {
            return IsExploredByFaction(cell, ownerFactionId) &&
                   IsLogisticsConnected(cell, ownerFactionId);
        }

        return IsExploredByFaction(cell, viewerFactionId);
    }

    private static bool CanOwn(HexCellData cell, int factionId)
    {
        return factionId >= 0 &&
               cell != null &&
               cell.HexType != Enums.HexType.LakeOrSea;
    }

    private static bool CanTraverse(HexCellData cell, int factionId)
    {
        return CanOwn(cell, factionId) && cell.Player_City_Index.Key == factionId;
    }
}
