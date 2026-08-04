using System.Collections.Generic;

//****************************************
// 【动态地图-阶段二/三/四/五】渲染后端接口（IMapRenderBackend）
// 阶段二实现 = MapRenderer（WholeMap 后端）；阶段三实现 = ChunkMapRenderer（Chunked 后端）。
// 阶段四：SupportsAnimatedTransition + 动画几何构建 + MaterialPropertyBlock 进度驱动（§20-10）。
// 阶段五：分帧提交（ComputeDirtyChunkIndices / PrepareChunkGeometrySlice）。
// 双后端并存，配置 MapRenderMode 切换（§二十-2）；共用无状态 CellMeshData 生成器。
// 测试可用替身注入。
//****************************************

public interface IMapRenderBackend
{
    /// <summary>是否支持"脏 Chunk 局部重建"（阶段三 Chunked 后端 true；WholeMap 后端 false）。</summary>
    bool SupportsChunkedRebuild { get; }

    /// <summary>
    /// 是否支持 Shader 顶点动画（阶段四）：Chunked 后端 true；WholeMap 后端 false。
    /// false 时 MapMutationService 对 Duration&gt;0 降级为同步提交（§14 阶段四检测点）。
    /// </summary>
    bool SupportsAnimatedTransition { get; }

    /// <summary>基于当前（已写入目标数据的）HexCellData 生成全图几何 staging（无渲染副作用）。</summary>
    PreparedWholeMapGeometry PrepareWholeMapGeometry();

    /// <summary>把 staging 几何原子应用到渲染层（复用 Mesh/材质缓存，无新建泄漏）。</summary>
    void CommitWholeMapGeometry(PreparedWholeMapGeometry geometry);

    /// <summary>
    /// 阶段三：基于脏格集合计算脏 Chunk（含一环 halo 依赖），只重建受影响 Chunk 的 staging。
    /// WholeMap 后端不应被调用（SupportsChunkedRebuild=false 时走 WholeMap 路径）。
    /// </summary>
    PreparedChunkGeometry PrepareChunkGeometry(IReadOnlyCollection<HexCellData> changedCells);

    /// <summary>阶段三：把脏 Chunk staging 原子交换到渲染层（active ↔ staging 双缓冲）。</summary>
    void CommitChunkGeometry(PreparedChunkGeometry geometry);

    /// <summary>
    /// 阶段四：动画几何构建——与 PrepareChunkGeometry 相同脏 Chunk 计算，
    /// 但生成带 UV2/UV3 顶点动画通道的 staging（§20-10）：
    /// UV2.x=startVertexY、UV2.y=targetVertexY；UV3.x=错峰延迟、UV3.y=参与标记。
    /// WholeMap 后端不支持（抛 NotSupportedException）。
    /// </summary>
    PreparedChunkGeometry PrepareAnimatedChunkGeometry(
        IReadOnlyCollection<HexCellData> changedCells,
        IReadOnlyDictionary<int, float> oldHeights,
        IReadOnlyDictionary<int, float> staggerDelays);

    /// <summary>阶段四：提交动画 staging（交换 mesh 并挂载 MaterialPropertyBlock，进度=0）。</summary>
    void CommitAnimatedChunkGeometry(PreparedChunkGeometry geometry);

    /// <summary>阶段四：逐帧驱动 Chunk 动画进度（MaterialPropertyBlock 设置 _ChunkProgress，§20-10）。</summary>
    void SetChunkAnimationProgress(ChunkIndex index, float progress);

    /// <summary>阶段四：动画结束清理（进度定格 1、回收淡出幽灵、清 MPB）。幂等。</summary>
    void FinalizeChunkAnimation(ChunkIndex index);

    /// <summary>
    /// 阶段五：计算脏 Chunk 索引集合（改格 + 一环邻居 → 所属 Chunk 去重，§七），不构建几何。
    /// 分帧提交用。WholeMap 后端不支持（抛 NotSupportedException）。
    /// </summary>
    System.Collections.Generic.IReadOnlyList<ChunkIndex> ComputeDirtyChunkIndices(
        System.Collections.Generic.IReadOnlyCollection<HexCellData> changedCells);

    /// <summary>
    /// 阶段五：只构建指定 Chunk 列表的 staging（分帧提交用，每帧构建少量 Chunk）。
    /// WholeMap 后端不支持（抛 NotSupportedException）。
    /// </summary>
    PreparedChunkGeometry PrepareChunkGeometrySlice(
        System.Collections.Generic.IReadOnlyList<ChunkIndex> chunkIndices);

    /// <summary>变化格对象刷新：移除已清空的地貌/资源模型（转交句柄）、保留模型归位、重建网格线。</summary>
    void RefreshCellObjects(IReadOnlyCollection<HexCellData> changedCells, RemovedVisualHandle removed);

    /// <summary>立即刷新迷雾视觉（突破 20fps 限频），用于瞬间亮灭场景。</summary>
    void ForceRefreshFogVisuals();
}
