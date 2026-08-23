using System.Collections.Generic;
using UnityEngine;
using Zenject;

//****************************************
// 【动态地图-能力测试】全地图波浪式上下变化测试控制器（MapWaveTestController）
// 按 V 键触发一次移动波带（纯视觉脉冲，最终形态 2026-08-05）：
//   Commit#1：临时 +RiseHeightLevels 生成 oldY/targetY 缓存（Chunk 不提交 staging mesh，
//   仅提取 targetY/行延迟），Commit 返回后立即恢复逻辑 Height/RealCenterWorldCoordinate
//   （RestoreLogicalCellState）——波浪期间地图逻辑数据始终为原海拔；
//   视觉 Wave 在每行窗口内 0→1→0 脉冲（约 6 行宽波带，单行时长 ≈0.31s@4s）；
//   Finalized 后以 Duration=0 提交一次精确重建稳定网格（RestoreOriginalHeights）。
// 范围策略：
//   - 跳过水域格（WaterLevelConfig.IsWater 绝对阈值 WaterLevel=1——全图 +Δ 会把
//     湖海误判为陆地触发水陆双向重置，MapMutationService.ApplyPatch）；
//   - 全图陆地格均参与（含竞技场预留区，2026-08-05 调整：测试竞技场区域地块的波浪表现）。
// 验证配套：MapMutationDiagnostics.EnableCommitLogging（两次提交的耗时/脏格/脏 Chunk 数）、
//   EnableDirtyChunkHighlight（脏格高亮）；动画期间可观察交互锁/单位视觉跟随/事件四阶段。
// 注：全图波浪混有"未参与动画的水域格"，keep-below clip 平面（按参与格最低 startY 起算）
//   会裁掉同 Chunk 内不参与动画的更低格 → 测试期间自动置
//   MapMutationDiagnostics.DisableKeepBelowClip=true（ChunkMapRenderer 配合），
//   完成/异常/取消路径统一恢复 false。
// 本控制器的完整修订史（曲面→阶梯→移动波峰→纯视觉脉冲→FogConnector/Shader 稳定）见
//   动态地图/全地图波浪式上下变化测试.md；其对动画管线的设计约束反哺见
//   动态地图/动态地图变化与分块重建方案.md 末章。
//****************************************

public class MapWaveTestController : ITickable
{
    public enum WaveTestState
    {
        Idle,
        Rising,
        Falling
    }

    /// <summary>波浪提升量 = 4 个地形海拔（Height 级差）。世界 Y 差 = 级数 × elevationStep
    /// （当前配置 elevationStep=1 → 4 世界单位）。陆地格 Height&gt;WaterLevel=1，+4 后仍为陆地；
    /// 水域格在 BeginRise 中先行排除，不会误判触发水陆双向重置。</summary>
    public const float RiseHeightLevels = 4f;

    /// <summary>单程动画时长（秒）。32 行行间距约 0.10s；约 6 行宽的波带在窗口内升起→回落。</summary>
    public const float DurationSeconds = 4f;

    private readonly IMapDataService _mapDataService;
    private readonly MapMutationService _mutationService;

    private List<HexCellData> _cells = new List<HexCellData>();
    private Dictionary<int, float> _originalHeights = new Dictionary<int, float>();
    private Dictionary<int, Vector3> _originalCenters = new Dictionary<int, Vector3>();
    private int _activeCommitId = -1;

    public WaveTestState State { get; private set; } = WaveTestState.Idle;

    public MapWaveTestController(
        IMapDataService mapDataService,
        MapMutationService mutationService)
    {
        _mapDataService = mapDataService;
        _mutationService = mutationService;
        _mutationService.MapChanged += OnMapChanged;
    }

    public void Tick()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.V) && State == WaveTestState.Idle)
            BeginRise();
#endif
    }

    /// <summary>强制结束测试并复位开关（对局结束/调试兜底）。幂等。</summary>
    public void Reset()
    {
        State = WaveTestState.Idle;
        _activeCommitId = -1;
        MapMutationDiagnostics.DisableKeepBelowClip = false;
    }

    private void BeginRise()
    {
        IReadOnlyList<HexCellData> allCells = _mapDataService.GetAllCells();
        if (allCells == null || allCells.Count == 0)
        {
            Debug.LogWarning("[MapWaveTest] 地图尚未生成，V 键波浪测试忽略。");
            return;
        }

        _cells.Clear();
        _originalHeights.Clear();
        _originalCenters.Clear();
        foreach (HexCellData cell in allCells)
        {
            if (cell == null) continue;
            if (WaterLevelConfig.IsWater(cell)) continue;                // 跳过水域（绝对高度判定）
            _cells.Add(cell);
            _originalHeights[cell.GenerateOrder] = cell.Height;
            _originalCenters[cell.GenerateOrder] = cell.RealCenterWorldCoordinate;
        }
        if (_cells.Count == 0)
        {
            Debug.LogWarning("[MapWaveTest] 没有可变化的陆地格（全图水域？），忽略。");
            return;
        }

        // 测试模式：禁用 keep-below clip 顶出（全图混有未参与动画的低格会被裁掉）
        MapMutationDiagnostics.DisableKeepBelowClip = true;

        try
        {
            _mutationService.BeginTransaction();
            foreach (HexCellData cell in _cells)
                _mutationService.Apply(cell, HexCellPatch.HeightPatch(cell.Height + RiseHeightLevels));
            MapCommitResult result = _mutationService.Commit(new MapTransitionOptions
            {
                Duration = DurationSeconds,
                Stagger = MapTransitionStagger.Wave,
                LockAffectedCells = true
            });

            if (result != null)
            {
                _activeCommitId = result.CommitId;
                State = WaveTestState.Rising;
                // Commit 已把 oldY/targetY 缓存在 Chunk UV2 与视觉过渡服务中。Wave 之后只需这些
                // 缓存，不应让逻辑 Height 在整个动画期间保持 +4，否则所有查询 Cell 的系统都会
                // 把地图视为整体抬高。立即恢复逻辑数据，顶点脉冲仍可正常使用缓存目标。
                RestoreLogicalCellState();
                Debug.Log($"[MapWaveTest] 波浪升起提交完成：Commit#{result.CommitId}、格 {result.ChangedCells.Count}、" +
                          $"脏 Chunk {result.AffectedChunks?.Count ?? 0}（动画 {DurationSeconds}s 整行接续推进；" +
                          $"水域 {allCells.Count - _cells.Count} 格已排除，含竞技场预留区的全图陆地格均参与）。");
            }
        }
        catch
        {
            MapMutationDiagnostics.DisableKeepBelowClip = false;
            State = WaveTestState.Idle;
            throw;
        }
    }

    private void RestoreOriginalHeights()
    {
        try
        {
            State = WaveTestState.Falling;
            _mutationService.BeginTransaction();
            foreach (HexCellData cell in _cells)
            {
                if (_originalHeights.TryGetValue(cell.GenerateOrder, out float original))
                    _mutationService.Apply(cell, HexCellPatch.HeightPatch(original));
            }
            MapCommitResult result = _mutationService.Commit(new MapTransitionOptions
            {
                Duration = 0f,
                Stagger = MapTransitionStagger.Simultaneous,
                LockAffectedCells = true
            });

            if (result != null)
            {
                Debug.Log($"[MapWaveTest] 波带结束，已同步恢复原海拔：Commit#{result.CommitId}、" +
                          $"格 {result.ChangedCells.Count}、脏 Chunk {result.AffectedChunks?.Count ?? 0}。");
            }
            FinishTest();
        }
        catch
        {
            MapMutationDiagnostics.DisableKeepBelowClip = false;
            State = WaveTestState.Idle;
            throw;
        }
    }

    private void RestoreLogicalCellState()
    {
        foreach (HexCellData cell in _cells)
        {
            if (cell == null) continue;
            if (_originalHeights.TryGetValue(cell.GenerateOrder, out float height))
                cell.Height = height;
            if (_originalCenters.TryGetValue(cell.GenerateOrder, out Vector3 center))
                cell.RealCenterWorldCoordinate = center;
        }
    }

    private void OnMapChanged(MapChangedEvent e)
    {
        if (e.CommitId != _activeCommitId) return;

        if (e.Phase == MapChangedPhase.Finalized)
        {
            if (State == WaveTestState.Rising)
            {
                RestoreOriginalHeights();
            }
        }
        else if (e.Phase == MapChangedPhase.Cancelled)
        {
            // 动画被强制完成（对局结束/相交 Chunk 冲突提交）：复位状态并恢复开关
            FinishTest();
        }
    }

    private void FinishTest()
    {
        Reset();
        Debug.Log("[MapWaveTest] 全地图波浪式上下变化结束：地形已回落原高度（或动画被强制完成），V 键可再次触发。");
    }
}
