/// <summary>
/// 地图生成时固化到地块上的探索奖励快照。
/// 探索结算只消费该快照，不再进行随机抽取。
/// </summary>
public sealed class ExplorationRewardData
{
    public ExplorationRewardConfigSO.ExplorationRewardType RewardType;
    public int GoldAmount;
    public UnitConfigSO[] UnitConfigs;
    public TacticalCardSO TacticalCard;
    public BuildingConfigSO BuildingConfig;
}
