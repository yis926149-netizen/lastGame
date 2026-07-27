using UnityEngine;

/// <summary>
/// 探索奖励配置 ScriptableObject。
/// 定义探索地块时的随机金币和单位奖励档位表。
/// </summary>
[CreateAssetMenu(fileName = "ExplorationRewardConfig", menuName = "Game/Exploration Reward Config")]
public class ExplorationRewardConfigSO : ScriptableObject
{
    [Header("金币奖励档位")]
    [Tooltip("金币奖励档位数组，掷骰随机选择一档。索引 0 = 档位 0，索引 1 = 档位 1，以此类推。")]
    public int[] goldTiers = new int[] { 0, 25, 50, 100, 200, 400 };

    [Header("单位奖励档位")]
    [Tooltip("单位数量奖励档位数组，掷骰随机选择一档。")]
    public int[] unitCountTiers = new int[] { 0, 1, 2, 3, 4, 5 };

    [Header("奖励单位类型")]
    [Tooltip("奖励生成的单位 ID（UnitDatabase 索引）。当前固定为仙人掌（ID 2）。")]
    public int rewardUnitID = 2; // 仙人掌

    /// <summary>掷金币骰子，返回金币数量</summary>
    public int RollGold()
    {
        if (goldTiers == null || goldTiers.Length == 0) return 0;
        return goldTiers[Random.Range(0, goldTiers.Length)];
    }

    /// <summary>掷单位骰子，返回单位数量</summary>
    public int RollUnitCount()
    {
        if (unitCountTiers == null || unitCountTiers.Length == 0) return 0;
        return unitCountTiers[Random.Range(0, unitCountTiers.Length)];
    }
}
