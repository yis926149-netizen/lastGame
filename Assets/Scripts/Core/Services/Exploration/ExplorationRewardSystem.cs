using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 探索奖励系统：监听探索完成事件，按【两段式随机】结算奖励。
/// 第一次掷骰决定奖励类型（无奖励/金币/军事单位/战术卡牌），
/// 第二次掷骰决定该类型的具体数值（档位表见 ExplorationRewardConfigSO）。
/// 每个地块每次探索只结算一种奖励。
/// </summary>
public class ExplorationRewardSystem
{
    private readonly IExplorationService _explorationService;
    private readonly ExplorationRewardConfigSO _config;
    private readonly GoldWallet _goldWallet;
    private readonly IPlayerUnitSpawnService _unitSpawnService;
    private readonly IPlayerBuildingSpawnService _buildingSpawnService;
    private readonly IMapDataService _mapDataService;
    private readonly TacticalCardPresenter _tacticalCardPresenter;
    private readonly ExplorationCoinPresenter _coinPresenter;

    public ExplorationRewardSystem(
        IExplorationService explorationService,
        ExplorationRewardConfigSO config,
        GoldWallet goldWallet,
        IPlayerUnitSpawnService unitSpawnService,
        IPlayerBuildingSpawnService buildingSpawnService,
        IMapDataService mapDataService,
        TacticalCardPresenter tacticalCardPresenter,
        ExplorationCoinPresenter coinPresenter)
    {
        _explorationService = explorationService;
        _config = config;
        _goldWallet = goldWallet;
        _unitSpawnService = unitSpawnService;
        _buildingSpawnService = buildingSpawnService;
        _mapDataService = mapDataService;
        _tacticalCardPresenter = tacticalCardPresenter;
        _coinPresenter = coinPresenter;

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

        // 第一次掷骰：奖励类型
        ExplorationRewardConfigSO.ExplorationRewardType rewardType = _config.RollRewardType();

        switch (rewardType)
        {
            case ExplorationRewardConfigSO.ExplorationRewardType.None:
                Debug.Log($"[ExplorationReward] 地块 {cell.HexCoordinate} 无奖励");
                break;

            case ExplorationRewardConfigSO.ExplorationRewardType.Gold:
                // 第二次掷骰：金币档位
                int goldAmount = AddGoldReward(cell);
                Debug.Log($"[ExplorationReward] 地块 {cell.HexCoordinate} 金币奖励 +{goldAmount}");
                break;

            case ExplorationRewardConfigSO.ExplorationRewardType.MilitaryUnit:
                // 第二次掷骰：单位数量与单位 ID
                int unitCount = _config.RollUnitCount();
                if (unitCount > 0)
                {
                    SpawnUnitsWithOverflow(cell, unitCount);
                }
                Debug.Log($"[ExplorationReward] 地块 {cell.HexCoordinate} 军事单位奖励 x{unitCount}");
                break;

            case ExplorationRewardConfigSO.ExplorationRewardType.TacticalCard:
                // 第二次掷骰：随机一张战术牌
                TacticalCardSO card = _config.RollTacticalCard();
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
                // 第二次掷骰：随机一种建筑，直接放置在被探索地块上
                BuildingConfigSO buildingConfig = _config.RollBuildingConfig();
                if (buildingConfig == null)
                {
                    Debug.LogWarning($"[ExplorationReward] 建筑奖励但 rewardBuildings 为空，地块 {cell.HexCoordinate}，降级为金币");
                    AddGoldReward(cell);
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
                        AddGoldReward(cell);
                    }
                }
                else
                {
                    // 格子不合格（公共建筑/山格/禁建地貌/已有单位或建筑）→ 降级为金币
                    Debug.Log($"[ExplorationReward] 地块 {cell.HexCoordinate} 不可建造，建筑奖励降级为金币");
                    AddGoldReward(cell);
                }
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// 金币奖励结算：掷金币档位 → 入账 → 播金币表现（纯表现层，失败不影响结算）。返回金币数量。
    /// </summary>
    private int AddGoldReward(HexCellData cell)
    {
        int goldAmount = _config.RollGold();
        if (goldAmount > 0)
        {
            _goldWallet.AddGold(0, goldAmount); // PlayerIndex = 0
            if (_coinPresenter != null)
            {
                _coinPresenter.PlayCoinAt(cell, goldAmount);
            }
        }
        return goldAmount;
    }

    /// <summary>
    /// 在目标地块生成单位，溢出时放入相邻地块。
    /// </summary>
    private void SpawnUnitsWithOverflow(HexCellData targetCell, int count)
    {
        for (int i = 0; i < count; i++)
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
            UnitConfigSO unitConfig = _config.RollUnitConfig();
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
        const int maxRings = 5;

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
