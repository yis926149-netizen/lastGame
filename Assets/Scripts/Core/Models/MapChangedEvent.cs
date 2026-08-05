using System.Collections.Generic;
using UnityEngine;

//****************************************
// 【动态地图-阶段二/阶段五】最小强类型地图变化事件（MapChangedEvent）
// 阶段二只广播 Committed 阶段；阶段四再扩展 TransitionStarted/Finalized/Cancelled。
// 阶段五补充 AffectedChunks（脏范围诊断，§阶段五-诊断扩展）。
// 兼容层：旧 MapVisualEventSO.Raise() 固定在 Committed 后调用一次。
//****************************************

/// <summary>脏位标记：哪些子系统需要联动处理（§八）。</summary>
[System.Flags]
public enum MapDirtyFlags
{
    None       = 0,
    Terrain    = 1 << 0,   // 高度/地形 → 地形 mesh
    Water      = 1 << 1,
    River      = 1 << 2,
    Grid       = 1 << 3,
    Objects    = 1 << 4,   // 地貌/资源模型
    Fog        = 1 << 5,   // 永久/临时可见性变化（由可见性服务触发）
    Navigation = 1 << 6,   // 寻路/移动
    Labels     = 1 << 7,   // 探索费用标签
    Territory  = 1 << 8,   // 归属
    Logistics  = 1 << 9    // 后勤重算
}

/// <summary>事务阶段：阶段二仅 Committed；阶段四扩展动画阶段。</summary>
public enum MapChangedPhase
{
    Committed,
    TransitionStarted,
    Finalized,
    Cancelled
}

/// <summary>强类型地图变化事件参数（阶段二：仅 Committed；阶段五：+AffectedChunks 诊断）。</summary>
public sealed class MapChangedEvent
{
    public int CommitId { get; }
    public IReadOnlyList<HexCellData> ChangedCells { get; }
    public MapDirtyFlags DirtyFlags { get; }
    public MapChangedPhase Phase { get; }

    /// <summary>受影响（脏）Chunk 列表，供诊断和调试可视化使用。</summary>
    public IReadOnlyList<ChunkIndex> AffectedChunks { get; }

    public MapChangedEvent(int commitId, IReadOnlyList<HexCellData> changedCells, MapDirtyFlags dirtyFlags, MapChangedPhase phase, IReadOnlyList<ChunkIndex> affectedChunks = null)
    {
        CommitId = commitId;
        ChangedCells = changedCells;
        DirtyFlags = dirtyFlags;
        Phase = phase;
        AffectedChunks = affectedChunks ?? new List<ChunkIndex>();
    }
}

/// <summary>
/// 错峰模式（§13.7）：动画期间各格按模式错开启动时机。
/// 已实现 Simultaneous / CenterToOuter / Wave（行粒度：同行同延迟、行间阶梯接续，2026-08-05）；
/// OuterToCenter / Random / Directional 保留后续接入（当前降级为 Simultaneous 并警告）。
/// </summary>
public enum MapTransitionStagger
{
    Simultaneous,
    CenterToOuter,
    OuterToCenter,
    Random,
    Directional,
    Wave
}

/// <summary>
/// 地图变化过渡选项。阶段二仅支持 Duration=0（同步提交）；
/// 阶段四接入 Duration&gt;0 的 Shader 顶点动画（§13.7 / §20-10）。
/// </summary>
public sealed class MapTransitionOptions
{
    /// <summary>动画时长（秒）。0=同步提交，不产生动画。</summary>
    public float Duration = 0f;

    /// <summary>动画期间是否锁定受影响格（§20-5）。</summary>
    public bool LockAffectedCells = true;

    /// <summary>缓动曲线；null 时使用默认 smoothstep（§13.7）。</summary>
    public AnimationCurve Easing;

    /// <summary>错峰模式（已支持 Simultaneous / CenterToOuter / Wave 行粒度）。</summary>
    public MapTransitionStagger Stagger = MapTransitionStagger.Simultaneous;

    /// <summary>错峰中心格（CenterToOuter/OuterToCenter 用，§13.2 中心向外）。</summary>
    public HexCellData StaggerCenter;
}

/// <summary>提交结果（调试/测试用；阶段五起含 AffectedChunks 诊断）。</summary>
public sealed class MapCommitResult
{
    public int CommitId { get; }
    public IReadOnlyList<HexCellData> ChangedCells { get; }
    public MapDirtyFlags DirtyFlags { get; }

    /// <summary>【阶段五-诊断】受影响（脏）Chunk 列表（Chunked 后端分帧提交中可能为 null/空）。</summary>
    public IReadOnlyList<ChunkIndex> AffectedChunks { get; }

    public MapCommitResult(int commitId, IReadOnlyList<HexCellData> changedCells, MapDirtyFlags dirtyFlags, IReadOnlyList<ChunkIndex> affectedChunks = null)
    {
        CommitId = commitId;
        ChangedCells = changedCells;
        DirtyFlags = dirtyFlags;
        AffectedChunks = affectedChunks;
    }
}

/// <summary>
/// 待移除视觉句柄（§12.1/12.3）：Commit 把旧模型/网格所有权转交句柄，
/// Finalize（阶段二为同一同步调用末尾，阶段四为动画结束）统一销毁。
/// </summary>
public sealed class RemovedVisualHandle
{
    private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

    public void Add(UnityEngine.Object target)
    {
        if (target != null) _objects.Add(target);
    }

    public bool IsEmpty => _objects.Count == 0;

    /// <summary>只读对象列表（阶段四：动画期间模型下沉/溶解用，§13.4）。</summary>
    public IReadOnlyList<UnityEngine.Object> Objects => _objects;

    /// <summary>销毁全部被收集对象并清空。幂等。</summary>
    public void DestroyAll()
    {
        foreach (UnityEngine.Object target in _objects)
        {
            if (target != null)
                UnityEngine.Object.Destroy(target);
        }
        _objects.Clear();
    }
}
