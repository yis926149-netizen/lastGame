/// <summary>
/// 探索奖励建筑放置规则（纯函数）。
/// 玩家侧（ExplorationRewardSystem）与 AI 侧（AIAutoExplorer）奖励结算共用，
/// 与卡牌部署路径（CardPresenter.IsReleaseValid）同一套格级资格，避免双阵营规则漂移。
/// </summary>
public static class RewardBuildingRule
{
    /// <summary>
    /// 奖励建筑可否放置在目标格：山格/水域/禁建地貌不可建造，
    /// 格上已有建筑（含公共建筑）或单位不可放置。
    /// </summary>
    public static bool CanPlace(HexCellData cell)
    {
        return cell != null
            && MountainCellRule.CanBuildOnCell(cell)
            && cell.BulidingTypeOnHex_Building.Key == Enums.BulidingType.NoBuilding
            && !cell.IsHaveUnit();
    }
}
