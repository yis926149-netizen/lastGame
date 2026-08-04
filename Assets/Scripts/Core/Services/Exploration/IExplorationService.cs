using System;

/// <summary>
/// 探索服务接口：主动探索地块的核心入口。
/// 【探索重构-阶段3】新增服务，替代旧的自动探索逻辑。
/// 【统一开发入口】TryExplore 增加阵营参数：玩家与 AI 共用同一服务，
/// 服务内校验（含目标格中立校验）保证同时开发互斥（先到先得，后到不扣费）。
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
    /// 完成探索后逻辑（领土、收割、视觉刷新），由动画回调在柱体特效结束后触发。
    /// </summary>
    void CompleteExploration(HexCellData cell);

    /// <summary>
    /// 探索成功事件：供 UI/视觉/音效等订阅。
    /// 事件参数：被探索的格子。
    /// </summary>
    event Action<HexCellData> CellExplored;

    /// <summary>
    /// 探索奖励触发事件：在探索成功时触发，供奖励系统按阵营订阅。
    /// 事件参数：被探索的格子、开发方阵营（0=玩家，1=AI）。
    /// </summary>
    event Action<HexCellData, int> ExplorationRewardTriggered;
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
