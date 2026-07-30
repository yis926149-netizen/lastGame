using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 探索奖励系统：监听探索完成事件，掷骰发放金币和单位奖励。
/// 【探索奖励随机机制】新增系统。
/// </summary>
public class ExplorationRewardSystem
{
    private readonly IExplorationService _explorationService;
    private readonly ExplorationRewardConfigSO _config;
    private readonly GoldWallet _goldWallet;
    private readonly IPlayerUnitSpawnService _unitSpawnService;
    private readonly IMapDataService _mapDataService;

    public ExplorationRewardSystem(
        IExplorationService explorationService,
        ExplorationRewardConfigSO config,
        GoldWallet goldWallet,
        IPlayerUnitSpawnService unitSpawnService,
        IMapDataService mapDataService)
    {
        _explorationService = explorationService;
        _config = config;
        _goldWallet = goldWallet;
        _unitSpawnService = unitSpawnService;
        _mapDataService = mapDataService;

        _explorationService.ExplorationRewardTriggered += OnExplorationRewardTriggered;
    }

    ~ExplorationRewardSystem()
    {
        if (_explorationService != null)
        {
            _explorationService.ExplorationRewardTriggered -= OnExplorationRewardTriggered;
        }
    }

    private void OnExplorationRewardTriggered(HexCellData cell)
    {
        if (cell == null) return;

        // 1. 掷金币骰子，发放金币奖励
        int goldAmount = _config.RollGold();
        if (goldAmount > 0)
        {
            _goldWallet.AddGold(0, goldAmount); // PlayerIndex = 0
        }

        // 2. 掷单位骰子，生成单位奖励
        int unitCount = _config.RollUnitCount();
        if (unitCount > 0)
        {
            SpawnUnitsWithOverflow(cell, unitCount);
        }
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

            // 目标格已有单位 → 尝试溢出到邻格
            if (targetCell.IsHaveUnit())
            {
                spawnCell = FindOverflowCell(targetCell);
                if (spawnCell == null)
                {
                    Debug.LogWarning($"[ExplorationReward] 无法生成第 {i + 1} 个单位：目标格及邻格均已占用");
                    continue; // 跳过该单位
                }
                spawnPosition = spawnCell.RealCenterWorldCoordinate;
            }

            // 生成单位
            GameObject unit = _unitSpawnService.SpawnPlayerUnit(_config.rewardUnitID, spawnPosition);
                if (unit != null) { }
                else
                {
                    Debug.LogError($"[ExplorationReward] 生成单位失败：unitID={_config.rewardUnitID}, position={spawnPosition}");
                }
        }
    }

    /// <summary>
    /// 查找可溢出的邻格：非水域、无建筑、无单位的空格。
    /// </summary>
    private HexCellData FindOverflowCell(HexCellData originCell)
    {
        var candidates = new List<HexCellData>();

        for (int dir = 0; dir < 6; dir++)
        {
            HexCellData neighbor = _mapDataService.GetNeighbor(originCell, (Enums.HexDirection)dir);
            if (neighbor == null) continue;

            // 筛选条件：非水域、无建筑、无单位
            if (neighbor.HexType == Enums.HexType.LakeOrSea) continue;
            if (neighbor.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding) continue;
            if (neighbor.IsHaveUnit()) continue;

            candidates.Add(neighbor);
        }

        // 随机选一格
        return candidates.Count > 0 ? candidates[Random.Range(0, candidates.Count)] : null;
    }
}
