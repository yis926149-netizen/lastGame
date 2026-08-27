/// <summary>
/// 探索服务接口：主动探索地块的核心入口。
/// 【探索重构-阶段3】新增服务，替代旧的自动探索逻辑。
/// 【统一开发入口】TryExplore 增加阵营参数：玩家与 AI 共用同一服务，
/// 服务内校验（含目标格中立校验）保证同时开发互斥（先到先得，后到不扣费）。
/// 【探索结果纯广播】探索结果统一通过 IExplorationBroadcastSource 广播，
/// 本接口不再暴露 CellExplored / ExplorationRewardTriggered / CompleteExploration 等旧事件。
/// </summary>
public interface IExplorationService
{
    /// <summary>
    /// 尝试为指定阵营开发一个地块。
    /// 校验、扣费、归属写入在同一方法内同步完成，天然互斥。
    /// </summary>
    /// <param name="targetCell">目标地块</param>
    /// <param name="factionId">开发方阵营（0=玩家，1=AI）</param>
    ExploreResult TryExplore(HexCellData targetCell, int factionId);

    /// <summary>
    /// 玩家探索动画到达奖励表现点（石柱溶解30% / 飞盘撞击）时调用。
    /// 幂等：同一地块只有第一次成功调用会发布 RewardPoint；动画异常由服务超时兜底。
    /// </summary>
    /// <param name="targetCell">动画完成的地块</param>
    void SignalRewardPoint(HexCellData targetCell);
}

/// <summary>
/// 探索结果枚举
/// </summary>
public enum ExploreResult
{
    Success,                // 成功
    AlreadyExplored,        // 本阵营已探索
    NotAdjacent,            // 不邻接已探索区域（邻接规则不满足）
    InsufficientResources,  // 资源不足
    Unexplorable,           // 不可探索（公共建筑占位区域 / 敌方单位占格）
    NotNeutral,             // 地块已被任意一方取得归属（中立校验失败）
    RuleFailed              // 其他规则校验失败
}
