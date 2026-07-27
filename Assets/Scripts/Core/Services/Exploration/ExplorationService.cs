using System;
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

    public event Action<HexCellData> CellExplored;
    public event Action<HexCellData> ExplorationRewardTriggered;

    public ExplorationService(
        IExplorationCostProvider costProvider,
        IPlayerResourceWallet wallet,
        IExplorationRule rule,
        MapVisualEventSO mapVisualEvent,
        ITerritoryService territoryService,
        GoldWallet goldWallet)
    {
        _costProvider = costProvider;
        _wallet = wallet;
        _rule = rule;
        _mapVisualEvent = mapVisualEvent;
        _territoryService = territoryService;
        _goldWallet = goldWallet;
    }

    public ExploreResult TryExplore(HexCellData targetCell)
    {
        // 0. 水域不可探索
        if (targetCell.HexType == Enums.HexType.LakeOrSea)
            return ExploreResult.AlreadyExplored;

        // 1. 基础校验：格子存在且未探索
        if (targetCell == null || targetCell.IsExplored)
            return ExploreResult.AlreadyExplored;

        // 公共建筑系统：公共建筑占位格+周围一环不可探索
        if (targetCell.IsUnexplorable)
            return ExploreResult.Unexplorable;

        // 2. 规则校验：邻接规则等
        if (!_rule.IsValid(targetCell))
            return ExploreResult.NotAdjacent;

        // 3. 成本计算与资源扣费
        var cost = _costProvider.GetCost(targetCell);
        if (!_wallet.TrySpend(cost))
            return ExploreResult.InsufficientResources;

        // 4. 执行探索：标记已探索
        targetCell.ExploreThisHexCell();

        // 5-9. 后续逻辑（领土/收割/视觉刷新）推迟到动画结束后由 CompleteExploration 执行
        // 触发事件：柱体特效等动画挂在此事件
        CellExplored?.Invoke(targetCell);

        return ExploreResult.Success;
    }

    public void CompleteExploration(HexCellData targetCell)
    {
        // 5. 圈入势力范围（探索 = 占领）
        _territoryService.Claim(targetCell);

        // 6. 收割资源：地块资源转换为金币（探索即收割）
        HarvestAndReward(targetCell);

        // 7. 触发地图视觉刷新
        _mapVisualEvent?.Raise();

        // 8. 触发探索随机奖励（金币 + 单位），由奖励系统订阅
        ExplorationRewardTriggered?.Invoke(targetCell);
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

    public AdjacencyExplorationRule(IMapDataService mapDataService)
    {
        _mapDataService = mapDataService;
    }

    public bool IsValid(HexCellData targetCell)
    {
        // 必须邻接至少一个已探索格
        for (int i = 0; i < 6; i++)
        {
            var neighbor = _mapDataService.GetNeighbor(targetCell, (Enums.HexDirection)i);
            if (neighbor != null && neighbor.IsExplored)
                return true;
        }
        return false;
    }
}
