using Zenject;

//****************************************
// 【动态地图-阶段五】分帧提交执行器（MapSlicedCommitExecutor）
// 每帧驱动 MapMutationService 的分帧提交（§阶段五-分帧提交）：
// 数据写入/单位处理在 CommitSliced 调用帧原子完成，脏 Chunk 几何构建按
// maxChunksPerFrame 拆分到多帧，全部完成后统一提交 + 收尾 + 广播事件。
// 职责单一：只转发 TickSlicedCommit；事务/锁/事件逻辑全在 MapMutationService。
//****************************************

public class MapSlicedCommitExecutor : ITickable
{
    private readonly MapMutationService _mutationService;

    public MapSlicedCommitExecutor(MapMutationService mutationService)
    {
        _mutationService = mutationService;
    }

    public void Tick()
    {
        _mutationService?.TickSlicedCommit();
    }
}
