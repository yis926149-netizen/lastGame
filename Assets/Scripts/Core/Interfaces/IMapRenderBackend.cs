using System.Collections.Generic;

//****************************************
// Chunk 地图渲染后端接口。
// 阶段四：SupportsAnimatedTransition + 动画几何构建 + MaterialPropertyBlock 进度驱动（§20-10）。
// 阶段五：分帧提交（ComputeDirtyChunkIndices / PrepareChunkGeometrySlice）。
// 测试可用替身注入。
//****************************************

public interface IMapRenderBackend
{
    /// <summary>
    /// 是否支持 Shader 顶点动画。
    /// false 时 MapMutationService 对 Duration&gt;0 降级为同步提交。
    /// </summary>
    bool SupportsAnimatedTransition { get; }

    /// <summary>
    /// 基于脏格集合计算脏 Chunk（含一环 halo 依赖），只重建受影响 Chunk 的 staging。
    /// </summary>
    PreparedChunkGeometry PrepareChunkGeometry(IReadOnlyCollection<HexCellData> changedCells);

    /// <summary>阶段三：把脏 Chunk staging 原子交换到渲染层（active ↔ staging 双缓冲）。</summary>
    void CommitChunkGeometry(PreparedChunkGeometry geometry);

    /// <summary>
    /// 阶段四：动画几何构建——与 PrepareChunkGeometry 相同脏 Chunk 计算，
    /// 但生成带 UV2/UV3 顶点动画通道的 staging（§20-10）：
    /// UV2.x=startVertexY、UV2.y=targetVertexY；UV3.x=错峰起点、UV3.y=错峰终点。
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
    /// 分帧提交用。
    /// </summary>
    System.Collections.Generic.IReadOnlyList<ChunkIndex> ComputeDirtyChunkIndices(
        System.Collections.Generic.IReadOnlyCollection<HexCellData> changedCells);

    /// <summary>
    /// 阶段五：只构建指定 Chunk 列表的 staging（分帧提交用，每帧构建少量 Chunk）。
    /// </summary>
    PreparedChunkGeometry PrepareChunkGeometrySlice(
        System.Collections.Generic.IReadOnlyList<ChunkIndex> chunkIndices);

    /// <summary>变化格对象刷新：移除已清空的地貌/资源模型（转交句柄）。
    /// snapToFinalPosition=false 用于动画提交：模型由视觉过渡服务跟随地形移动。</summary>
    void RefreshCellObjects(IReadOnlyCollection<HexCellData> changedCells, RemovedVisualHandle removed,
        bool snapToFinalPosition = true);

    /// <summary>
    /// 立即刷新迷雾视觉（突破 20fps 限频），用于瞬间亮灭场景。
    /// 【实机修订-2026-08-04】snapCells 非空时对指定格立即 Snap（瞬间点亮/遮盖，§18.2"突起帧
    /// 37 格瞬间点亮"），其余格保持渐变过渡（释放时重新聚拢）；null 等价旧行为（全渐变）。
    /// </summary>
    void ForceRefreshFogVisuals(IReadOnlyCollection<HexCellData> snapCells = null);
}
