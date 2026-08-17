using UnityEngine;
using System.Collections.Generic;
using Zenject;

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

    // 【断供方案-阶段1/§4.1】领地字典一律从地块归属重建，禁止手工维护（会漂移）。
    // 可选属性注入：测试直接 new 时不注入，跳过重建。
    [Inject(Optional = true)] public PlayerModelManager PlayerModelManager { get; set; }
    [Inject(Optional = true)] public EnemyModelManager EnemyModelManager { get; set; }

    public event System.Action LogisticsChanged;

    public int Version { get; private set; }

    private readonly AnnexationService _annexation;
    private int _recalcDepth;
    private readonly GameFlowConfigProvider _gameFlow;

    public LogisticsService(IMapDataService mapDataService, GameFlowConfigProvider gameFlow = null)
    {
        _mapDataService = mapDataService;
        _annexation = new AnnexationService(mapDataService);
        _gameFlow = gameFlow;
    }

    /// <summary>吞并重算递归深度上限。Excel 优先，缺失回退 3。</summary>
    private int MaxAnnexationRecalcDepth => _gameFlow?.AnnexationRecalcDepth ?? 3;

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

        // 【断供方案-阶段4】区域吞并：断供区与敌方后勤网络相邻即整体易主
        if (_annexation.TryAnnex(_connectedCells, _mainCityRoots))
        {
            // 归属批量变化（含公共建筑外一环）后领地字典从地块重建（§4.1）
            RebuildSphereDictionaries();

            if (_recalcDepth < MaxAnnexationRecalcDepth)
            {
                // 吞并后状态已稳定：仅 1 次尾递归；事件由最内层"无迁移"的那一次触发
                _recalcDepth++;
                RecalculateAll();
                _recalcDepth--;
                return;
            }
        }

        RebuildSphereDictionaries();
        LogisticsChanged?.Invoke();
    }

    // 【断供方案-阶段1/§4.1】从地块归属重建双方领地字典（含公共建筑伪阵营），
    // 覆盖探索/占领/吞并/公共建筑易主所有归属变化路径。
    private void RebuildSphereDictionaries()
    {
        if (PlayerModelManager != null)
            PlayerModelManager.RebuildSphereOfInfluence();

        if (EnemyModelManager != null)
        {
            List<int> factionIds = new List<int>(EnemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData.Keys);
            foreach (int factionId in factionIds)
                EnemyModelManager.RebuildSphereOfInfluence(factionId);
        }
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
        // 【断供方案-阶段1/A7】中立（Key < 0）与公共建筑伪阵营（Key >= 2）按观察方
        // 永久发现状态判断（后勤方案 §2.3）；仅阵营 0/1 的已归属格按归属方探索+供应判断。
        if (ownerFactionId < 0 || ownerFactionId >= 2)
            return IsExploredByFaction(cell, viewerFactionId);

        return IsExploredByFaction(cell, ownerFactionId) &&
               IsLogisticsConnected(cell, ownerFactionId);
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
