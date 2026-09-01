using UnityEngine;

/// <summary>
/// 探索奖励配置 ScriptableObject。
/// 【两段式随机】第一次掷骰决定奖励类型（无奖励/金币/军事单位/战术卡牌），
/// 第二次掷骰决定该类型的具体数值。每个地块每次探索只结算一种奖励。
/// 所有概率与奖励数值均暴露在此配置中，便于调参。
/// </summary>
[CreateAssetMenu(fileName = "ExplorationRewardConfig", menuName = "Game/Exploration Reward Config")]
public class ExplorationRewardConfigSO : ScriptableObject
{
    /// <summary>奖励类型（第一次随机结果）。每个地块每次探索只结算一种。</summary>
    public enum ExplorationRewardType
    {
        None = 0,         // 无奖励：只获得地块
        Gold = 1,         // 金币奖励：即时金币
        MilitaryUnit = 2, // 军事单位奖励：生成单位
        TacticalCard = 3, // 战术卡牌奖励：获得一张战术牌
        Building = 4,     // 建筑奖励：在被探索地块上直接放置建筑（格子不合格时降级为金币）
    }

    [Header("第一次随机：奖励类型权重")]
    [Tooltip("无奖励权重（策划案对应：普通 50%）")]
    public int noneRewardWeight = 50;

    [Tooltip("金币奖励权重（策划案对应：经济 30%，已移除金矿分支，纯即时金币）")]
    public int goldRewardWeight = 30;

    [Tooltip("军事单位奖励权重（策划案对应：军事 10%）")]
    public int militaryRewardWeight = 10;

    [Tooltip("战术卡牌奖励权重（策划案对应：战术 10%）")]
    public int tacticalRewardWeight = 10;

    [Tooltip("建筑奖励权重（高价值奖励，建议低权重）")]
    public int buildingRewardWeight = 5;

    [Header("第二次随机：金币档位（金币奖励）")]
    [Tooltip("金币奖励档位数组，等概率随机选择一档。")]
    public int[] goldTiers = new int[] { 25, 50, 100, 200, 400 };

    [Header("第二次随机：军事单位档位（军事单位奖励）")]
    [Tooltip("单位数量档位数组，等概率随机选择一档。")]
    public int[] unitCountTiers = new int[] { 1, 2, 3, 4, 5 };

    [Tooltip("奖励可生成的单位配置（生成每个单位时随机选取）。")]
    public UnitConfigSO[] rewardUnits;

    [Header("第二次随机：战术卡牌（战术卡牌奖励）")]
    [Tooltip("奖励可获得的战术卡牌（等概率随机抽取一张）。")]
    public TacticalCardSO[] rewardTacticalCards;

    [Header("第二次随机：建筑（建筑奖励）")]
    [Tooltip("奖励可放置的建筑配置（等概率随机选择）。当前配置为金矿；地块不合格或生成失败时降级为金币。请勿放入 City/PublicBuilding。")]
    public BuildingConfigSO[] rewardBuildings;

    [Header("探索费用（按地块奖励类型）")]
    [Tooltip("按奖励类型索引的探索费用数组（0=无/1=金币/2=军事/3=战术/4=建筑）；未配置或越界时回退默认 50。")]
    public int[] explorationCostsByType = new int[] { 50, 50, 50, 50, 50 };

    private const int DefaultExplorationCost = 50;

    /// <summary>按地块自身的奖励类型返回探索费用；未配置或越界时回退默认值。</summary>
    public int GetExplorationCost(ExplorationRewardType rewardType)
    {
        int index = (int)rewardType;
        if (explorationCostsByType == null || index < 0 || index >= explorationCostsByType.Length)
        {
            return DefaultExplorationCost;
        }
        return explorationCostsByType[index];
    }

    /// <summary>地图生成专用：使用受 SeedService 管理的随机流抽取奖励类型。</summary>
    public ExplorationRewardType RollRewardType(System.Random random)
    {
        int total = noneRewardWeight + goldRewardWeight + militaryRewardWeight + tacticalRewardWeight + buildingRewardWeight;
        if (total <= 0) return ExplorationRewardType.None;

        return ResolveRewardType(random.Next(0, total));
    }

    private ExplorationRewardType ResolveRewardType(int roll)
    {
        if (roll < noneRewardWeight) return ExplorationRewardType.None;
        roll -= noneRewardWeight;
        if (roll < goldRewardWeight) return ExplorationRewardType.Gold;
        roll -= goldRewardWeight;
        if (roll < militaryRewardWeight) return ExplorationRewardType.MilitaryUnit;
        roll -= militaryRewardWeight;
        if (roll < tacticalRewardWeight) return ExplorationRewardType.TacticalCard;
        return ExplorationRewardType.Building;
    }

    /// <summary>金币档位（地图生成时经 GenerateReward 按种子流掷出）。</summary>
    public int RollGold(System.Random random)
    {
        if (goldTiers == null || goldTiers.Length == 0) return 0;
        return goldTiers[random.Next(0, goldTiers.Length)];
    }

    /// <summary>单位数量档位（地图生成时经 GenerateReward 按种子流掷出）。</summary>
    public int RollUnitCount(System.Random random)
    {
        if (unitCountTiers == null || unitCountTiers.Length == 0) return 0;
        return unitCountTiers[random.Next(0, unitCountTiers.Length)];
    }

    /// <summary>从奖励单位配置数组中随机返回一个配置；空数组返回 null（不再回退魔法 ID）。</summary>
    public UnitConfigSO RollUnitConfig(System.Random random)
    {
        if (rewardUnits == null || rewardUnits.Length == 0) return null;
        return rewardUnits[random.Next(0, rewardUnits.Length)];
    }

    /// <summary>从奖励战术卡牌数组中随机抽取一张，返回 null 表示无可发牌。</summary>
    public TacticalCardSO RollTacticalCard(System.Random random)
    {
        if (rewardTacticalCards == null || rewardTacticalCards.Length == 0) return null;
        return rewardTacticalCards[random.Next(0, rewardTacticalCards.Length)];
    }

    /// <summary>从奖励建筑配置数组中等概率返回一个配置；空数组返回 null。</summary>
    public BuildingConfigSO RollBuildingConfig(System.Random random)
    {
        if (rewardBuildings == null || rewardBuildings.Length == 0) return null;
        return rewardBuildings[random.Next(0, rewardBuildings.Length)];
    }

    /// <summary>地图生成时一次性固化奖励类型及其全部随机结果。</summary>
    public ExplorationRewardData GenerateReward(System.Random random)
    {
        var reward = new ExplorationRewardData
        {
            RewardType = RollRewardType(random)
        };

        switch (reward.RewardType)
        {
            case ExplorationRewardType.Gold:
                reward.GoldAmount = RollGold(random);
                break;

            case ExplorationRewardType.MilitaryUnit:
                int unitCount = Mathf.Max(0, RollUnitCount(random));
                reward.UnitConfigs = new UnitConfigSO[unitCount];
                for (int i = 0; i < unitCount; i++)
                {
                    reward.UnitConfigs[i] = RollUnitConfig(random);
                }
                break;

            case ExplorationRewardType.TacticalCard:
                reward.TacticalCard = RollTacticalCard(random);
                break;

            case ExplorationRewardType.Building:
                reward.BuildingConfig = RollBuildingConfig(random);
                reward.GoldAmount = RollGold(random);
                break;
        }

        return reward;
    }
}
