using System;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

/// <summary>
/// 探索服务实现：主动探索地块的核心逻辑。
/// 【探索重构-阶段3】新增服务，替代旧的自动探索逻辑。
/// 【探索重构-阶段7】接入金币系统：扣费 + 收割奖励。
/// 【统一开发入口】TryExplore 增加阵营参数：玩家与 AI 共用同一服务；
/// 校验（含中立校验）、扣费、归属写入在同一方法内同步完成，保证同时开发互斥。
/// 【探索结果纯广播】服务只负责探索事务与阶段推进：
///  - TryExplore 唯一消费奖励快照并发布 Explored；
///  - 订阅 Settled 缓存玩家实际结算结果；
///  - SignalRewardPoint 在动画奖励点（或超时兜底）发布 RewardPoint。
/// </summary>
public class ExplorationService : IExplorationService, ITickable, IDisposable
{
    private const float PendingTimeoutSeconds = 10f;

    private readonly IExplorationCostProvider _costProvider;
    private readonly GoldWallet _wallet;
    private readonly IExplorationRule _rule;
    private readonly MapVisualEventSO _mapVisualEvent;
    private readonly ITerritoryService _territoryService;
    private readonly ILogisticsService _logisticsService;
    private readonly MapResourceCollectionService _collectionService;
    private readonly EnemyModelManager _enemyModelManager;
    private readonly IMapInteractionGate _interactionGate;
    private readonly IExplorationBroadcastSource _broadcastSource;
    private readonly IExplorationBroadcastPublisher _broadcastPublisher;
    private readonly Func<float> _timeProvider;

    private readonly Dictionary<HexCellData, PlayerPending> _pending = new Dictionary<HexCellData, PlayerPending>();

    public ExplorationService(
        IExplorationCostProvider costProvider,
        GoldWallet wallet,
        IExplorationRule rule,
        MapVisualEventSO mapVisualEvent,
        ITerritoryService territoryService,
        ILogisticsService logisticsService,
        MapResourceCollectionService collectionService,
        IExplorationBroadcastSource broadcastSource,
        IExplorationBroadcastPublisher broadcastPublisher,
        EnemyModelManager enemyModelManager = null,
        [InjectOptional] IMapInteractionGate interactionGate = null,
        [InjectOptional] Func<float> timeProvider = null)
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
        _broadcastSource = broadcastSource;
        _broadcastPublisher = broadcastPublisher;
        _timeProvider = timeProvider ?? (() => Time.realtimeSinceStartup);

        _broadcastSource.Broadcast += OnBroadcast;
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

        // 【多单位落点】枚举格内全部站位单位做敌方判定。
        foreach (GameObject occupant in targetCell.GetStandingUnits())
        {
            if (occupant == null) continue;
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
        int harvestReward = _collectionService.HarvestForGold(targetCell, factionId);
        Debug.Log($"[Exploration] 阵营 {factionId} 探索 {targetCell.HexCoordinate}，+{harvestReward} Gold。");

        // 唯一消费奖励快照：必须发生在 _mapVisualEvent.Raise() 与任何探索广播之前，
        // 确保外部监听器无法抢先消费 cell 上的奖励快照。
        ExplorationRewardData rewardSnapshot = targetCell.TakeExplorationReward();
        if (rewardSnapshot == null)
        {
            Debug.LogError($"[Exploration] 地块 {targetCell.HexCoordinate} 缺少预生成奖励快照（factionId={factionId}），按缺失快照处理。");
        }
        else if (factionId == 0)
        {
            Debug.Log($"[RewardTrace] TakeSnapshot cell={targetCell.HexCoordinate} type={rewardSnapshot.RewardType} gold={rewardSnapshot.GoldAmount} units={(rewardSnapshot.UnitConfigs?.Length ?? -1)} card={(rewardSnapshot.TacticalCard == null ? "NULL" : rewardSnapshot.TacticalCard.cardId)} building={(rewardSnapshot.BuildingConfig == null ? "NULL" : rewardSnapshot.BuildingConfig.buildingId.ToString())}");
        }

        ExplorationAcquisition explored = ExplorationAcquisition.Explored(targetCell, factionId, rewardSnapshot);
        if (factionId == 0)
        {
            Debug.Log($"[RewardTrace] Explored cell={targetCell.HexCoordinate} type={explored.OriginalRewardType} gold={explored.OriginalGoldAmount} units={(explored.UnitConfigs?.Count ?? -1)} card={(explored.TacticalCard == null ? "NULL" : explored.TacticalCard.cardId)} building={(explored.BuildingConfig == null ? "NULL" : explored.BuildingConfig.buildingId.ToString())}");
        }

        // 玩家侧在 Explored 广播前建立 pending，防止同步广播期间状态遗漏。
        if (factionId == 0)
        {
            _pending[targetCell] = new PlayerPending
            {
                ExploredPayload = explored,
                EstablishedAt = _timeProvider(),
            };
        }

        _mapVisualEvent?.Raise();
        _broadcastPublisher.Publish(explored);

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

    /// <summary>动画奖励点回调：幂等发布 RewardPoint；Settled 未到则先标记等待。</summary>
    public void SignalRewardPoint(HexCellData targetCell)
    {
        if (targetCell == null)
        {
            Debug.LogWarning("[Exploration] SignalRewardPoint(null) 被调用，忽略。");
            return;
        }

        if (!_pending.TryGetValue(targetCell, out PlayerPending pending))
            return;
        if (pending.RewardPointEmitted)
            return;

        if (pending.SettledPayload == null)
        {
            // 奖励点异常早于 Settled：先标记，待 Settled 到达后再发布实际结算结果。
            pending.RewardPointRequested = true;
            return;
        }

        EmitRewardPoint(targetCell, pending);
    }

    public void Tick()
    {
        if (_pending.Count == 0)
            return;

        var expired = new List<HexCellData>();
        foreach (KeyValuePair<HexCellData, PlayerPending> kv in _pending)
        {
            if (_timeProvider() - kv.Value.EstablishedAt > PendingTimeoutSeconds)
                expired.Add(kv.Key);
        }

        foreach (HexCellData cell in expired)
        {
            if (!_pending.TryGetValue(cell, out PlayerPending pending))
                continue;
            if (pending.RewardPointEmitted)
                continue;

            _pending.Remove(cell);
            pending.RewardPointEmitted = true;

            if (pending.SettledPayload != null)
            {
                // 动画中断兜底：使用已缓存的实际结算结果发布一次 RewardPoint。
                _broadcastPublisher.Publish(pending.SettledPayload.AtRewardPoint());
            }
            else
            {
                Debug.LogError($"[Exploration] 玩家 pending 超时且 Settled 缺失，已清理（cell={cell.HexCoordinate}）。");
            }
        }
    }

    public void Dispose()
    {
        _broadcastSource.Broadcast -= OnBroadcast;
        _pending.Clear();
    }

    /// <summary>订阅 Settled 阶段，缓存玩家实际结算结果；若奖励点已提前到达则补发。</summary>
    private void OnBroadcast(ExplorationAcquisition acquisition)
    {
        if (acquisition == null || acquisition.FactionId != 0)
            return;
        if (acquisition.Phase != ExplorationBroadcastPhase.Settled)
            return;

        if (_pending.TryGetValue(acquisition.Cell, out PlayerPending pending))
        {
            pending.SettledPayload = acquisition;
            if (pending.RewardPointRequested && !pending.RewardPointEmitted)
                EmitRewardPoint(acquisition.Cell, pending);
        }
    }

    private void EmitRewardPoint(HexCellData cell, PlayerPending pending)
    {
        if (pending.RewardPointEmitted)
            return;

        pending.RewardPointEmitted = true;
        _pending.Remove(cell);
        _broadcastPublisher.Publish(pending.SettledPayload.AtRewardPoint());
    }

    /// <summary>玩家侧探索阶段状态：等待动画奖励点与超时兜底。</summary>
    private sealed class PlayerPending
    {
        public ExplorationAcquisition ExploredPayload;
        public ExplorationAcquisition SettledPayload;
        public float EstablishedAt;
        public bool RewardPointRequested;
        public bool RewardPointEmitted;
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
