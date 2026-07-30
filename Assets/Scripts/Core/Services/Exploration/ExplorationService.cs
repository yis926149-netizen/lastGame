using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 探索服务实现：主动探索地块的核心逻辑。
/// 【探索重构-阶段3】新增服务，替代旧的自动探索逻辑。
/// 【探索重构-阶段7】接入金币系统：扣费 + 收割奖励。
/// </summary>
public class ExplorationService : IExplorationService
{
    private readonly IExplorationCostProvider _costProvider;
    private readonly IPlayerResourceWallet _wallet;
    private readonly IExplorationRule _rule;
    private readonly MapVisualEventSO _mapVisualEvent;
    private readonly ITerritoryService _territoryService;
    private readonly GoldWallet _goldWallet;
    private readonly ILogisticsService _logisticsService;
    private readonly HashSet<HexCellData> _pendingCompletions = new HashSet<HexCellData>();

    public event Action<HexCellData> CellExplored;
    public event Action<HexCellData> ExplorationRewardTriggered;

    public ExplorationService(
        IExplorationCostProvider costProvider,
        IPlayerResourceWallet wallet,
        IExplorationRule rule,
        MapVisualEventSO mapVisualEvent,
        ITerritoryService territoryService,
        GoldWallet goldWallet,
        ILogisticsService logisticsService)
    {
        _costProvider = costProvider;
        _wallet = wallet;
        _rule = rule;
        _mapVisualEvent = mapVisualEvent;
        _territoryService = territoryService;
        _goldWallet = goldWallet;
        _logisticsService = logisticsService;
    }

    public ExploreResult TryExplore(HexCellData targetCell)
    {
        if (targetCell == null || targetCell.IsExplored)
            return ExploreResult.AlreadyExplored;

        if (targetCell.HexType == Enums.HexType.LakeOrSea)
            return ExploreResult.AlreadyExplored;

        if (targetCell.IsUnexplorable)
            return ExploreResult.Unexplorable;

        GameObject occupant = targetCell.GetUnit();
        if (occupant != null && occupant.CompareTag("EnemyUnit"))
            return ExploreResult.Unexplorable;

        if (!_rule.IsValid(targetCell))
            return ExploreResult.NotAdjacent;

        var cost = _costProvider.GetCost(targetCell);
        if (!_wallet.TrySpend(cost))
            return ExploreResult.InsufficientResources;

        targetCell.ExploreThisHexCell();
        _pendingCompletions.Add(targetCell);

        _territoryService.Claim(targetCell);
        _logisticsService.RecalculateAll();
        HarvestAndReward(targetCell);
        _mapVisualEvent?.Raise();
        ExplorationRewardTriggered?.Invoke(targetCell);

        CellExplored?.Invoke(targetCell);

        return ExploreResult.Success;
    }

    public void CompleteExploration(HexCellData targetCell)
    {
        _pendingCompletions.Remove(targetCell);
    }

    /// <summary>
    /// 收割地块资源，转换为金币奖励。
    /// 基础奖励 5 Gold，资源地块额外奖励。
    /// </summary>
    private void HarvestAndReward(HexCellData cell)
    {
        if (cell == null) return;

        int reward = 5; // 基础探索奖励

        var resource = cell.GetResource();
        switch (resource)
        {
            case Enums.ResourceType.Animals:   reward += 20; break;
            case Enums.ResourceType.Plants:    reward += 15; break;
            case Enums.ResourceType.Minerals:  reward += 25; break;
            case Enums.ResourceType.Chest:     reward += 30; break;
            case Enums.ResourceType.HealthPack: reward += 10; break;
        }

        // 清除地块资源（已收割）
        if (resource != Enums.ResourceType.None)
        {
            cell.ReapResource();
            if (cell.resourceModel != null)
            {
            UnityEngine.Object.Destroy(cell.resourceModel);
                cell.resourceModel = null;
            }
        }

        _goldWallet.AddGold(0, reward);
        Debug.Log($"[Exploration] 探索 {cell.HexCoordinate}，收割 {resource}，+{reward} Gold。余额 {_goldWallet.Gold}");
    }
}

/// <summary>
/// 邻接探索规则实现（A3 已确认）：只能探索相邻已探索格
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

    public bool IsValid(HexCellData targetCell)
    {
        for (int i = 0; i < 6; i++)
        {
            var neighbor = _mapDataService.GetNeighbor(targetCell, (Enums.HexDirection)i);
            if (neighbor == null) continue;
            if (neighbor.Player_City_Index.Key != 0) continue;
            if (_logisticsService == null || _logisticsService.IsLogisticsConnected(neighbor, 0))
                return true;
        }
        return false;
    }
}
