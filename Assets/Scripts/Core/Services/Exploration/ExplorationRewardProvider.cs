using System;
using System.Collections.Generic;
using GameConfig;

//****************************************
//功能说明：探索奖励提供者（阶段6：Excel 唯一主源）。
//         奖励类型权重、金币/单位数量档位、探索费用、奖励池关系仅由 Excel 决定；
//         奖励池的 configId（unit.*/tactical.*/building.*）经映射解析到手工资源 SO。
//         Excel 未生成/未命中时抛异常，暴露配置缺失。
//****************************************
public class ExplorationRewardProvider
{
    private readonly ExplorationRewardConfigDatabaseSO _config; // Excel 数值
    private readonly ExplorationRewardPoolDatabaseSO _pool;     // Excel 奖励池
    private readonly IUnitDataProvider _unitProvider;           // 按 legacyId 找 UnitConfigSO
    private readonly IBuildingDataProvider _buildingProvider;   // 按 legacyId 找 BuildingConfigSO
    private readonly UnitBalanceDatabaseSO _unitBalance;        // unitId → legacyId
    private readonly BuildingBalanceDatabaseSO _buildingBalance; // buildingId → legacyId
    private readonly TacticalCardDatabaseSO _tacticalDatabase;  // TacticalCardSO.cardId（稳定 ID）

    public ExplorationRewardProvider(
        ExplorationRewardConfigDatabaseSO config = null,
        ExplorationRewardPoolDatabaseSO pool = null,
        IUnitDataProvider unitProvider = null,
        IBuildingDataProvider buildingProvider = null,
        UnitBalanceDatabaseSO unitBalance = null,
        BuildingBalanceDatabaseSO buildingBalance = null,
        TacticalCardDatabaseSO tacticalDatabase = null)
    {
        _config = config;
        _pool = pool;
        _unitProvider = unitProvider;
        _buildingProvider = buildingProvider;
        _unitBalance = unitBalance;
        _buildingBalance = buildingBalance;
        _tacticalDatabase = tacticalDatabase;
    }

    private ExplorationRewardConfigData RequireConfig()
    {
        if (_config?.Config == null)
            throw new InvalidOperationException(
                "[ExplorationReward] Excel 探索奖励配置未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 ExplorationRewardConfigDatabaseSO。");
        return _config.Config;
    }

    public ExplorationRewardConfigSO.ExplorationRewardType RollRewardType(Random random)
    {
        var cfg = RequireConfig();
        int total = cfg.noneWeight + cfg.goldWeight + cfg.militaryWeight + cfg.tacticalWeight + cfg.buildingWeight;
        if (total <= 0) return ExplorationRewardConfigSO.ExplorationRewardType.None;

        int roll = random.Next(0, total);
        if (roll < cfg.noneWeight) return ExplorationRewardConfigSO.ExplorationRewardType.None;
        roll -= cfg.noneWeight;
        if (roll < cfg.goldWeight) return ExplorationRewardConfigSO.ExplorationRewardType.Gold;
        roll -= cfg.goldWeight;
        if (roll < cfg.militaryWeight) return ExplorationRewardConfigSO.ExplorationRewardType.MilitaryUnit;
        roll -= cfg.militaryWeight;
        if (roll < cfg.tacticalWeight) return ExplorationRewardConfigSO.ExplorationRewardType.TacticalCard;
        return ExplorationRewardConfigSO.ExplorationRewardType.Building;
    }

    public int RollGold(Random random)
    {
        int[] tiers = ParseIntList(RequireConfig().goldTiers);
        if (tiers.Length > 0) return tiers[random.Next(0, tiers.Length)];
        return 0;
    }

    public int RollUnitCount(Random random)
    {
        int[] tiers = ParseIntList(RequireConfig().unitCountTiers);
        if (tiers.Length > 0) return Math.Max(0, tiers[random.Next(0, tiers.Length)]);
        return 0;
    }

    public UnitConfigSO RollUnitConfig(Random random)
    {
        string configId = RollPoolEntry("MilitaryUnit", random);
        if (string.IsNullOrEmpty(configId)) return null;

        if (_unitBalance == null || !_unitBalance.TryGetUnit(configId, out var ub))
            throw new InvalidOperationException(
                $"[ExplorationReward] 奖励池 unit.{configId} 未在 Excel 单位平衡库命中。");
        if (_unitProvider == null || !_unitProvider.TryGetUnitConfig(ub.legacyId, out var unit))
            throw new InvalidOperationException(
                $"[ExplorationReward] 奖励池 unit.{configId} 映射的 legacyId {ub.legacyId} 无对应 UnitConfigSO 资源。");
        return unit;
    }

    public TacticalCardSO RollTacticalCard(Random random)
    {
        string configId = RollPoolEntry("TacticalCard", random);
        if (string.IsNullOrEmpty(configId)) return null;

        if (_tacticalDatabase == null || _tacticalDatabase.cards == null)
            throw new InvalidOperationException(
                "[ExplorationReward] 战术卡资源库 TacticalCardDatabaseSO 未加载。");
        foreach (var card in _tacticalDatabase.cards)
            if (card != null && card.cardId == configId) return card;

        throw new InvalidOperationException(
            $"[ExplorationReward] 奖励池 tactical.{configId} 无对应 TacticalCardSO 资源。");
    }

    public BuildingConfigSO RollBuildingConfig(Random random)
    {
        string configId = RollPoolEntry("Building", random);
        if (string.IsNullOrEmpty(configId)) return null;

        if (_buildingBalance == null || !_buildingBalance.TryGetBuilding(configId, out var bb))
            throw new InvalidOperationException(
                $"[ExplorationReward] 奖励池 building.{configId} 未在 Excel 建筑平衡库命中。");
        if (_buildingProvider == null || !_buildingProvider.TryGetBuildingConfig(bb.legacyId, out var building))
            throw new InvalidOperationException(
                $"[ExplorationReward] 奖励池 building.{configId} 映射的 legacyId {bb.legacyId} 无对应 BuildingConfigSO 资源。");
        return building;
    }

    /// <summary>地图生成时一次性固化奖励类型及其全部随机结果。</summary>
    public ExplorationRewardData GenerateReward(Random random)
    {
        var reward = new ExplorationRewardData
        {
            RewardType = RollRewardType(random)
        };

        switch (reward.RewardType)
        {
            case ExplorationRewardConfigSO.ExplorationRewardType.Gold:
                reward.GoldAmount = RollGold(random);
                break;

            case ExplorationRewardConfigSO.ExplorationRewardType.MilitaryUnit:
                int unitCount = Math.Max(0, RollUnitCount(random));
                reward.UnitConfigs = new UnitConfigSO[unitCount];
                for (int i = 0; i < unitCount; i++)
                {
                    reward.UnitConfigs[i] = RollUnitConfig(random);
                }
                break;

            case ExplorationRewardConfigSO.ExplorationRewardType.TacticalCard:
                reward.TacticalCard = RollTacticalCard(random);
                break;

            case ExplorationRewardConfigSO.ExplorationRewardType.Building:
                reward.BuildingConfig = RollBuildingConfig(random);
                reward.GoldAmount = RollGold(random);
                break;
        }

        return reward;
    }

    /// <summary>按奖励类型返回探索费用（Excel 唯一主源）。</summary>
    public int GetExplorationCost(ExplorationRewardConfigSO.ExplorationRewardType rewardType)
    {
        var cfg = RequireConfig();
        return rewardType switch
        {
            ExplorationRewardConfigSO.ExplorationRewardType.Gold => cfg.costGold,
            ExplorationRewardConfigSO.ExplorationRewardType.MilitaryUnit => cfg.costMilitary,
            ExplorationRewardConfigSO.ExplorationRewardType.TacticalCard => cfg.costTactical,
            ExplorationRewardConfigSO.ExplorationRewardType.Building => cfg.costBuilding,
            _ => cfg.costNone,
        };
    }

    private string RollPoolEntry(string rewardType, Random random)
    {
        if (_pool == null)
            throw new InvalidOperationException(
                "[ExplorationReward] Excel 奖励池未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 ExplorationRewardPoolDatabaseSO。");

        var candidates = new List<ExplorationRewardPoolEntry>();
        int totalWeight = 0;
        foreach (var entry in _pool.EnabledEntries)
        {
            if (entry == null || entry.rewardType != rewardType) continue;
            candidates.Add(entry);
            totalWeight += Math.Max(1, entry.weight);
        }

        if (candidates.Count == 0 || totalWeight <= 0) return null;

        int roll = random.Next(0, totalWeight);
        foreach (var entry in candidates)
        {
            roll -= Math.Max(1, entry.weight);
            if (roll < 0) return entry.configId;
        }
        return candidates[candidates.Count - 1].configId;
    }

    private static int[] ParseIntList(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<int>();
        var parts = csv.Split(',');
        var result = new List<int>(parts.Length);
        foreach (var p in parts)
        {
            if (int.TryParse(p.Trim(), out var n))
                result.Add(n);
        }
        return result.ToArray();
    }
}
