using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 探索服务实现：主动探索地块的核心逻辑。
/// 【探索重构-阶段3】新增服务，替代旧的自动探索逻辑。
/// 【探索重构-阶段7】接入金币系统：扣费 + 收割奖励。
/// 【统一开发入口】TryExplore 增加阵营参数：玩家与 AI 共用同一服务；
/// 校验（含中立校验）、扣费、归属写入在同一方法内同步完成，保证同时开发互斥。
/// </summary>
public class ExplorationService : IExplorationService
{
    private readonly IExplorationCostProvider _costProvider;
    private readonly GoldWallet _wallet;
    private readonly IExplorationRule _rule;
    private readonly MapVisualEventSO _mapVisualEvent;
    private readonly ITerritoryService _territoryService;
    private readonly ILogisticsService _logisticsService;
    private readonly MapResourceCollectionService _collectionService;
    private readonly EnemyModelManager _enemyModelManager;
    private readonly IMapInteractionGate _interactionGate;
    private readonly HashSet<HexCellData> _pendingCompletions = new HashSet<HexCellData>();

    public event Action<HexCellData> CellExplored;
    public event Action<HexCellData, int> ExplorationRewardTriggered;

    public ExplorationService(
        IExplorationCostProvider costProvider,
        GoldWallet wallet,
        IExplorationRule rule,
        MapVisualEventSO mapVisualEvent,
        ITerritoryService territoryService,
        ILogisticsService logisticsService,
        MapResourceCollectionService collectionService,
        EnemyModelManager enemyModelManager = null,
        [Zenject.InjectOptional] IMapInteractionGate interactionGate = null)
    {
        _costProvider = costProvider;
        _wallet = wallet;
        _rule = rule;
        _mapVisualEvent = mapVisualEvent;
        _territoryService = territoryService;
        _logisticsService = logisticsService;
        _collectionService = collectionService;
        _enemyModelManager = enemyModelManager;
        _interactionGate = interactionGate;
    }

    public ExploreResult TryExplore(HexCellData targetCell, int factionId)
    {
        if (targetCell == null || targetCell.IsExploredBy(factionId))
            return ExploreResult.AlreadyExplored;

        // 【动态地图-阶段二】交互锁：事务/动画期间受影响格禁止探索（§12.6）
        if (_interactionGate != null && _interactionGate.IsLocked(targetCell, MapInteractionType.Explore))
            return ExploreResult.Unexplorable;

        if (targetCell.HexType == Enums.HexType.LakeOrSea)
            return ExploreResult.AlreadyExplored;

        if (targetCell.IsUnexplorable)
            return ExploreResult.Unexplorable;

        GameObject occupant = targetCell.GetUnit();
        if (occupant != null)
        {
            bool isEnemy = factionId == 0
                ? occupant.CompareTag("EnemyUnit")
                : occupant.CompareTag("PlayerUnit");
            if (isEnemy)
                return ExploreResult.Unexplorable;
        }

        // 【统一开发入口-互斥核心】中立校验：地块已归属任意一方则失败。
        // 双方共用此服务后，先到者写入归属，后到者在此返回 NotNeutral 且不扣费（策划案 §26）。
        if (targetCell.Player_City_Index.Key != -1)
            return ExploreResult.NotNeutral;

        if (!_rule.IsValid(targetCell, factionId))
            return ExploreResult.NotAdjacent;

        var cost = _costProvider.GetCost(targetCell);
        if (!_wallet.TrySpendGold(factionId, cost.Amount))
            return ExploreResult.InsufficientResources;

        // 标记探索（按阵营）
        if (factionId == 0)
        {
            targetCell.ExploreThisHexCell();
            _pendingCompletions.Add(targetCell);
        }
        else
        {
            targetCell.ExploreBy(factionId);
        }

        // 取得归属（按阵营）
        if (factionId == 0)
        {
            _territoryService.Claim(targetCell);
        }
        else
        {
            ClaimForFaction(targetCell, factionId);
        }

        _logisticsService.RecalculateAll();

        // 收割（按阵营受益方）
        int reward = _collectionService.HarvestForGold(targetCell, factionId);
        Debug.Log($"[Exploration] 阵营 {factionId} 探索 {targetCell.HexCoordinate}，+{reward} Gold。");

        _mapVisualEvent?.Raise();
        ExplorationRewardTriggered?.Invoke(targetCell, factionId);

        // 探索动画事件仅玩家侧保留（避免 AI 开发触发玩家视角特效）
        if (factionId == 0)
            CellExplored?.Invoke(targetCell);

        return ExploreResult.Success;
    }

    /// <summary>
    /// AI 阵营取得归属：写入归属与势力范围字典（与旧 AIAutoExplorer 行为一致）。
    /// </summary>
    private void ClaimForFaction(HexCellData cell, int factionId)
    {
        if (_enemyModelManager == null) return;

        var cityKey = new KeyValuePair<int, int>(factionId, 0);
        cell.Player_City_Index = cityKey;

        if (_enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData.TryGetValue(factionId, out var sphere))
            sphere[cell.HexCoordinate] = cell;

        if (!_enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData.TryGetValue(cityKey, out var cityDict))
        {
            cityDict = new Dictionary<Vector3, HexCellData>();
            _enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData[cityKey] = cityDict;
        }
        cityDict[cell.HexCoordinate] = cell;
    }

    public void CompleteExploration(HexCellData targetCell)
    {
        _pendingCompletions.Remove(targetCell);
    }
}

/// <summary>
/// 邻接探索规则实现（A3 已确认）：只能探索相邻且属于己方连通领地的地块。
/// 【统一开发入口】IsValid 按阵营参数化。
/// </summary>
public class AdjacencyExplorationRule : IExplorationRule
{
    private readonly IMapDataService _mapDataService;
    private readonly ILogisticsService _logisticsService;

    public AdjacencyExplorationRule(IMapDataService mapDataService, ILogisticsService logisticsService)
    {
        _mapDataService = mapDataService;
        _logisticsService = logisticsService;
    }

    public bool IsValid(HexCellData targetCell, int factionId)
    {
        for (int i = 0; i < 6; i++)
        {
            var neighbor = _mapDataService.GetNeighbor(targetCell, (Enums.HexDirection)i);
            if (neighbor == null) continue;
            if (neighbor.Player_City_Index.Key != factionId) continue;
            if (_logisticsService == null || _logisticsService.IsLogisticsConnected(neighbor, factionId))
                return true;
        }
        return false;
    }
}
