using System;
using System.Collections.Generic;

/// <summary>
/// 探索统一广播的三个阶段。
/// </summary>
public enum ExplorationBroadcastPhase
{
    /// <summary>探索事务成功，原始奖励快照已经由服务唯一物化。</summary>
    Explored,

    /// <summary>玩家或 AI 奖励处理器完成实际结算。</summary>
    Settled,

    /// <summary>玩家探索动画到达奖励表现点，或动画异常时执行兜底。</summary>
    RewardPoint,
}

/// <summary>
/// 探索统一广播的不可变载荷。
/// 区分"原始奖励"（地图生成时固化）与"实际结算结果"（奖励系统实际发放），
/// 携带地块、阵营、阶段、原始/实际奖励及其对应数据。
/// 载荷对象不可变；UnitConfigs 在 Explored 构造时复制一次并以 IReadOnlyList 暴露，
/// 防止一个订阅者修改集合影响其他订阅者（SO 配置资源本身仍为共享只读引用）。
/// </summary>
public sealed class ExplorationAcquisition
{
    private readonly IReadOnlyList<UnitConfigSO> _unitConfigs;

    public HexCellData Cell { get; }
    public int FactionId { get; }
    public ExplorationBroadcastPhase Phase { get; }
    public bool HasRewardSnapshot { get; }

    public ExplorationRewardConfigSO.ExplorationRewardType OriginalRewardType { get; }
    public int OriginalGoldAmount { get; }
    public IReadOnlyList<UnitConfigSO> UnitConfigs => _unitConfigs;
    public TacticalCardSO TacticalCard { get; }
    public BuildingConfigSO BuildingConfig { get; }

    public ExplorationRewardConfigSO.ExplorationRewardType SettledRewardType { get; }
    public int SettledGoldAmount { get; }

    private ExplorationAcquisition(
        HexCellData cell,
        int factionId,
        ExplorationBroadcastPhase phase,
        bool hasRewardSnapshot,
        ExplorationRewardConfigSO.ExplorationRewardType originalRewardType,
        int originalGoldAmount,
        IReadOnlyList<UnitConfigSO> unitConfigs,
        TacticalCardSO tacticalCard,
        BuildingConfigSO buildingConfig,
        ExplorationRewardConfigSO.ExplorationRewardType settledRewardType,
        int settledGoldAmount)
    {
        Cell = cell;
        FactionId = factionId;
        Phase = phase;
        HasRewardSnapshot = hasRewardSnapshot;
        OriginalRewardType = originalRewardType;
        OriginalGoldAmount = originalGoldAmount;
        _unitConfigs = unitConfigs;
        TacticalCard = tacticalCard;
        BuildingConfig = buildingConfig;
        SettledRewardType = settledRewardType;
        SettledGoldAmount = settledGoldAmount;
    }

    /// <summary>
    /// 构造 Explored 阶段载荷。
    /// reward == null 表示缺失快照（区别于显式 RewardType.None），HasRewardSnapshot=false。
    /// </summary>
    public static ExplorationAcquisition Explored(HexCellData cell, int factionId, ExplorationRewardData reward)
    {
        if (cell == null)
            throw new ArgumentNullException(nameof(cell));

        if (reward == null)
        {
            return new ExplorationAcquisition(
                cell,
                factionId,
                ExplorationBroadcastPhase.Explored,
                hasRewardSnapshot: false,
                originalRewardType: ExplorationRewardConfigSO.ExplorationRewardType.None,
                originalGoldAmount: 0,
                unitConfigs: Array.Empty<UnitConfigSO>(),
                tacticalCard: null,
                buildingConfig: null,
                settledRewardType: ExplorationRewardConfigSO.ExplorationRewardType.None,
                settledGoldAmount: 0);
        }

        return new ExplorationAcquisition(
            cell,
            factionId,
            ExplorationBroadcastPhase.Explored,
            hasRewardSnapshot: true,
            originalRewardType: reward.RewardType,
            originalGoldAmount: reward.GoldAmount,
            unitConfigs: CopyUnits(reward.UnitConfigs),
            tacticalCard: reward.TacticalCard,
            buildingConfig: reward.BuildingConfig,
            settledRewardType: ExplorationRewardConfigSO.ExplorationRewardType.None,
            settledGoldAmount: 0);
    }

    /// <summary>
    /// 从 Explored 载荷复制并切换为 Settled，写入实际结算结果（保留原始字段）。
    /// </summary>
    public ExplorationAcquisition SettledAs(
        ExplorationRewardConfigSO.ExplorationRewardType settledType,
        int settledGoldAmount = 0)
    {
        if (Phase != ExplorationBroadcastPhase.Explored)
            throw new InvalidOperationException($"SettledAs 只能从 Explored 载荷构造，当前阶段为 {Phase}。");
        if (settledGoldAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(settledGoldAmount), "结算金币不能为负。");

        return new ExplorationAcquisition(
            Cell,
            FactionId,
            ExplorationBroadcastPhase.Settled,
            HasRewardSnapshot,
            OriginalRewardType,
            OriginalGoldAmount,
            _unitConfigs,
            TacticalCard,
            BuildingConfig,
            settledType,
            settledGoldAmount);
    }

    /// <summary>从 Settled 载荷复制并切换为 RewardPoint，保留实际结算结果。</summary>
    public ExplorationAcquisition AtRewardPoint()
    {
        if (Phase != ExplorationBroadcastPhase.Settled)
            throw new InvalidOperationException($"AtRewardPoint 只能从 Settled 载荷构造，当前阶段为 {Phase}。");

        return new ExplorationAcquisition(
            Cell,
            FactionId,
            ExplorationBroadcastPhase.RewardPoint,
            HasRewardSnapshot,
            OriginalRewardType,
            OriginalGoldAmount,
            _unitConfigs,
            TacticalCard,
            BuildingConfig,
            SettledRewardType,
            SettledGoldAmount);
    }

    private static IReadOnlyList<UnitConfigSO> CopyUnits(UnitConfigSO[] source)
    {
        if (source == null || source.Length == 0)
            return Array.Empty<UnitConfigSO>();

        var copy = new UnitConfigSO[source.Length];
        Array.Copy(source, copy, source.Length);
        return Array.AsReadOnly(copy);
    }
}
