using UnityEngine;
using Zenject;

//****************************************
// 【动态地图-能力测试】鼠标指格地形高度微调测试控制器（MapHeightEditTestController）
// 鼠标悬停地图地块（Selection 通道青色高亮指示）；按住 R 高度永久 +1、按住 F 永久 -1：
//   - 单按立即提交一次；按住后每 CommitIntervalSeconds（0.1s）连发一次，10Hz；
//   - Duration=0 同步提交（连发稳定、无动画排队）；如需观感改 AnimationDuration=0.3f；
//   - 不跳过水域格：±1 跨过 WaterLevel=1 阈值时经 MapMutationService.ApplyPatch
//     触发水陆双向重置（河流/海岸/movementCost），陆地→水域还会弹射格上单位——
//     这些联动即本测试的极端情况考察点；
//   - 高亮仅在指针格变化时重建（HexHighlightRenderer.RebuildChannel 每帧重建成本）；
//   - 指针在 UI 上时不取格、不响应（与卡牌拖拽同口径）。
// 仅编辑器/开发构建绑定（GameInstaller #if UNITY_EDITOR || DEVELOPMENT_BUILD）。
// 设计讨论与极端情况测试清单见根目录《鼠标指格地形高度微调测试-RF键实现方案.md》。
//****************************************

public class MapHeightEditTestController : ITickable
{
    /// <summary>按住连发的提交间隔（秒）→ 10Hz。</summary>
    public const float CommitIntervalSeconds = 0.1f;

    /// <summary>单次高度变化量（地形海拔级数）。</summary>
    public const float Step = 1f;

    /// <summary>0=同步提交（推荐，连发稳定）；>0 时同格连发会强制完成相交旧动画。</summary>
    public const float AnimationDuration = 0f;

    private readonly IInputService _input;
    private readonly IMapDataService _mapDataService;
    private readonly MapMutationService _mutationService;
    private readonly HexHighlightRenderer _highlightRenderer;

    private HexCellData _hoveredCell;
    private float _nextCommitTime;

    public MapHeightEditTestController(
        IInputService input,
        IMapDataService mapDataService,
        MapMutationService mutationService,
        [Zenject.InjectOptional] HexHighlightRenderer highlightRenderer = null)
    {
        _input = input;
        _mapDataService = mapDataService;
        _mutationService = mutationService;
        _highlightRenderer = highlightRenderer;
    }

    public void Tick()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // UI 阻挡：指针在 UI 上时既不取格也不响应（清高亮，防隔着面板改地形）
        if (_input.IsPointerOverUI())
        {
            RefreshHover(null);
            return;
        }

        RefreshHover(ResolveHoveredCell());
        if (_hoveredCell == null) return;

        bool holdingR = _input.GetKey(KeyCode.R);
        bool holdingF = _input.GetKey(KeyCode.F);
        if (!holdingR && !holdingF) return;

        // 按下瞬间立即提交；随后按 CommitIntervalSeconds 连发
        bool trigger = _input.GetKeyDown(KeyCode.R) || _input.GetKeyDown(KeyCode.F) ||
                       Time.realtimeSinceStartup >= _nextCommitTime;
        if (!trigger) return;

        ApplyHeight(_hoveredCell, holdingR ? +Step : -Step);
        _nextCommitTime = Time.realtimeSinceStartup + CommitIntervalSeconds;
    }
#endif

    /// <summary>屏幕射线取指针格（与 PlayerInputHandler.HighlightGridOnMouseHover 同范式）。</summary>
    private HexCellData ResolveHoveredCell()
    {
        if (_input.RaycastFromScreen(_input.MousePosition, out RaycastHit hit, 100f, LayerMask.GetMask("Map")))
            return _mapDataService.GetCellByWorldPosition(hit.point);
        return null;
    }

    /// <summary>高亮仅在指针格变化时重建（RebuildChannel 每次调用都会重建 mesh）。
    /// 【程序化山脉-阶段6.4】调试豁免：本控制器是开发调试工具（仅编辑器/开发构建绑定），
    /// 使用显式诊断豁免入口——被高度编辑的格即使是山格也必须可见指示，
    /// 不被玩家可见通道的山格门禁吞掉。</summary>
    private void RefreshHover(HexCellData cell)
    {
        if (cell == _hoveredCell) return;
        _hoveredCell = cell;
        if (_highlightRenderer == null) return;
        if (cell == null)
            _highlightRenderer.ClearChannel(HexHighlightChannel.Selection);
        else
            _highlightRenderer.SetHighlightedCellsDiagnostic(HexHighlightChannel.Selection, new[] { cell }, Color.cyan);
    }

    /// <summary>单格高度 ±Step 的完整事务（读的是 cell 最新 Height，连发天然累加）。
    /// try/catch 兜底（审计 2026-08-05）：Commit 自身有 finally 清事务（MapMutationService.cs:272-280），
    /// 但 BeginTransaction→Apply 之间若抛异常会残留 _inTransaction=true 毒化整个会话
    /// （10Hz 连发下必须防御）——异常时调用幂等 Rollback 后 rethrow，下帧可继续。</summary>
    private void ApplyHeight(HexCellData cell, float delta)
    {
        try
        {
            _mutationService.BeginTransaction();
            _mutationService.Apply(cell, HexCellPatch.HeightPatch(cell.Height + delta));
            MapCommitResult result = _mutationService.Commit(new MapTransitionOptions
            {
                Duration = AnimationDuration,
                Stagger = MapTransitionStagger.Simultaneous,
                LockAffectedCells = true
            });
            if (result != null)
                Debug.Log($"[MapHeightEdit] 格 {cell.GenerateOrder} 高度={cell.Height}（" +
                          $"脏 Chunk {result.AffectedChunks?.Count ?? 0}；水域跨界联动由 ApplyPatch 自动处理）");
        }
        catch
        {
            _mutationService.Rollback();
            throw;
        }
    }
}
