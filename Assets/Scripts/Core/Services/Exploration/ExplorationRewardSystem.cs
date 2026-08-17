using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 探索奖励系统：监听探索完成事件，消费地图生成时固化的奖励快照。
/// 每个地块每次探索只结算一种奖励。
/// </summary>
public class ExplorationRewardSystem
{
    private readonly IExplorationService _explorationService;
    private readonly GoldWallet _goldWallet;
    private readonly IPlayerUnitSpawnService _unitSpawnService;
    private readonly IPlayerBuildingSpawnService _buildingSpawnService;
    private readonly IMapDataService _mapDataService;
    private readonly TacticalCardPresenter _tacticalCardPresenter;
    private readonly ExplorationCoinPresenter _coinPresenter;
    private readonly AIConfigProvider _aiConfig;

    public ExplorationRewardSystem(
        IExplorationService explorationService,
        GoldWallet goldWallet,
        IPlayerUnitSpawnService unitSpawnService,
        IPlayerBuildingSpawnService buildingSpawnService,
        IMapDataService mapDataService,
        TacticalCardPresenter tacticalCardPresenter,
        ExplorationCoinPresenter coinPresenter,
        AIConfigProvider aiConfig = null)
    {
        _explorationService = explorationService;
        _goldWallet = goldWallet;
        _unitSpawnService = unitSpawnService;
        _buildingSpawnService = buildingSpawnService;
        _mapDataService = mapDataService;
        _tacticalCardPresenter = tacticalCardPresenter;
        _coinPresenter = coinPresenter;
        _aiConfig = aiConfig;

        _explorationService.ExplorationRewardTriggered += OnExplorationRewardTriggered;
    }

    ~ExplorationRewardSystem()
    {
        if (_explorationService != null)
        {
            _explorationService.ExplorationRewardTriggered -= OnExplorationRewardTriggered;
        }
    }

    private void OnExplorationRewardTriggered(HexCellData cell, int factionId)
    {
        if (cell == null) return;

        // 本系统依赖玩家专属服务（玩家钱包/玩家单位生成/玩家战术牌），只结算玩家阵营；
        // AI 阵营的奖励由 AIAutoExplorer 订阅同一事件按阵营分发结算。
        if (factionId != 0) return;

        ExplorationRewardData reward = cell.TakeExplorationReward();
        if (reward == null)
        {
            Debug.LogWarning($"[ExplorationReward] 地块 {cell.HexCoordinate} 没有预生成奖励，跳过结算");
            return;
        }

        switch (reward.RewardType)
        {
            case ExplorationRewardConfigSO.ExplorationRewardType.None:
                Debug.Log($"[ExplorationReward] 地块 {cell.HexCoordinate} 无奖励");
                break;

            case ExplorationRewardConfigSO.ExplorationRewardType.Gold:
                AddGoldReward(cell, reward.GoldAmount);
                Debug.Log($"[ExplorationReward] 地块 {cell.HexCoordinate} 金币奖励 +{reward.GoldAmount}");
                break;

            case ExplorationRewardConfigSO.ExplorationRewardType.MilitaryUnit:
                int unitCount = reward.UnitConfigs?.Length ?? 0;
                if (unitCount > 0)
                {
                    SpawnUnitsWithOverflow(cell, reward.UnitConfigs);
                }
                Debug.Log($"[ExplorationReward] 地块 {cell.HexCoordinate} 军事单位奖励 x{unitCount}");
                break;

            case ExplorationRewardConfigSO.ExplorationRewardType.TacticalCard:
                TacticalCardSO card = reward.TacticalCard;
                if (card != null && _tacticalCardPresenter != null)
                {
                    _tacticalCardPresenter.AddCardWithFly(card, cell.RealCenterWorldCoordinate);
                }
                else
                {
                    Debug.LogWarning($"[ExplorationReward] 战术奖励但无法发牌（配置数据库或持有者为空），地块 {cell.HexCoordinate}");
                }
                break;

            case ExplorationRewardConfigSO.ExplorationRewardType.Building:
                BuildingConfigSO buildingConfig = reward.BuildingConfig;
                if (buildingConfig == null)
                {
                    Debug.LogWarning($"[ExplorationReward] 建筑奖励但 rewardBuildings 为空，地块 {cell.HexCoordinate}，降级为金币");
                    AddGoldReward(cell, reward.GoldAmount);
                    break;
                }
                if (RewardBuildingRule.CanPlace(cell))
                {
                    if (_buildingSpawnService.SpawnPlayerBuilding(buildingConfig.buildingId, cell.RealCenterWorldCoordinate))
                    {
                        Debug.Log($"[ExplorationReward] 地块 {cell.HexCoordinate} 建筑奖励：{buildingConfig.name}");
                    }
                    else
                    {
                        Debug.LogWarning($"[ExplorationReward] 建筑生成失败（{buildingConfig.name}），地块 {cell.HexCoordinate}，降级为金币");
                        AddGoldReward(cell, reward.GoldAmount);
                    }
                }
                else
                {
                    // 格子不合格（公共建筑/山格/禁建地貌/已有单位或建筑）→ 降级为金币
                    Debug.Log($"[ExplorationReward] 地块 {cell.HexCoordinate} 不可建造，建筑奖励降级为金币");
                    AddGoldReward(cell, reward.GoldAmount);
                }
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// 金币奖励结算：使用预生成数量入账并播放表现。
    /// </summary>
    private void AddGoldReward(HexCellData cell, int goldAmount)
    {
        if (goldAmount > 0)
        {
            _goldWallet.AddGold(0, goldAmount); // PlayerIndex = 0
            if (_coinPresenter != null)
            {
                _coinPresenter.PlayCoinAt(cell, goldAmount);
            }
        }
    }

    /// <summary>
    /// 在目标地块生成单位，溢出时放入相邻地块。
    /// </summary>
    private void SpawnUnitsWithOverflow(HexCellData targetCell, UnitConfigSO[] unitConfigs)
    {
        for (int i = 0; i < unitConfigs.Length; i++)
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
        int maxRings = _aiConfig?.MilitaryRewardOverflowRings ?? 5;

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
