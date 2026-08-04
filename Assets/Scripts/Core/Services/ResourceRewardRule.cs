/// <summary>
/// 地图资源探索收割奖励规则（纯函数）。
/// 【地图资源配置化】收敛原 ExplorationService / AIAutoExplorer / PublicBuildingBase 三处镜像重复的
/// 金币换算 switch（对齐 CardGenerationRule 的单一规则模式）。
/// </summary>
public static class ResourceRewardRule
{
    /// <summary>
    /// 探索收割金币 = 数据库基础奖励 + 资源配置加成；无资源格只给基础奖励。
    /// </summary>
    public static int ComputeExplorationReward(int baseReward, MapResourceSO resource)
    {
        return baseReward + (resource == null ? 0 : resource.explorationGoldBonus);
    }
}
