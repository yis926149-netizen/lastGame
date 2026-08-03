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

    [Header("第二次随机：金币档位（金币奖励）")]
    [Tooltip("金币奖励档位数组，等概率随机选择一档。")]
    public int[] goldTiers = new int[] { 25, 50, 100, 200, 400 };

    [Header("第二次随机：军事单位档位（军事单位奖励）")]
    [Tooltip("单位数量档位数组，等概率随机选择一档。")]
    public int[] unitCountTiers = new int[] { 1, 2, 3, 4, 5 };

    [Tooltip("奖励可生成的单位配置（生成每个单位时随机选取）。")]
    public UnitConfigSO[] rewardUnits;

    [Header("第二次随机：战术卡牌（战术卡牌奖励）")]
    [Tooltip("战术卡牌数据库：战术奖励时从中随机抽取一张。")]
    public TacticalCardDatabaseSO tacticalCardDatabase;

    /// <summary>第一次掷骰：按权重返回奖励类型。</summary>
    public ExplorationRewardType RollRewardType()
    {
        int total = noneRewardWeight + goldRewardWeight + militaryRewardWeight + tacticalRewardWeight;
        if (total <= 0) return ExplorationRewardType.None;

        int roll = Random.Range(0, total);
        if (roll < noneRewardWeight) return ExplorationRewardType.None;
        roll -= noneRewardWeight;
        if (roll < goldRewardWeight) return ExplorationRewardType.Gold;
        roll -= goldRewardWeight;
        if (roll < militaryRewardWeight) return ExplorationRewardType.MilitaryUnit;
        return ExplorationRewardType.TacticalCard;
    }

    /// <summary>第二次掷骰（金币）：返回金币数量。</summary>
    public int RollGold()
    {
        if (goldTiers == null || goldTiers.Length == 0) return 0;
        return goldTiers[Random.Range(0, goldTiers.Length)];
    }

    /// <summary>第二次掷骰（军事）：返回单位数量。</summary>
    public int RollUnitCount()
    {
        if (unitCountTiers == null || unitCountTiers.Length == 0) return 0;
        return unitCountTiers[Random.Range(0, unitCountTiers.Length)];
    }

    /// <summary>第二次掷骰（军事）：从奖励单位配置数组中随机返回一个配置；空数组返回 null（不再回退魔法 ID）。</summary>
    public UnitConfigSO RollUnitConfig()
    {
        if (rewardUnits == null || rewardUnits.Length == 0) return null;
        return rewardUnits[Random.Range(0, rewardUnits.Length)];
    }

    /// <summary>第二次掷骰（战术）：从战术卡牌数据库随机抽取一张，返回 null 表示无可发牌。</summary>
    public TacticalCardSO RollTacticalCard()
    {
        if (tacticalCardDatabase == null || tacticalCardDatabase.cards == null ||
            tacticalCardDatabase.cards.Count == 0)
        {
            return null;
        }
        return tacticalCardDatabase.cards[Random.Range(0, tacticalCardDatabase.cards.Count)];
    }
}
