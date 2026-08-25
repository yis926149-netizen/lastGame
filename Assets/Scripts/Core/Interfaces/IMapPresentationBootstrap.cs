using System;

/// <summary>
/// 地图表现层初始化入口。
/// 【P0-1 地图初始化分帧】提供两条路径：
///  - <see cref="InitializeMapPresentation"/>：完全同步，单帧完成（编辑器/测试/回退路径）。
///  - <see cref="BeginInitializeMapPresentation"/> + <see cref="TickInitializeMapPresentation"/>：
///    同帧只建骨架（雾全局属性 + Chunk 宿主 + LandForm/Resource 根节点），
///    mesh 构建与 prefab 实例化按帧分批推进，消除开局 1588ms 长帧。
/// </summary>
public interface IMapPresentationBootstrap
{
    /// <summary>同步全量初始化（保留：编辑器/测试同步路径）。</summary>
    void InitializeMapPresentation();

    /// <summary>
    /// 分帧初始化第一步：同帧建立骨架（下游 <c>GameObject.Find</c> 与数据层步骤可立即依赖）。
    /// </summary>
    /// <param name="onReady">全部分帧工作完成后回调一次（可为 null）。已就绪时立即同步调用。</param>
    void BeginInitializeMapPresentation(Action onReady = null);

    /// <summary>分帧初始化推进一批。返回 true 表示全部完成（含已完成时的幂等 true）。</summary>
    bool TickInitializeMapPresentation();

    /// <summary>表现层是否已全部就绪。</summary>
    bool IsPresentationReady { get; }
}
