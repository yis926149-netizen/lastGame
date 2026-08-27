using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家探索奖励结算：订阅统一广播的 Explored 阶段（仅玩家阵营），
/// 从载荷读取原始奖励字段（不再读取或消费 cell 上的奖励快照），
/// 每条处理路径结束后发布且只发布一个 Settled（实际结算结果）。
/// </summary>
public class ExplorationRewardSystem : IDisposable
{
    private readonly IExplorationBroadcastSource _broadcastSource;
    private readonly IExplorationBroadcastPublisher _broadcastPublisher;
    private readonly GoldWallet _goldWallet;
    private readonly IPlayerUnitSpawnService _unitSpawnService;
    private readonly IPlayerBuildingSpawnService _buildingSpawnService;
    private readonly IMapDataService _mapDataService;
    private readonly TacticalCardPresenter _tacticalCardPresenter;
    private readonly AIConfigProvider _aiConfig;

    public ExplorationRewardSystem(
        IExplorationBroadcastSource broadcastSource,
        IExplorationBroadcastPublisher broadcastPublisher,
        GoldWallet goldWallet,
        IPlayerUnitSpawnService unitSpawnService,
        IPlayerBuildingSpawnService buildingSpawnService,
        IMapDataService mapDataService,
        TacticalCardPresenter tacticalCardPresenter,
        AIConfigProvider aiConfig = null)
    {
        _broadcastSource = broadcastSource;
        _broadcastPublisher = broadcastPublisher;
        _goldWallet = goldWallet;
        _unitSpawnService = unitSpawnService;
        _buildingSpawnService = buildingSpawnService;
        _mapDataService = mapDataService;
        _tacticalCardPresenter = tacticalCardPresenter;
        _aiConfig = aiConfig;

        _broadcastSource.Broadcast += OnBroadcast;
    }

    public void Dispose()
    {
        _broadcastSource.Broadcast -= OnBroadcast;
    }

    private void OnBroadcast(ExplorationAcquisition acquisition)
    {
        if (acquisition == null || acquisition.FactionId != 0)
            return;
        // 只处理 Explored，避免收到自身发布的 Settled 而递归结算。
        if (acquisition.Phase != ExplorationBroadcastPhase.Explored)
            return;

        Settle(acquisition);
    }

    private void Settle(ExplorationAcquisition acquisition)
    {
        HexCellData cell = acquisition.Cell;

        try
        {
            if (!acquisition.HasRewardSnapshot)
            {
                Debug.LogWarning($"[ExplorationReward] 地块 {cell.HexCoordinate} 没有预生成奖励，跳过结算");
                _broadcastPublisher.Publish(acquisition.SettledAs(ExplorationRewardConfigSO.ExplorationRewardType.None));
                return;
            }

            switch (acquisition.OriginalRewardType)
            {
                case ExplorationRewardConfigSO.ExplorationRewardType.None:
                    Debug.Log($"[ExplorationReward] 地块 {cell.HexCoordinate} 无奖励");
                    _broadcastPublisher.Publish(acquisition.SettledAs(ExplorationRewardConfigSO.ExplorationRewardType.None));
                    break;

                case ExplorationRewardConfigSO.ExplorationRewardType.Gold:
                    AddGoldReward(acquisition.OriginalGoldAmount);
                    Debug.Log($"[ExplorationReward] 地块 {cell.HexCoordinate} 金币奖励 +{acquisition.OriginalGoldAmount}");
                    _broadcastPublisher.Publish(acquisition.SettledAs(ExplorationRewardConfigSO.ExplorationRewardType.Gold, acquisition.OriginalGoldAmount));
                    break;

                case ExplorationRewardConfigSO.ExplorationRewardType.MilitaryUnit:
                    int unitCount = acquisition.UnitConfigs?.Count ?? 0;
                    if (unitCount > 0)
                    {
                        SpawnUnitsWithOverflow(cell, acquisition.UnitConfigs);
                    }
                    Debug.Log($"[ExplorationReward] 地块 {cell.HexCoordinate} 军事单位奖励 x{unitCount}");
                    _broadcastPublisher.Publish(acquisition.SettledAs(ExplorationRewardConfigSO.ExplorationRewardType.MilitaryUnit));
                    break;

                case ExplorationRewardConfigSO.ExplorationRewardType.TacticalCard:
                    TacticalCardSO card = acquisition.TacticalCard;
                    if (card != null && _tacticalCardPresenter != null)
                    {
                        _tacticalCardPresenter.AddCardWithFly(card, cell.RealCenterWorldCoordinate);
                    }
                    else
                    {
                        Debug.LogWarning($"[ExplorationReward] 战术奖励但无法发牌（配置数据库或持有者为空），地块 {cell.HexCoordinate}");
                    }
                    _broadcastPublisher.Publish(acquisition.SettledAs(ExplorationRewardConfigSO.ExplorationRewardType.TacticalCard));
                    break;

                case ExplorationRewardConfigSO.ExplorationRewardType.Building:
                    SettleBuilding(acquisition);
                    break;

                default:
                    _broadcastPublisher.Publish(acquisition.SettledAs(ExplorationRewardConfigSO.ExplorationRewardType.None));
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            // 结算内部异常也必须发布结果，避免玩家 pending 永久等待。
            _broadcastPublisher.Publish(acquisition.SettledAs(ExplorationRewardConfigSO.ExplorationRewardType.None));
        }
    }

    /// <summary>建筑奖励：成功放置建筑，否则降级为备用金币（三条降级路径统一发布 Gold 结算）。</summary>
    private void SettleBuilding(ExplorationAcquisition acquisition)
    {
        HexCellData cell = acquisition.Cell;
        BuildingConfigSO buildingConfig = acquisition.BuildingConfig;
        int fallbackGold = acquisition.OriginalGoldAmount;

        if (buildingConfig == null)
        {
            Debug.LogWarning($"[ExplorationReward] 建筑奖励但 rewardBuildings 为空，地块 {cell.HexCoordinate}，降级为金币");
            AddGoldReward(fallbackGold);
            _broadcastPublisher.Publish(acquisition.SettledAs(ExplorationRewardConfigSO.ExplorationRewardType.Gold, fallbackGold));
            return;
        }

        if (RewardBuildingRule.CanPlace(cell))
        {
            if (_buildingSpawnService.SpawnPlayerBuilding(buildingConfig.buildingId, cell.RealCenterWorldCoordinate))
            {
                Debug.Log($"[ExplorationReward] 地块 {cell.HexCoordinate} 建筑奖励：{buildingConfig.name}");
                _broadcastPublisher.Publish(acquisition.SettledAs(ExplorationRewardConfigSO.ExplorationRewardType.Building));
            }
            else
            {
                Debug.LogWarning($"[ExplorationReward] 建筑生成失败（{buildingConfig.name}），地块 {cell.HexCoordinate}，降级为金币");
                AddGoldReward(fallbackGold);
                _broadcastPublisher.Publish(acquisition.SettledAs(ExplorationRewardConfigSO.ExplorationRewardType.Gold, fallbackGold));
            }
        }
        else
        {
            // 格子不合格（公共建筑/山格/禁建地貌/已有单位或建筑）→ 降级为金币
            Debug.Log($"[ExplorationReward] 地块 {cell.HexCoordinate} 不可建造，建筑奖励降级为金币");
            AddGoldReward(fallbackGold);
            _broadcastPublisher.Publish(acquisition.SettledAs(ExplorationRewardConfigSO.ExplorationRewardType.Gold, fallbackGold));
        }
    }

    /// <summary>金币奖励结算：只负责钱包入账，表现由 RewardPoint 阶段的 CoinPresenter 处理。</summary>
    private void AddGoldReward(int goldAmount)
    {
        if (goldAmount > 0)
        {
            _goldWallet.AddGold(0, goldAmount); // PlayerIndex = 0
        }
    }

    /// <summary>
    /// 在目标地块生成单位，溢出时放入相邻地块。
    /// </summary>
    private void SpawnUnitsWithOverflow(HexCellData targetCell, IReadOnlyList<UnitConfigSO> unitConfigs)
    {
        for (int i = 0; i < unitConfigs.Count; i++)
        {
            Vector3 spawnPosition = targetCell.RealCenterWorldCoordinate;
            HexCellData spawnCell = targetCell;

            // 目标格已有单位或为山格/水域 → 尝试溢出到邻格（决策 ①：山格不可部署）
            if (targetCell.IsHaveUnit() || !MountainCellRule.CanSpawnUnitOnCell(targetCell))
            {
                spawnCell = FindOverflowCell(targetCell);
                if (spawnCell == null)
                {
                    Debug.LogWarning($"[ExplorationReward] 无法生成第 {i + 1} 个单位：目标格及邻格均已占用或不可部署");
                    continue; // 跳过该单位
                }
                spawnPosition = spawnCell.RealCenterWorldCoordinate;
            }

            // 生成单位
            UnitConfigSO unitConfig = unitConfigs[i];
            if (unitConfig == null)
            {
                Debug.LogWarning("[ExplorationReward] 探索奖励无可用单位配置（rewardUnits 为空），跳过生成");
                continue;
            }
            GameObject unit = _unitSpawnService.SpawnPlayerUnit(unitConfig.Id, spawnPosition);
                if (unit != null) { }
                else
                {
                    Debug.LogError($"[ExplorationReward] 生成单位失败：unitID={unitConfig.Id}, position={spawnPosition}");
                }
        }
    }

    /// <summary>
    /// 以 BFS 从 originCell 向外扩展查找可用的溢出格（非水域、无建筑、无单位）。
    /// 最多扩展到 maxRings 环，找不到返回 null。
    /// </summary>
    private HexCellData FindOverflowCell(HexCellData originCell)
    {
        int maxRings = _aiConfig.MilitaryRewardOverflowRings;

        var visited = new HashSet<Vector3> { originCell.HexCoordinate };
        var frontier = new List<HexCellData> { originCell };

        for (int ring = 0; ring < maxRings; ring++)
        {
            var nextFrontier = new List<HexCellData>();

            foreach (var cell in frontier)
            {
                for (int dir = 0; dir < 6; dir++)
                {
                    HexCellData neighbor = _mapDataService.GetNeighbor(cell, (Enums.HexDirection)dir);
                    if (neighbor == null) continue;
                    if (!visited.Add(neighbor.HexCoordinate)) continue;

                    if (neighbor.HexType != Enums.HexType.LakeOrSea &&
                        MountainCellRule.CanSpawnUnitOnCell(neighbor) &&
                        neighbor.BulidingTypeOnHex_Building.Key == Enums.BulidingType.NoBuilding &&
                        !neighbor.IsHaveUnit())
                    {
                        return neighbor;
                    }

                    nextFrontier.Add(neighbor);
                }
            }

            frontier = nextFrontier;
        }

        return null;
    }
}
