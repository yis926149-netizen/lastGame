using System.IO;
using NUnit.Framework;

/// <summary>
/// 【程序化山脉】阶段 7.8：性能与资源生命周期的源码契约测试（防回滚）。
/// 锁住性能验收工具链：
///  - ChunkMapRenderer：构建耗时/碰撞 cooking/动画帧耗时诊断钩子（默认关闭零开销）
///  - ChunkMapRenderer：DebugChunks 只读诊断枚举（供编辑器验收工具统计，不暴露修改语义）
///  - MountainGeometryBuilder：单格 solid 扇顶点预算常量 = 54（决策 ㉛/3.3 预算断言）
///  - ChunkRenderData：Active/StagingCollision 双缓冲上限 2 mesh/Chunk，OnDestroy 显式销毁
/// 运行时性能数据（构建耗时/面数/材质实例数/动画帧耗时）由
/// Tools/程序化山脉/性能基线（自动 PlayMode）采集并写 Temp/mountain_perf_report.txt。
/// </summary>
public class MountainStage7PerformanceContractTests
{
    private const string ScriptsRoot = "Assets/Scripts";

    private static string ReadScript(string relativePath) => File.ReadAllText(Path.Combine(ScriptsRoot, relativePath));

    [Test]
    public void ChunkMapRenderer_TimingHooks_DefaultOffGuarded()
    {
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        StringAssert.Contains("public static bool EnableChunkBuildTiming;", renderer,
            "构建计时开关必须存在（阶段 7.8 工具链）");
        StringAssert.Contains("public static long ChunkBuildCount;", renderer, "Chunk 构建计数必须存在");
        StringAssert.Contains("public static long MountainChunkBuildCount;", renderer, "含山 Chunk 构建计数必须存在");
        StringAssert.Contains("public static double ChunkBuildMsTotal;", renderer, "无山 Chunk 构建耗时累计必须存在");
        StringAssert.Contains("public static double MountainChunkBuildMsTotal;", renderer, "含山 Chunk 构建耗时累计必须存在");
        StringAssert.Contains("public static long CollisionCommitCount;", renderer, "碰撞 cooking（提交）计数必须存在");
        StringAssert.Contains("public static long AnimProgressFrameCount;", renderer, "动画帧采样计数必须存在");
        StringAssert.Contains("public static double AnimProgressFrameMsTotal;", renderer, "动画帧耗时累计必须存在");
        StringAssert.Contains("System.Diagnostics.Stopwatch timing = EnableChunkBuildTiming ? System.Diagnostics.Stopwatch.StartNew() : null;",
            renderer, "计时必须默认关闭（零开销），仅工具启用");
    }

    [Test]
    public void ChunkMapRenderer_CollisionCommitCountedUnderFlag()
    {
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        StringAssert.Contains("if (EnableChunkBuildTiming) CollisionCommitCount++;", renderer,
            "collision cooking 计数必须发生在 TerrainCollider.sharedMesh = StagingCollision 之后且仅在开关下");
    }

    [Test]
    public void ChunkMapRenderer_DebugChunks_ReadOnlyDiagnosticAccessor()
    {
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        StringAssert.Contains("public IEnumerable<ChunkRenderData> DebugChunks => _chunks.Values;", renderer,
            "必须提供只读 Chunk 枚举供性能/诊断工具统计（阶段 7.8）");
    }

    [Test]
    public void MountainGeometryBuilder_SolidFanVertexBudget_Is54()
    {
        string builder = ReadScript("Core/Services/MountainGeometryBuilder.cs");

        StringAssert.Contains("public const int SolidMountainFanVertexCount = 54;", builder,
            "单格 solid 扇 flat 拆分顶点预算常量必须存在且 = 54（决策 ㉛，阶段 3.3）");
        StringAssert.Contains("new List<int>(SolidMountainFanVertexCount)", builder,
            "索引列表容量必须走预算常量");
        StringAssert.Contains("new List<Vector3>(SolidMountainFanVertexCount)", builder,
            "顶点列表容量必须走预算常量");
        StringAssert.Contains("new List<MountainVertexAnimSource[]>(SolidMountainFanVertexCount)", builder,
            "动画来源列表容量必须走预算常量");
        Assert.AreEqual(54, MountainGeometryBuilder.SolidMountainFanVertexCount, "预算常量必须等于 54");
    }

    [Test]
    public void ChunkRenderData_CollisionDoubleBuffer_DestroyedOnDestroy()
    {
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        StringAssert.Contains("chunk.ActiveCollision", renderer, "ActiveCollision 双缓冲必须存在（阶段 3.2）");
        StringAssert.Contains("chunk.StagingCollision", renderer, "StagingCollision 双缓冲必须存在（阶段 3.2）");
        StringAssert.Contains("if (chunk.ActiveCollision != null) Object.Destroy(chunk.ActiveCollision);", renderer,
            "OnDestroy 必须销毁 Active 碰撞 mesh（内存有界：每 Chunk ≤ 2）");
        StringAssert.Contains("if (chunk.StagingCollision != null) Object.Destroy(chunk.StagingCollision);", renderer,
            "OnDestroy 必须销毁 Staging 碰撞 mesh（内存有界：每 Chunk ≤ 2）");
    }
}
