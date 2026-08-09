using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zenject;

//****************************************
// 【动态地图-阶段四/阶段五】地图视觉过渡服务（MapVisualTransitionService）
// 驱动 CPU 顶点动画（2026-08-05 起，顶点插值在 C# 侧执行；surface shader vert 读 UV 通道不可靠，
// 实机三次实验确认，详见 动态地图/全地图波浪式上下变化测试.md）：
// - 每 Chunk 用 MaterialPropertyBlock 提供 _ChunkProgress（0~1），每顶点 UV2/UV3 提供
//   startVertexY / targetVertexY / delayStart / delayEnd（窗口=end-start，Wave 行阶梯）。
// - 错峰：CenterToOuter 中心格先动、外环后动（§13.2）。
// - 动画期间受影响格保持 IMapInteractionGate 锁定（§20-5），结束后只解锁自己动画的格。
// - 单位视觉高度跟随动画（§13.6），动画结束吸附回最终 RealCenterWorldCoordinate。
// - 待移除模型动画期间下沉溶解，Finalize 统一销毁（§13.4 模型溶解）。
// 状态机：Stable → Preparing → Animating → Finalizing（§13.8，简化实现）。
// 阶段五（§阶段五-并行动画）：支持多个互不相交 Chunk 动画并行——BeginTransition 仅强制完成
// 与新动画 Chunk 相交的旧动画，不相交的动画并行推进；每个动画的 Finalize 只解锁自己的格
// （MapInteractionGate.UnlockCells），互不清锁。
//
// 【动画管线设计约束-2026-08-05（波浪测试反哺，详见 动态地图/动态地图变化与分块重建方案.md 末章）】
// ① 错峰延迟语义为 (delayStart, delayEnd)：每格动画时长 = end-start，可显式指定；
//    禁止回退 (1-delay) 归一化——早动格会占满整个时间线、传播前沿不可控（实机"曲面"观感）。
// ② RiseWindowKey=-1 是错峰字典保留键（行上升窗口）；GenerateOrder 恒 ≥0，无冲突。
// ③ Wave 脉冲语义为窗口内 0→1→0（AnimationReturnsToStart）；非 Wave 为单调 0→1。
// ④ GetAnimatedWorldY(cell) 是动画中"地图表面高度"的唯一查询入口；
//    外围系统（跟随物/FogConnector/城墙类）禁止直接读 RealCenterWorldCoordinate 当视觉高度。
// ⑤ 跟随物目标 Y 来自 ActiveMapTransition.TargetCenterWorldY 缓存，而非实时 Cell 坐标——
//    纯视觉脉冲会在 Commit 后立即恢复逻辑 Height/RealCenterWorldCoordinate。
//****************************************

/// <summary>活动动画的调度数据（一个 Commit 对应一个动画实例）。</summary>
public sealed class ActiveMapTransition
{
    public int CommitId;
    public MapDirtyFlags DirtyFlags;
    public float Duration;
    public float Elapsed;
    public AnimationCurve Easing;
    public MapTransitionOptions Options;

    public List<HexCellData> ChangedCells = new List<HexCellData>();
    public List<ChunkIndex> Chunks = new List<ChunkIndex>();
    public HashSet<HexCellData> ChangedSet = new HashSet<HexCellData>();

    /// <summary>GenerateOrder → 旧中心世界 Y（快照于写数据前，§12.1）。</summary>
    public IReadOnlyDictionary<int, float> OldCenterWorldY;

    /// <summary>GenerateOrder → 动画目标中心世界 Y。Wave 恢复逻辑 Cell 后仍由此驱动跟随物。</summary>
    public readonly Dictionary<int, float> TargetCenterWorldY = new Dictionary<int, float>();

    /// <summary>GenerateOrder → 错峰延迟 [0,1]。</summary>
    public IReadOnlyDictionary<int, float> StaggerDelays;

    /// <summary>待移除模型句柄（动画期间下沉，Finalize 销毁）。</summary>
    public RemovedVisualHandle Removed;

    /// <summary>当前缓动进度 [0,1]（含 easing）。</summary>
    public float EasedProgress;

    /// <summary>阶段事件发布回调（由 MapMutationService 提供，转发到 MapChanged）。</summary>
    public Action<MapChangedEvent> PhasePublisher;

    /// <summary>【阶段五】本动画的视觉跟随物（并行动画下各动画独立持有，§阶段五-并行动画）。</summary>
    public readonly Dictionary<Transform, HexCellData> Followers = new Dictionary<Transform, HexCellData>();

    public bool Completed;

    /// <summary>格子的本地动画进度（含错峰，§13.3 与 shader 同公式）。
    /// 【实机修订-2026-08-04】返回线性 local：EasedProgress 已是单次 smoothstep（Tick 内缓动），
    /// Shader 端同步改为线性插值——此前两端各做一次 smoothstep 形成双重缓动，外环 delay 结束后
    /// 陡峭冲起，观感"先突出一小段、再猛冲成型"。
    /// Wave 模式在窗口内执行 0→1→0 脉冲：一行随波带升起后立即回落，不在波后累积成高台。
    /// 非 Wave 模式无保留键，维持 0→1 的目标高度插值。</summary>
    public float LocalProgress(HexCellData cell)
    {
        float delay = 0f;
        float window = 0f;
        if (StaggerDelays != null && cell != null)
        {
            StaggerDelays.TryGetValue(cell.GenerateOrder, out delay);
            StaggerDelays.TryGetValue(MapVisualTransitionService.RiseWindowKey, out window);
        }
        if (window <= 0f) window = 1f - delay;
        float local = Mathf.Clamp01((EasedProgress - delay) / Mathf.Max(0.0001f, window));
        if (Options?.Stagger != MapTransitionStagger.Wave) return local;

        float pulse = 1f - Mathf.Abs(local * 2f - 1f);
        return pulse * pulse * (3f - 2f * pulse);
    }
}

public class MapVisualTransitionService : ITickable
{
    /// <summary>错峰延迟字典的保留键：Wave 模式的"行上升窗口"（时间线比例，每行同窗）。
    /// 字典按键为 GenerateOrder（恒 ≥0），-1 无冲突。LocalProgress 与 CPU 顶点动画共同读取，
    /// 保证单位/跟随物与 mesh 同公式。非 Wave 模式不写该键（回退 (1-delay) 旧公式）。</summary>
    public const int RiseWindowKey = -1;

    private readonly IMapRenderBackend _renderBackend;
    private readonly MapInteractionGate _interactionGate;
    private readonly UnitMovementSystem _unitMovementSystem;

    /// <summary>【阶段五】活动动画列表（支持不相交 Chunk 并行）。</summary>
    private readonly List<ActiveMapTransition> _actives = new List<ActiveMapTransition>();

    public bool IsAnimating => _actives.Count > 0;

    public bool IsTransitionActive(int commitId)
    {
        return _actives.Any(t => !t.Completed && t.CommitId == commitId);
    }

    public MapVisualTransitionService(
        IMapRenderBackend renderBackend,
        MapInteractionGate interactionGate,
        UnitMovementSystem unitMovementSystem)
    {
        _renderBackend = renderBackend;
        _interactionGate = interactionGate;
        _unitMovementSystem = unitMovementSystem;
    }

    // ── 错峰延迟计算（§13.7）────────────────────────────────

    /// <summary>
    /// 计算每格错峰延迟（GenerateOrder → [0,1] 归一化，乘 StaggerSpan 后为最大延迟比例）。
    /// Simultaneous：全部 0；CenterToOuter：按到中心 cube 距离归一化。
    /// 其他模式第一版降级为 Simultaneous 并警告（§13.7 后续接入）。
    /// </summary>
    public Dictionary<int, float> ComputeStaggerDelays(
        IReadOnlyCollection<HexCellData> cells,
        MapTransitionOptions options)
    {
        var result = new Dictionary<int, float>();
        if (cells == null) return result;

        // 【实机修订-2026-08-04】错峰上限 0.6 → 0.35：外环最迟延迟 35% 动画时长（原 60%），
        // 避免"中心先突起、外环久等后陡峭猛冲"的突兀观感（§13.2 中心向外错峰）
        const float StaggerSpan = 0.35f;

        MapTransitionStagger mode = options?.Stagger ?? MapTransitionStagger.Simultaneous;
        HexCellData center = options?.StaggerCenter;

        if (mode == MapTransitionStagger.CenterToOuter && center != null)
        {
            float maxDist = 0f;
            var dists = new Dictionary<int, float>();
            foreach (HexCellData cell in cells)
            {
                if (cell == null) continue;
                float d = CubeDistance(cell.HexCoordinate, center.HexCoordinate);
                dists[cell.GenerateOrder] = d;
                if (d > maxDist) maxDist = d;
            }
            foreach (var kv in dists)
            {
                float t = maxDist > 0f ? kv.Value / maxDist : 0f;
                result[kv.Key] = Mathf.Clamp01(t) * StaggerSpan;
            }
        }
        else if (mode == MapTransitionStagger.Wave)
        {
            // 【波浪-行粒度修订-2026-08-05】按"行"整行同延迟：同一行（HexCoordinate.z，
            // cube z = 生成行 j，见 MapGenerator.HexCoordinatesGeneration）所有格子共享
            // 同一错峰延迟，整行作为刚性平板升降；行间按行号归一化递增延迟，波浪从第 0 行
            // 推进到末行，观感为"一排地块接续升降"的阶梯波。此前按 GenerateOrder 逐格
            // 归一化（order = z*xNumber + x），行内列向也有微小延迟差，观感为对角连续
            // 曲面（麦浪），看不清"一排"的粒度。跨度 0.8：末行开始动画时前列已接近完成，
            // 形成接续推进感（全图推进需要比竞技场外环更大的错峰，故不沿用 StaggerSpan=0.35）。
            // Wave 为移动脉冲而非累积高台：窗口内每行 0→1→0，约 6 行宽波带中前半升起、
            // 后半回落。波带经过后地形已回原位，避免全图最终堆到 +4 海拔形成长斜坡/裂口。
            // 上限 1-WaveSpan 保证末行在动画结束前完成回落。
            // 窗口存于保留键 RiseWindowKey=-1，LocalProgress 与顶点动画统一读取。
            const float WaveSpan = 0.8f;
            const float WaveFrontRows = 6f;
            int minRow = int.MaxValue;
            int maxRow = int.MinValue;
            foreach (HexCellData c in cells)
            {
                if (c == null) continue;
                int row = Mathf.RoundToInt(c.HexCoordinate.z);
                if (row < minRow) minRow = row;
                if (row > maxRow) maxRow = row;
            }
            float range = Mathf.Max(1f, maxRow - minRow);
            int rowCount = maxRow - minRow + 1;
            float step = rowCount > 1 ? WaveSpan / (rowCount - 1) : WaveSpan;
            result[RiseWindowKey] = Mathf.Min(step * WaveFrontRows, 1f - WaveSpan);
            foreach (HexCellData c in cells)
            {
                if (c == null) continue;
                int row = Mathf.RoundToInt(c.HexCoordinate.z);
                result[c.GenerateOrder] = ((row - minRow) / range) * WaveSpan;
            }
        }
        else
        {
            if (mode != MapTransitionStagger.Simultaneous)
                Debug.LogWarning($"[MapVisualTransitionService] 错峰模式 {mode} 第一版未实现，已降级为 Simultaneous。");
            foreach (HexCellData cell in cells)
            {
                if (cell != null) result[cell.GenerateOrder] = 0f;
            }
        }
        return result;
    }

    // ── 动画生命周期 ─────────────────────────────────────────

    /// <summary>
    /// 启动动画（Commit 完成后调用）。Duration&lt;=0 时同步完成（Committed 后立即 Finalized）。
    /// 【阶段五】只强制完成与本次动画 Chunk 相交的旧动画；不相交的旧动画保持并行（§阶段五-并行动画）。
    /// 调用方应在加锁前先 ForceCompleteConflicting 保证锁顺序（§20-6），本方法内再做防御性检查。
    /// </summary>
    public void BeginTransition(
        int commitId,
        MapDirtyFlags dirtyFlags,
        IReadOnlyCollection<HexCellData> changedCells,
        MapTransitionOptions options,
        IReadOnlyList<ChunkIndex> chunks,
        IReadOnlyDictionary<int, float> oldCenterWorldY,
        IReadOnlyDictionary<int, float> staggerDelays,
        RemovedVisualHandle removed,
        Action<MapChangedEvent> phasePublisher)
    {
        // 防御性：完成与新动画 Chunk 相交的活动动画（正常情况下 Commit 已提前处理）
        if (chunks != null && chunks.Count > 0)
            CompleteConflicting(chunks);

        var transition = new ActiveMapTransition
        {
            CommitId = commitId,
            DirtyFlags = dirtyFlags,
            Duration = Mathf.Max(0f, options?.Duration ?? 0f),
            Easing = options?.Easing,
            Options = options ?? new MapTransitionOptions(),
            OldCenterWorldY = oldCenterWorldY,
            StaggerDelays = staggerDelays,
            Removed = removed ?? new RemovedVisualHandle(),
            PhasePublisher = phasePublisher
        };
        if (changedCells != null)
        {
            transition.ChangedCells.AddRange(changedCells);
            foreach (HexCellData cell in changedCells)
            {
                if (cell == null) continue;
                transition.ChangedSet.Add(cell);
                transition.TargetCenterWorldY[cell.GenerateOrder] = cell.RealCenterWorldCoordinate.y;
            }
        }
        if (chunks != null) transition.Chunks.AddRange(chunks);

        _actives.Add(transition);

        if (transition.Duration <= 0f)
        {
            // 同步路径：同一调用内完成（§12.3 Duration=0）
            Complete(transition, cancelled: false);
            return;
        }

        PublishPhase(transition, MapChangedPhase.TransitionStarted);
    }

    /// <summary>
    /// 【阶段五】强制完成与指定 Chunk 集合相交的活动动画（§阶段五-并行动画）。
    /// 不相交的动画保持并行。幂等。chunks 为 null（测试替身无法计算脏 Chunk）时
    /// 保守完成全部活动动画（等价阶段四的全局串行语义，保证锁顺序安全）。
    /// </summary>
    public void ForceCompleteConflicting(IReadOnlyList<ChunkIndex> chunks)
    {
        if (_actives.Count == 0) return;
        if (chunks == null)
        {
            ForceComplete();
            return;
        }
        if (chunks.Count == 0) return;
        CompleteConflicting(chunks);
    }

    private void CompleteConflicting(IReadOnlyList<ChunkIndex> chunks)
    {
        var incoming = new HashSet<ChunkIndex>(chunks);
        for (int i = _actives.Count - 1; i >= 0; i--)
        {
            ActiveMapTransition t = _actives[i];
            if (t.Completed) continue;
            bool conflicts = t.Chunks.Any(c => incoming.Contains(c));
            if (conflicts)
                Complete(t, cancelled: true);
        }
    }

    /// <summary>ITickable：逐帧推进所有活动动画（§13.5 逻辑在 Commit 帧已切换，视觉独立播放）。</summary>
    public void Tick() => Tick(Time.deltaTime);

    /// <summary>推进动画（显式 deltaTime，便于测试/固定步长驱动）。</summary>
    public void Tick(float deltaTime)
    {
        for (int i = _actives.Count - 1; i >= 0; i--)
        {
            ActiveMapTransition t = _actives[i];
            if (t.Completed) continue;

            t.Elapsed += deltaTime;
            float raw = Mathf.Clamp01(t.Elapsed / Mathf.Max(0.0001f, t.Duration));
            float eased = t.Easing != null ? Mathf.Clamp01(t.Easing.Evaluate(raw)) : SmoothStep(raw);
            t.EasedProgress = eased;

            // 每 Chunk 驱动 MPB 进度（§20-10）
            foreach (ChunkIndex chunk in t.Chunks)
                _renderBackend.SetChunkAnimationProgress(chunk, eased);

            // 单位视觉高度跟随（§13.6）
            UpdateUnitVisualOffsets(t);

            // 注册的视觉跟随物（宝箱等，随格动画升起，§13.2）
            UpdateVisualFollowers(t);

            // 待移除模型下沉溶解（§13.4）
            UpdateRemovedVisuals(t);

            if (raw >= 1f)
                Complete(t, cancelled: false);
        }
    }

    /// <summary>
    /// 注册视觉跟随物：动画期间其世界 Y 跟随指定格的动画高度（如中央宝箱随地形升起，§13.2）。
    /// 【阶段五】注册到包含该格的活动动画；动画结束后跟随物停在最终高度。
    /// 同一跟随物重复注册按最新格处理。
    /// </summary>
    public void RegisterVisualFollower(Transform target, HexCellData cell)
    {
        if (target == null || cell == null) return;
        ActiveMapTransition owner = _actives.FirstOrDefault(t => !t.Completed && t.ChangedSet.Contains(cell))
                                   ?? _actives.FirstOrDefault(t => !t.Completed);
        if (owner == null) return;
        owner.Followers[target] = cell;
        // 立即定位到动画起点，避免首帧跳动
        Vector3 pos = target.position;
        pos.y = GetAnimatedWorldY(cell);
        target.position = pos;
    }

    public void UnregisterVisualFollower(Transform target)
    {
        if (target == null) return;
        foreach (ActiveMapTransition t in _actives)
            t.Followers.Remove(target);
    }

    /// <summary>把变化格上的地貌/资源模型接入当前动画，避免 Commit 帧直接跳到最终海拔。</summary>
    public void RegisterCellVisualFollowers(IEnumerable<HexCellData> cells)
    {
        if (cells == null) return;
        foreach (HexCellData cell in cells)
        {
            if (cell == null) continue;
            if (cell.landFormModel != null)
                RegisterVisualFollower(cell.landFormModel.transform, cell);
            if (cell.resourceModel != null)
                RegisterVisualFollower(cell.resourceModel.transform, cell);
        }
    }

    private void UpdateVisualFollowers(ActiveMapTransition t)
    {
        if (t.Followers.Count == 0) return;
        foreach (var kv in t.Followers)
        {
            if (kv.Key == null || kv.Value == null) continue;
            Vector3 pos = kv.Key.position;
            pos.y = GetAnimatedWorldY(kv.Value);
            kv.Key.position = pos;
        }
    }

    /// <summary>对局结束时强制完成全部动画（§13.8 对局结束清理）；幂等。</summary>
    public void ForceComplete()
    {
        for (int i = _actives.Count - 1; i >= 0; i--)
        {
            ActiveMapTransition t = _actives[i];
            if (t.Completed) continue;
            t.EasedProgress = 1f;
            Complete(t, cancelled: true);
        }
    }

    /// <summary>查询格子当前动画高度（世界 Y）。动画内使用实例缓存的 old/target，
    /// 不依赖 Cell 当前逻辑高度（Wave 会在 Commit 后立即恢复逻辑 Cell）。</summary>
    public float GetAnimatedWorldY(HexCellData cell)
    {
        if (cell == null) return 0f;
        float fallbackY = cell.RealCenterWorldCoordinate.y;
        foreach (ActiveMapTransition t in _actives)
        {
            if (t.Completed) continue;
            if (!t.ChangedSet.Contains(cell)) continue;
            if (t.OldCenterWorldY == null ||
                !t.OldCenterWorldY.TryGetValue(cell.GenerateOrder, out float oldY))
                return fallbackY;
            if (!t.TargetCenterWorldY.TryGetValue(cell.GenerateOrder, out float targetY))
                return fallbackY;
            float local = t.LocalProgress(cell);
            return Mathf.Lerp(oldY, targetY, local);
        }
        return fallbackY;
    }

    // ── 内部实现 ─────────────────────────────────────────────

    private void PublishPhase(ActiveMapTransition t, MapChangedPhase phase)
    {
        t.PhasePublisher?.Invoke(new MapChangedEvent(
            t.CommitId,
            t.ChangedCells,
            t.DirtyFlags,
            phase));
    }

    /// <summary>
    /// 完成/取消动画（幂等）：进度定格 1 → 后端收尾 → 解锁自己的格 → 销毁句柄 → 吸附单位 →
    /// 发布 Finalized/Cancelled。【阶段五】只解锁本动画的格（UnlockCells），不影响并行动画。
    /// </summary>
    private void Complete(ActiveMapTransition t, bool cancelled)
    {
        if (t.Completed) return;
        t.Completed = true;

        // 1. 后端收尾：进度定格 1（顶点停在最终位置）
        foreach (ChunkIndex chunk in t.Chunks)
            _renderBackend.FinalizeChunkAnimation(chunk);

        // 2. 单位吸附最终高度。Wave 是回到起点的脉冲，逻辑 Height 随后由测试控制器同步恢复；
        // 此处若吸附目标高度，会在回调前制造一次错误的单位跳高。
        if (_unitMovementSystem != null && t.Options?.Stagger != MapTransitionStagger.Wave)
            _unitMovementSystem.RefreshStandingUnitPositions(t.ChangedSet);

        // 3. 销毁待移除模型（动画下沉后统一清理）
        t.Removed?.DestroyAll();

        // 4. 解锁（动画期间保持锁，§20-5；只解锁自己的格，§阶段五-并行动画）
        _interactionGate?.UnlockCells(t.ChangedSet);

        // 5. 发布 Finalized / Cancelled（同一 CommitId 只完成一次）
        if (!cancelled)
            PublishPhase(t, MapChangedPhase.Finalized);
        else
            PublishPhase(t, MapChangedPhase.Cancelled);

        _actives.Remove(t);
    }

    private void UpdateUnitVisualOffsets(ActiveMapTransition t)
    {
        if (_unitMovementSystem == null) return;

        foreach (HexCellData cell in t.ChangedCells)
        {
            if (cell == null) continue;
            GameObject unit = cell.GetOccupant() ?? cell.GetUnit();
            if (unit == null) continue;
            if (_unitMovementSystem.IsUnitMoving(unit)) continue; // 移动中单位跳过（§12.5）

            Vector3 pos = unit.transform.position;
            pos.y = GetAnimatedWorldY(cell);
            unit.transform.position = pos;
        }
    }

    private void UpdateRemovedVisuals(ActiveMapTransition t)
    {
        if (t.Removed == null || t.Removed.IsEmpty) return;
        float progress = t.EasedProgress;
        foreach (UnityEngine.Object obj in t.Removed.Objects)
        {
            GameObject go = obj as GameObject;
            if (go == null) continue;
            // 下沉 + 缩小：模拟模型溶解（§13.4）
            Vector3 p = go.transform.position;
            p.y -= progress * 2f;
            go.transform.position = p;
            float s = Mathf.Max(0.01f, 1f - progress * 0.7f);
            go.transform.localScale = new Vector3(s, s, s);
        }
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);

    private static int CubeDistance(Vector3 a, Vector3 b)
    {
        Vector3 d = a - b;
        return (int)((Mathf.Abs(d.x) + Mathf.Abs(d.y) + Mathf.Abs(d.z)) * 0.5f);
    }
}
