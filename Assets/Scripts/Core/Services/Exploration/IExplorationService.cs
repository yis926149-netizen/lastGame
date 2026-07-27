using System;

/// <summary>
/// 探索服务接口：主动探索地块的核心入口。
/// 【探索重构-阶段3】新增服务，替代旧的自动探索逻辑。
/// </summary>
public interface IExplorationService
{
    ExploreResult TryExplore(HexCellData targetCell);

    /// <summary>
    /// 完成探索后逻辑（领土、收割、视觉刷新），由动画回调在柱体特效结束后触发。
    /// </summary>
    void CompleteExploration(HexCellData cell);

    /// <summary>
    /// 探索成功事件：供 UI/视觉/音效等订阅。
    /// 事件参数：被探索的格子。
    /// </summary>
    event Action<HexCellData> CellExplored;

    /// <summary>
    /// 探索奖励触发事件：在柱体特效溶解开始时触发，供奖励系统订阅。
    /// 事件参数：被探索的格子。
    /// </summary>
    event Action<HexCellData> ExplorationRewardTriggered;
}

/// <summary>
/// 探索结果枚举
/// </summary>
public enum ExploreResult
{
    Success,                // 成功
    AlreadyExplored,        // 已探索
    NotAdjacent,            // 不邻接已探索区域（邻接规则不满足）
    InsufficientResources,  // 资源不足
    Unexplorable,           // 不可探索（公共建筑占位区域）
    RuleFailed              // 其他规则校验失败
}
