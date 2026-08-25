using System.IO;
using NUnit.Framework;
using NSubstitute;
using UnityEngine;

//****************************************
// 【P0-1 地图初始化分帧】验收测试（实施计划 §3.5）
//
// 分帧路径与同步路径「结果完全一致」的保证不是靠两条独立实现对账，而是靠**共用同一段代码**：
//   · Chunk 划分 / 水面归一化 / 雾初值 → 两条路径都走 ChunkMapRenderer.PrepareChunkHosts
//   · 单 Chunk 几何            → 两条路径都走 BuildChunkStaging + CommitChunkStaging
//   · 地貌/资源实例化           → 两条路径都走 SpawnLandFormModel / SpawnResourceModel
// 因此 Chunk 数量、mesh 顶点数、实例数天然相同；本文件守住的是「这份共用关系不被后续重构拆开」，
// 外加一条真正可数值验证的关键契约（ComputeSolidAreaCenter ≡ BuildSolidArea().Center）。
//
// 完整 A/B 数值对账需要 Zenject 场景 + 渲染上下文（PlayMode），见实施计划 §3.5 真机验证清单。
//****************************************

/// <summary>
/// 【P0-1 关键契约】骨架帧用的「轻量中心公式」必须与 44 点构建的 0 号点完全一致。
///
/// 背景：RealCenterWorldCoordinate 原本只在逐 Chunk 的 PreBuildRectProfiles 里写入，
/// 而分帧后 GameFlowManager 在骨架帧同帧就要读它定位主城/AI 主城/公共建筑/地貌浮标。
/// PrepareChunkHosts 用 ComputeSolidAreaCenter 为全图预置该值；若它与后续 mesh 构建的
/// 覆写值不同，开局实体会落在与地形错位的高度/位置上（且分帧与同步两条路径结果不同）。
///
/// 当前实现里 44 点数组的 0 号点直接调用 ComputeSolidAreaCenter，两者结构上不可能分叉；
/// 本测试守的是这条「结构钉子」不被拆掉——一旦有人把 0 号点改回内联的
/// Perturb(...) + Y_Perturb 表达式，两处公式就会随各自演进而漂移，这里立刻失败。
/// </summary>
public class SolidAreaCenterParityTests
{
    private MapGenerationConfigSO _config;
    private Texture2D _noise;
    private Texture2D _previousNoise;

    [SetUp]
    public void SetUp()
    {
        _config = ScriptableObject.CreateInstance<MapGenerationConfigSO>();

        // Perturb/PerturbY2 都要采样 HexMetrics.noiseSource；用确定性伪噪声，避免依赖工程资源。
        _previousNoise = HexMetrics.noiseSource;
        _noise = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                _noise.SetPixel(x, y, new Color(
                    (x * 7 % 16) / 15f,
                    (y * 11 % 16) / 15f,
                    ((x + y) * 5 % 16) / 15f,
                    1f));
            }
        }
        _noise.Apply(false);
        HexMetrics.noiseSource = _noise;
    }

    [TearDown]
    public void TearDown()
    {
        HexMetrics.noiseSource = _previousNoise;
        if (_noise != null) Object.DestroyImmediate(_noise);
        if (_config != null) Object.DestroyImmediate(_config);
    }

    private static HexCellData MakeCell(int order, Vector3 center, float height) =>
        new HexCellData(Enums.HexType.NoRiver, order, Vector3.zero, center, height);

    [Test]
    public void ComputeSolidAreaCenter_EqualsBuildSolidAreaCenter_EighteenGon()
    {
        _config.solidAreaTopology = Enums.SolidAreaTopology.EighteenGon;
        AssertCenterParityAcrossCells();
    }

    [Test]
    public void ComputeSolidAreaCenter_EqualsBuildSolidAreaCenter_Hexagon()
    {
        // 六边形细分只改 7..18 号边点，0 号中心点必须不受影响。
        _config.solidAreaTopology = Enums.SolidAreaTopology.Hexagon;
        AssertCenterParityAcrossCells();
    }

    private void AssertCenterParityAcrossCells()
    {
        IMapDataService mapData = Substitute.For<IMapDataService>();
        var generator = new MeshGeneratorService(mapData, _config);

        // 覆盖不同 XZ（噪声采样点不同）与不同 Height（elevationStep 换算 + Y 扰动）
        var cells = new[]
        {
            MakeCell(0, Vector3.zero, 0f),
            MakeCell(1, new Vector3(5.196f, 0f, 4.5f), 1f),
            MakeCell(2, new Vector3(-12.7f, 0f, 31.5f), 4f),
            MakeCell(3, new Vector3(77.9f, 0f, -18f), -2f),
        };

        foreach (HexCellData cell in cells)
        {
            Vector3 cheap = generator.ComputeSolidAreaCenter(cell);
            Vector3 full = generator.BuildSolidArea(cell, null).Center;

            Assert.AreEqual(full.x, cheap.x, 1e-5f, $"cell {cell.GenerateOrder} 中心 X 不一致");
            Assert.AreEqual(full.y, cheap.y, 1e-5f, $"cell {cell.GenerateOrder} 中心 Y 不一致");
            Assert.AreEqual(full.z, cheap.z, 1e-5f, $"cell {cell.GenerateOrder} 中心 Z 不一致");
        }
    }

    [Test]
    public void ComputeSolidAreaCenter_AppliesElevationStep()
    {
        _config.elevationStep = 3f;
        IMapDataService mapData = Substitute.For<IMapDataService>();
        var generator = new MeshGeneratorService(mapData, _config);

        // 同一 XZ、Height 差 1 → Y 差恰为 elevationStep（噪声只依赖 XZ，两格扰动量相同）
        Vector3 low = generator.ComputeSolidAreaCenter(MakeCell(0, new Vector3(9f, 0f, 13.5f), 2f));
        Vector3 high = generator.ComputeSolidAreaCenter(MakeCell(1, new Vector3(9f, 0f, 13.5f), 3f));

        Assert.AreEqual(_config.elevationStep, high.y - low.y, 1e-4f,
            "Height→世界Y 换算必须只经 elevationStep 一次（不得重复叠加）");
        Assert.AreEqual(low.x, high.x, 1e-6f);
        Assert.AreEqual(low.z, high.z, 1e-6f);
    }
}

/// <summary>
/// 【P0-1 地图初始化分帧】源码契约测试：守住「同步/分帧两条路径共用同一实现」这一层结构，
/// 以及分帧引入的几处非显然时序修正，防止后续重构悄悄把它们拆开。
/// </summary>
public class MapPresentationSlicedInitSourceContractTests
{
    private const string ScriptsRoot = "Assets/Scripts";

    private static string ReadScript(string relativePath)
    {
        return File.ReadAllText(Path.Combine(ScriptsRoot, relativePath));
    }

    [Test]
    public void Interface_ExposesSlicedInitEntryPoints()
    {
        string contract = ReadScript("Core/Interfaces/IMapPresentationBootstrap.cs");

        StringAssert.Contains("void InitializeMapPresentation();", contract,
            "同步全量路径必须保留（编辑器/测试/回退）");
        StringAssert.Contains("BeginInitializeMapPresentation", contract, "分帧第一步：同帧建骨架");
        StringAssert.Contains("bool TickInitializeMapPresentation();", contract, "分帧推进入口");
        StringAssert.Contains("bool IsPresentationReady", contract, "就绪查询（驱动器据此自停）");
    }

    [Test]
    public void SyncPath_And_SlicedPath_ShareChunkHostPreparation()
    {
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        // 同步入口必须复用分帧的骨架实现，否则两条路径的 Chunk 划分/水面归一化/雾初值会漂移
        StringAssert.Contains("IReadOnlyList<ChunkIndex> indices = PrepareChunkHosts(hexVertices);", renderer,
            "ChunkMapRender 必须复用 PrepareChunkHosts，保证两条路径 Chunk 划分完全一致");
        StringAssert.Contains("public IReadOnlyList<ChunkIndex> PrepareChunkHosts(Vector3[] hexVertices)", renderer,
            "分帧骨架入口必须存在");
        StringAssert.Contains("BuildChunkStaging(chunk, chunk.Cells)", renderer,
            "单 Chunk 几何构建必须是两条路径共用的同一函数");
        StringAssert.Contains("chunk.Cells = pair.Value;", renderer,
            "重复初始化时必须刷新 chunk.Cells，避免持有过期分组引用");
    }

    [Test]
    public void PrepareChunkHosts_PreSeedsRealCenterWorldCoordinateForWholeMap()
    {
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        StringAssert.Contains("cell.RealCenterWorldCoordinate = _meshGenerator.ComputeSolidAreaCenter(cell);", renderer,
            "骨架帧必须为全图预置 RealCenterWorldCoordinate："
            + "GameFlowManager 同帧就用它定位主城/AI主城/公共建筑/地貌浮标，"
            + "而逐 Chunk 的 PreBuildRectProfiles 此时只跑过极少数格");
    }

    [Test]
    public void NormalizeWaterState_ClassifiesLakeOrSeaInSkeletonFrame()
    {
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");

        // HexType=LakeOrSea 不能再只留给分帧的 BuildChunkWater：GameFlowManager 在骨架帧同帧
        // 就用 HexType 过滤水域选玩家/AI 出生点，滞后写入会让城市建在水里。
        StringAssert.Contains("cell.HexType = Enums.HexType.LakeOrSea;", ReadMethodBody(renderer, "NormalizeWaterState"),
            "水面归一化必须在骨架帧就把水域格标为 LakeOrSea，"
            + "否则 PlayerInit/AIInit 同帧读 HexType 时水域仍为 NoRiver、城市建在水里");
    }

    [Test]
    public void BoundaryNormalMerge_DeferredToTail_NotPerSlice()
    {
        string renderer = ReadScript("Managers/ChunkMapRenderer.cs");
        string bootstrap = ReadScript("Managers/MapPresentationBootstrap.cs");

        StringAssert.Contains("public void CommitChunkGeometrySlice(PreparedChunkGeometry geometry)", renderer,
            "逐帧提交入口（不含法线合并）必须存在");
        StringAssert.Contains("public void FinishInitialChunkBuild(", renderer,
            "全量分帧收尾（统一合并边界法线）必须存在");
        // MergeChunkBoundaryNormals 读取「已提交」mesh，逐帧合并既产生错误中间法线又白做功
        StringAssert.DoesNotContain("MergeChunkBoundaryNormals", ReadSliceCommitBody(renderer),
            "CommitChunkGeometrySlice 内不得做跨 Chunk 边界法线合并");
        StringAssert.Contains("_chunkMapRenderer.CommitChunkGeometrySlice(prepared);", bootstrap,
            "分帧提交必须走不含法线合并的 slice 入口");
        StringAssert.Contains("_chunkMapRenderer.FinishInitialChunkBuild(_builtStaging);", bootstrap,
            "收尾必须统一合并一次边界法线");
    }

    /// <summary>按花括号配对截取指定方法的方法体。</summary>
    private static string ReadMethodBody(string source, string methodName)
    {
        string signature = "private void " + methodName + "(";
        int start = source.IndexOf(signature, System.StringComparison.Ordinal);
        Assert.Greater(start, -1, $"找不到 {methodName}");
        return ReadBraceBlock(source, start);
    }

    /// <summary>按花括号配对截取 CommitChunkGeometrySlice 的方法体。</summary>
    private static string ReadSliceCommitBody(string renderer)
    {
        const string signature = "public void CommitChunkGeometrySlice(PreparedChunkGeometry geometry)";
        int start = renderer.IndexOf(signature, System.StringComparison.Ordinal);
        Assert.Greater(start, -1, "找不到 CommitChunkGeometrySlice");
        return ReadBraceBlock(renderer, start);
    }

    /// <summary>从 <paramref name="start"/>（方法签名起始）截取到配对闭合花括号为止的整段源码。</summary>
    private static string ReadBraceBlock(string source, int start)
    {
        int open = source.IndexOf('{', start);
        Assert.Greater(open, -1, "方法体缺少 '{'");
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
                return source.Substring(start, i - start + 1);
        }
        Assert.Fail("方法体花括号不配对");
        return string.Empty;
    }

    [Test]
    public void InstantiatePath_IsSharedBetweenSyncAndSliced()
    {
        string bootstrap = ReadScript("Managers/MapPresentationBootstrap.cs");

        StringAssert.Contains("private bool SpawnLandFormModel(HexCellData cell)", bootstrap,
            "地貌实例化必须收敛为单格函数，供同步/分帧共用");
        StringAssert.Contains("private bool SpawnResourceModel(HexCellData cell)", bootstrap,
            "资源实例化必须收敛为单格函数，供同步/分帧共用");
        StringAssert.Contains("SpawnLandFormModel(_mapDataService.GetCell(coordinate))", bootstrap,
            "同步路径必须调用共用的单格实例化函数（否则实例数会与分帧路径漂移）");
        StringAssert.Contains("SpawnResourceModel(_mapDataService.GetCell(coordinate))", bootstrap,
            "同步路径必须调用共用的单格实例化函数");
        // 队列存 cell（而非 prefab）：骨架帧后若资源被 TakeResource 消费，分帧路径会自然跳过，
        // 与同步路径「先实例化再销毁」的最终状态一致。
        StringAssert.Contains("_resourceQueue.Add(cell)", bootstrap,
            "分帧队列必须存 cell，实例化时重新读 cell.resource");
    }

    [Test]
    public void SlicedInit_HasPerFrameBudgetsAndNoPerFrameDelegateAllocation()
    {
        string bootstrap = ReadScript("Managers/MapPresentationBootstrap.cs");

        StringAssert.Contains("_chunksPerFrame", bootstrap, "每帧 Chunk 预算必须可调");
        StringAssert.Contains("_instantiatesPerFrame", bootstrap, "每帧实例化预算必须可调");
        // 方法组转换每帧会分配一个 Func<>，WebGL 上 GC 尖刺可见
        StringAssert.Contains("_spawnLandFormFn = SpawnLandFormModel;", bootstrap,
            "实例化委托必须缓存，避免每帧 GC 分配");
        StringAssert.Contains("_spawnResourceFn = SpawnResourceModel;", bootstrap,
            "实例化委托必须缓存，避免每帧 GC 分配");
        StringAssert.Contains("EnableSlicedInitTiming", bootstrap,
            "分帧耗时埋点必须保留（真机对照 §3.5）");
    }

    [Test]
    public void SlicedInit_UnlocksOnlyItsOwnCells()
    {
        string bootstrap = ReadScript("Managers/MapPresentationBootstrap.cs");

        StringAssert.Contains("_interactionGate.LockCells(_lockedCells);", bootstrap,
            "分帧期间必须锁全图交互，防止玩家点半成品地图");
        StringAssert.Contains("_interactionGate.UnlockCells(_lockedCells);", bootstrap,
            "MapInteractionGate 无引用计数：只能解锁自己锁的格，不得 UnlockAll 清掉他人的锁");
        StringAssert.DoesNotContain("_interactionGate.UnlockAll()", bootstrap,
            "UnlockAll 会清掉动画/事务持有的锁");
    }

    [Test]
    public void GameFlow_UsesSlicedBeginAndReplaysResourceVisibility()
    {
        string flow = ReadScript("Managers/GameFlowManager.cs");

        StringAssert.Contains("_mapPresentationBootstrap.BeginInitializeMapPresentation(OnMapPresentationReady);", flow,
            "开局必须走分帧入口并注册收尾回调");
        StringAssert.Contains("_publicBuildingGenerator.ApplyResourceVisibility();", flow,
            "分帧路径下 resourceModel 晚于 MarkUnexplorableArea 才诞生，隐藏意图必须在收尾回调重放");
    }

    [Test]
    public void PublicBuildingGenerator_RecordsHiddenHexes_NotDerivedFromIsUnexplorable()
    {
        string generator = ReadScript("Managers/PublicBuildingGenerator.cs");

        StringAssert.Contains("_resourceHiddenHexes.Add(hex);", generator,
            "必须记录本生成器自己的隐藏意图");
        StringAssert.Contains("public void ApplyResourceVisibility()", generator,
            "重放入口必须存在且幂等");
        // ArenaEventManager 也置 IsUnexplorable=true 但不隐藏资源模型（37 格预留区），
        // 按 IsUnexplorable 统一 SetActive 会把那些格的资源错误藏掉。
        StringAssert.Contains("ArenaEventManager", generator,
            "必须留下「不能用 IsUnexplorable 反推可见性」的原因说明");
    }

    [Test]
    public void SlicedInitExecutor_IsRegisteredAsTickable()
    {
        string executor = ReadScript("Core/Services/MapMutation/MapPresentationSlicedInitExecutor.cs");
        string installer = ReadScript("Infrastructure/Installers/GameInstaller.cs");

        StringAssert.Contains(": ITickable", executor, "驱动器必须是 ITickable");
        StringAssert.Contains("_bootstrap.IsPresentationReady) return;", executor,
            "就绪后必须自停，Tick 退化为一次布尔判断");
        StringAssert.Contains("BindInterfacesAndSelfTo<MapPresentationSlicedInitExecutor>()", installer,
            "驱动器必须注册进容器，否则分帧永远推进不下去");
    }
}
