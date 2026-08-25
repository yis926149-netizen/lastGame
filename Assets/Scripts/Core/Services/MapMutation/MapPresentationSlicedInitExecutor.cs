using Zenject;

//****************************************
// 【P0-1 地图初始化分帧】表现层分帧初始化驱动器
// 仿 MapSlicedCommitExecutor：每帧推进一批 Chunk 提交 / prefab 实例化，
// IsPresentationReady 后自行停止（Tick 退化为一次布尔判断）。
//
// GameFlowManager.Initialize() 只调 BeginInitializeMapPresentation（同帧建骨架），
// 剩余重活由本驱动器分摊到后续若干帧，消除开局 1588ms 长帧。
//****************************************

public class MapPresentationSlicedInitExecutor : ITickable
{
    private readonly IMapPresentationBootstrap _bootstrap;

    public MapPresentationSlicedInitExecutor(IMapPresentationBootstrap bootstrap)
    {
        _bootstrap = bootstrap;
    }

    public void Tick()
    {
        if (_bootstrap == null || _bootstrap.IsPresentationReady) return;
        _bootstrap.TickInitializeMapPresentation();
    }
}
