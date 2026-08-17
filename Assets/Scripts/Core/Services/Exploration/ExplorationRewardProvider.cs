using System;
using System.Collections.Generic;
using GameConfig;

//****************************************
//功能说明：探索奖励提供者（对象化 + Excel 数值化）。
//         奖励类型权重、金币/单位数量档位、探索费用、奖励池关系优先由 Excel 决定；
//         奖励池的 configId（unit.*/tactical.*/building.*）经映射解析到手工资源 SO。
//         Excel 未生成时回退 Legacy ExplorationRewardConfigSO 字段（双轨迁移期）。
//****************************************
public class ExplorationRewardProvider
{
    private readonly ExplorationRewardConfigSO _legacyConfig;  // Legacy 手工配置（回退）
    private readonly ExplorationRewardConfigDatabaseSO _config; // Excel 数值
    private readonly ExplorationRewardPoolDatabaseSO _pool;     // Excel 奖励池
    private readonly IUnitDataProvider _unitProvider;           // 按 legacyId 找 UnitConfigSO
    private readonly IBuildingDataProvider _buildingProvider;   // 按 legacyId 找 BuildingConfigSO
    private readonly UnitBalanceDatabaseSO _unitBalance;        // unitId → legacyId
    private readonly BuildingBalanceDatabaseSO _buildingBalance; // buildingId → legacyId
    private readonly TacticalCardDatabaseSO _tacticalDatabase;  // TacticalCardSO.cardId（稳定 ID）

    public ExplorationRewardProvider(
        ExplorationRewardConfigSO legacyConfig = null,
        ExplorationRewardConfigDatabaseSO config = null,
        ExplorationRewardPoolDatabaseSO pool = null,
        IUnitDataProvider unitProvider = null,
        IBuildingDataProvider buildingProvider = null,
        UnitBalanceDatabaseSO unitBalance = null,
        BuildingBalanceDatabaseSO buildingBalance = null,
        TacticalCardDatabaseSO tacticalDatabase = null)
    {
        _legacyConfig = legacyConfig;
        _config = config;
        _pool = pool;
        _unitProvider = unitProvider;
        _buildingProvider = buildingProvider;
        _unitBalance = unitBalance;
        _buildingBalance = buildingBalance;
        _tacticalDatabase = tacticalDatabase;
    }

    public ExplorationRewardConfigData Config => _config?.Config;

    public ExplorationRewardConfigSO.ExplorationRewardType RollRewardType(Random random)
    {
        var cfg = Config;
        if (cfg != null)
        {
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

        return _legacyConfig != null
            ? _legacyConfig.RollRewardType(random)
            : ExplorationRewardConfigSO.ExplorationRewardType.None;
    }

    public int RollGold(Random random)
    {
        var cfg = Config;
        if (cfg != null)
        {
            int[] tiers = ParseIntList(cfg.goldTiers);
            if (tiers.Length > 0) return tiers[random.Next(0, tiers.Length)];
            return 0;
        }
        return _legacyConfig != null ? _legacyConfig.RollGold(random) : 0;
    }

    public int RollUnitCount(Random random)
    {
        var cfg = Config;
        if (cfg != null)
        {
            int[] tiers = ParseIntList(cfg.unitCountTiers);
            if (tiers.Length > 0) return Math.Max(0, tiers[random.Next(0, tiers.Length)]);
            return 0;
        }
        return _legacyConfig != null ? _legacyConfig.RollUnitCount(random) : 0;
    }

    public UnitConfigSO RollUnitConfig(Random random)
    {
        string configId = RollPoolEntry("MilitaryUnit", random);
        if (!string.IsNullOrEmpty(configId)
            && _unitBalance != null && _unitBalance.TryGetUnit(configId, out var ub)
            && _unitProvider != null && _unitProvider.TryGetUnitConfig(ub.legacyId, out var unit))
            return unit;

        return _legacyConfig != null ? _legacyConfig.RollUnitConfig(random) : null;
    }

    public TacticalCardSO RollTacticalCard(Random random)
    {
        string configId = RollPoolEntry("TacticalCard", random);
        if (!string.IsNullOrEmpty(configId) && _tacticalDatabase != null && _tacticalDatabase.cards != null)
        {
            foreach (var card in _tacticalDatabase.cards)
                if (card != null && card.cardId == configId) return card;
        }

        return _legacyConfig != null ? _legacyConfig.RollTacticalCard(random) : null;
    }

    public BuildingConfigSO RollBuildingConfig(Random random)
    {
        string configId = RollPoolEntry("Building", random);
        if (!string.IsNullOrEmpty(configId)
            && _buildingBalance != null && _buildingBalance.TryGetBuilding(configId, out var bb)
            && _buildingProvider != null && _buildingProvider.TryGetBuildingConfig(bb.legacyId, out var building))
            return building;

        return _legacyConfig != null ? _legacyConfig.RollBuildingConfig(random) : null;
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

    /// <summary>按奖励类型返回探索费用（Excel 优先，缺失回退 Legacy）。</summary>
    public int GetExplorationCost(ExplorationRewardConfigSO.ExplorationRewardType rewardType)
    {
        var cfg = Config;
        if (cfg != null)
        {
            return rewardType switch
            {
                ExplorationRewardConfigSO.ExplorationRewardType.Gold => cfg.costGold,
                ExplorationRewardConfigSO.ExplorationRewardType.MilitaryUnit => cfg.costMilitary,
                ExplorationRewardConfigSO.ExplorationRewardType.TacticalCard => cfg.costTactical,
                ExplorationRewardConfigSO.ExplorationRewardType.Building => cfg.costBuilding,
                _ => cfg.costNone,
            };
        }

        return _legacyConfig != null ? _legacyConfig.GetExplorationCost(rewardType) : 0;
    }

    private string RollPoolEntry(string rewardType, Random random)
    {
        if (_pool == null) return null;

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
