using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zenject;

//****************************************
// ChunkMapRenderer：唯一的 8×8 offset-grid 分块渲染后端（§六/§九/§十一）。
// - 每个 Chunk 持有 Terrain/Water/River 的 active/staging Mesh 双缓冲 + MeshCollider + 材质缓存。
// - 脏范围规则：改格 → 收集该格 + 一环邻居 → 所属 Chunk 去重（§七）。
// - 两阶段构建：阶段 1 为目标 Chunk + halo 预生成矩形 profile；阶段 2 生成目标 Chunk 自有几何（§九）。
// - 卡牌射线兼容：Chunk 根挂 MapChunkView，落点经 GetComponentInParent&lt;MapChunkView&gt; 判定（§11）。
// 阶段三范围：支持 FlatAll 法线（§二十-11），非 FlatAll 打运行时警告、不保证无缝。
//
// 【动画管线设计约束-2026-08-05（波浪测试反哺，详见 动态地图/动态地图变化与分块重建方案.md 末章）】
// ① 动画 Commit 首帧必须是旧高度：任何动画 staging 在提交 Renderer 前，顶点 Y 先写为旧高度
//    （ApplyAnimationStartVertices），目标高度只存 UV2.y——双缓冲目标网格先可见一帧即"全图突变"。
// ② 纯视觉脉冲（staging.AnimationReturnsToStart=true）不得提交 staging mesh：
//    仅提取 targetY/delay 写入 CPU 缓存，在当前稳定 mesh 上原地动画。提交重建 mesh 会替换
//    UV0/法线/submesh/材质槽，实机"动画中地图变色"（MPB/材质屏蔽均无效的根因）。
// ③ 顶点动画在 CPU 侧执行（SetChunkAnimationProgress 每帧写 mesh.vertices）；
//    每帧所有顶点基线无条件取 UV2.x 旧高度，仅有效窗口内插值 UV2.y，禁止沿用 AnimBaseVerts.y。
// ④ MaterialPropertyBlock 是整块替换语义：动画 Chunk 写入前必须确认该 Renderer 无其他
//    逐 Renderer 参数（迷雾/色调）；纯视觉脉冲路径完全不写 MPB、不切材质、不写 clip。
// ⑤ 高度类、拓扑不变动画的权威旧视觉 = 当前显示 mesh 的逐顶点快照
//    （BindWaveStartVerticesFromActiveMesh），禁止用数据模型差值反推（会与真实显示漂移）。
//****************************************

public class ChunkMapRenderer : MonoBehaviour, IMapRenderBackend
{
    public const int ChunkSize = 8;

    // 【动态地图-阶段四】每 Chunk 动画进度属性（MaterialPropertyBlock，§20-10）
    private static readonly int ChunkProgressId = Shader.PropertyToID("_ChunkProgress");
    // 【顶出方案-修订】每 Chunk clip 平面参数（动画期间恒定，Commit 时一次性设置，
    // 由 SetChunkClipPlane 写入，surf/ShadowCaster 共用，§13.2 顶出 clip）
    private static readonly int ChunkAnimBaseYId = Shader.PropertyToID("_ChunkAnimBaseY");
    private static readonly int ChunkAnimRiseHeightId = Shader.PropertyToID("_ChunkAnimRiseHeight");
    private static readonly int GridColorId = Shader.PropertyToID("_Color");

    // 【程序化山脉-阶段 7.8】Chunk 构建性能诊断钩子（默认关闭 = 零开销；仅编辑器工具
    // Tools/程序化山脉/性能基线 启用）。统计 Chunk 构建耗时（含/不含山体分类）、
    // collision cooking（提交）次数与 CPU 顶点动画单帧写入耗时，供阶段 7.8 性能验收记录。
    public static bool EnableChunkBuildTiming;
    public static long ChunkBuildCount;
    public static long MountainChunkBuildCount;
    public static double ChunkBuildMsTotal;
    public static double MountainChunkBuildMsTotal;
    public static long CollisionCommitCount;
    public static long AnimProgressFrameCount;
    public static double AnimProgressFrameMsTotal;
    public static double AnimProgressFrameMsMax;

    [Inject] private IMapDataService _mapDataService;
    [Inject] private MapGenerationConfigSO _config;
    [Inject] private IMeshGenerator _meshGenerator;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject(Optional = true)] private ILogisticsService _logisticsService;
    [Inject(Optional = true)] private IMapVisibilityResolver _visibilityResolver;

    /// <summary>可选的 Chunk 根父节点；为空时挂到本组件下。</summary>
    public Transform ChunkRootParent;

    // Chunk 运行时数据：ChunkIndex → 渲染宿主
    private readonly Dictionary<ChunkIndex, ChunkRenderData> _chunks = new Dictionary<ChunkIndex, ChunkRenderData>();

    /// <summary>【程序化山脉-阶段 7.8】性能基线/诊断用 Chunk 只读枚举（不暴露修改语义，仅供编辑器验收工具统计）。</summary>
    public IEnumerable<ChunkRenderData> DebugChunks => _chunks.Values;

    // 构建期注册表（每次 Prepare 重建；仅覆盖目标 Chunk + halo，§九）
    private IReadOnlyMapView _view;
    private readonly Dictionary<int, Vector3[]> _solidVertices = new Dictionary<int, Vector3[]>();
    private readonly Dictionary<int, Vector3[]> _lakeOrSeaVertices = new Dictionary<int, Vector3[]>();
    private readonly Dictionary<(int, Enums.HexDirection), List<Vector3>> _rectVerticesByCell = new Dictionary<(int, Enums.HexDirection), List<Vector3>>();
    private readonly Dictionary<(int owner, Enums.HexDirection direction), RectangleTransitionMeshData> _genericRectangleMeshes
        = new Dictionary<(int, Enums.HexDirection), RectangleTransitionMeshData>();
    /// <summary>【程序化山脉-阶段 3.6】山体 rect 缓存（与普通 rect 同键；普通 rect 保留原始表面供碰撞）。</summary>
    private readonly Dictionary<(int owner, Enums.HexDirection direction), MountainRectBuild> _mountainRectangleMeshes
        = new Dictionary<(int, Enums.HexDirection), MountainRectBuild>();

    // 材质缓存（按材质组合键共享，§6-2）
    private Material _terrainBaseMaterial0;
    private Material _terrainBaseMaterial1;
    private Material _terrainBaseMaterial2;
    // 【程序化山脉-阶段 4.2】山体稳定材质：每 Renderer 一份实例（与 _terrainBaseMaterial0 同生命周期，
    // OnDestroy 显式销毁）；Shader 查找只尝试一次，失败回落 _terrainBaseMaterial0 且只记录一次错误。
    private Shader _mountainShader;
    private Material _mountainMaterial;
    private bool _mountainShaderLookupAttempted;
    // 【程序化山脉-阶段 5.4】山体 Transition 变体（动画期间 keep-below clip）：每 Renderer 一份实例，
    // 属性从稳定山体材质克隆；Shader 缺失回落稳定山体材质（只报一次），绝不回落普通 Terrain shader。
    private Shader _mountainTransitionShader;
    private Material _mountainTransitionMaterial;
    private bool _mountainTransitionShaderLookupAttempted;
    private readonly Dictionary<(Material, Material), Material> _rectMaterialCache = new Dictionary<(Material, Material), Material>();
    private readonly Dictionary<(Material, Material, Material), Material> _triMaterialCache = new Dictionary<(Material, Material, Material), Material>();
    private readonly Dictionary<Material, Material> _mountainBoundaryMaterialCache = new Dictionary<Material, Material>();
    private readonly Dictionary<Material, Material> _mountainBoundaryTransitionCache = new Dictionary<Material, Material>();
    private Material _generatedGridMaterial;

    // 【阶段四修订】动画专用 *_Transition 材质缓存（§十九-21：动画 Shader 必须独立命名、
    // 独立材质，绝不修改三套稳定 Shader；仅在动画期间按 Chunk 切换使用）。
    // 与稳定材质一一对应：基础三材质各一份 Transition 变体，矩形/三角按组合键缓存。
    private Material _terrainBaseMaterial0Transition;
    private Material _terrainBaseMaterial1Transition;
    private Material _terrainBaseMaterial2Transition;
    private readonly Dictionary<(Material, Material), Material> _rectTransitionCache = new Dictionary<(Material, Material), Material>();
    private readonly Dictionary<(Material, Material, Material), Material> _triTransitionCache = new Dictionary<(Material, Material, Material), Material>();

    // 【阶段四修订-审查修复】Transition Shader 引用惰性缓存（每种只 Shader.Find 一次，
    // 与稳定路径 ResolveTerrainMaterials 的 null 保护模式一致，避免每次动画 Commit 重复字符串查找）
    private Shader _transitionBaseShader;
    private Shader _transitionRectShader;
    private Shader _transitionTriShader;

    // 迷雾状态：过渡管理器 + 限频。全局遮罩贴图由 MapPresentationBootstrap 创建绑定，
    // Chunk 后端负责盖章重建（RebuildFogMask）。
    private readonly FogTransitionManager _fogTransition = new FogTransitionManager();
    private float _fogRefreshTimer;
    // 【Excel 数值化】迷雾刷新间隔迁移至 FeelConfigProvider（原 const FogRefreshInterval = 1/20）。
    private bool _fogInitialized;
    private bool _isSubscribed;
    /// <summary>【迷雾修复-2026-08-04】后勤连通性事件订阅状态（开局主城迷雾刷新的关键路径）。</summary>
    private bool _isLogisticsSubscribed;

    /// <summary>【迷雾修复-2026-08-04】探索遮罩贴图盖章缓冲（Chunk 后端自行重建 _FogMaskTex 用）。</summary>
    private Color32[] _fogMaskData;

    /// <summary>【动态地图-阶段四】Chunked 后端支持 Shader 顶点动画（§20-10）。</summary>
    public bool SupportsAnimatedTransition => true;

    // ── 生命周期 ─────────────────────────────────────────────

    [Inject]
    private void InitializeAfterInjection()
    {
        Subscribe();
    }

    private void OnEnable() => Subscribe();

    private void Update()
    {
        _fogTransition.Tick(Time.deltaTime);
        if (_fogTransition.IsDirty)
        {
            _fogRefreshTimer += Time.deltaTime;
            if (_fogRefreshTimer >= FeelConfigProvider.FogRefreshInterval)
            {
                _fogRefreshTimer = 0f;
                RebuildFogMask();
                _fogTransition.ClearDirty();
            }
        }
    }

    private void OnDisable()
    {
        if (_isSubscribed && _mapVisualEvent != null)
        {
            _mapVisualEvent.OnMapVisualChanged.RemoveListener(OnMapVisualChanged);
            _isSubscribed = false;
        }
        if (_isLogisticsSubscribed && _logisticsService != null)
        {
            _logisticsService.LogisticsChanged -= OnLogisticsChanged;
            _isLogisticsSubscribed = false;
        }
    }

    private void Subscribe()
    {
        if (!_isSubscribed && _mapVisualEvent != null)
        {
            _mapVisualEvent.OnMapVisualChanged.AddListener(OnMapVisualChanged);
            _isSubscribed = true;
        }
        // 订阅后勤连通性变化：
        // GameFlowManager 开局时序为 PlayerInit(标记主城探索+Raise) → RecalculateAll，
        // Raise 时后勤尚未连通、主城格不可见；若此处不订阅 LogisticsChanged，
        // 后勤重算后主城可见性变化将永远无法触发迷雾目标刷新，主城初始地块迷雾
        // 直到下一次探索/竞技场等事件才消散。
        if (!_isLogisticsSubscribed && _logisticsService != null)
        {
            _logisticsService.LogisticsChanged += OnLogisticsChanged;
            _isLogisticsSubscribed = true;
        }
    }

    private void OnMapVisualChanged()
    {
        UpdateFogTransitionTargets();
    }

    private void OnLogisticsChanged()
    {
        UpdateFogTransitionTargets();
    }

    // ── 首帧全量构建 ──────────────────────────────────────────

    /// <summary>首次全量 Chunk 渲染入口。</summary>
    public void ChunkMapRender(Vector3[] hexVertices)
    {
        if (hexVertices == null || hexVertices.Length == 0) return;

        _view = new MapDataReadOnlyView(_mapDataService);

        // Water/coast profiles must not depend on Chunk traversal order. Normalize the
        // logical water fields before any Chunk builds a lake or coast dependency profile.
        NormalizeWaterState(_mapDataService.GetAllCells());

        // 按生成网格索引划分 Chunk（§二十-1）
        int xNumber = _config.xNumber;
        var cellsByChunk = new Dictionary<ChunkIndex, List<HexCellData>>();
        foreach (HexCellData cell in _mapDataService.GetAllCells())
        {
            if (cell == null) continue;
            ChunkIndex index = ChunkIndex.Of(cell, xNumber);
            if (!cellsByChunk.TryGetValue(index, out List<HexCellData> list))
            {
                list = new List<HexCellData>();
                cellsByChunk[index] = list;
            }
            list.Add(cell);
        }

        // 创建 Chunk 宿主
        var builtStaging = new List<ChunkStagingGeometry>();
        foreach (KeyValuePair<ChunkIndex, List<HexCellData>> pair in cellsByChunk)
        {
            ChunkRenderData chunk = GetOrCreateChunk(pair.Key, pair.Value);
            ChunkStagingGeometry staging = BuildChunkStaging(chunk, pair.Value);
            CommitChunkStaging(chunk, staging);
            builtStaging.Add(staging);
        }

        // 【阶段五-法线同步】初始全量构建同样合并跨 Chunk 边界法线（非 FlatAll 风格）
        if (_config != null && _config.shadingStyle != Enums.ShadingStyle.FlatAll)
            MergeChunkBoundaryNormals(builtStaging);

        _fogInitialized = false;
        UpdateFogTransitionTargets();
    }

    // ── IMapRenderBackend：脏 Chunk 路径 ─────────────────────

    public PreparedChunkGeometry PrepareChunkGeometry(IReadOnlyCollection<HexCellData> changedCells)
    {
        if (changedCells == null) return new PreparedChunkGeometry();

        _view = new MapDataReadOnlyView(_mapDataService);
        List<ChunkIndex> dirtyChunks = ComputeDirtyChunks(changedCells);
        var result = new PreparedChunkGeometry();
        foreach (ChunkIndex index in dirtyChunks)
        {
            if (!_chunks.TryGetValue(index, out ChunkRenderData chunk)) continue;
            ChunkStagingGeometry staging = BuildChunkStaging(chunk, chunk.Cells);
            result.Chunks.Add(staging);
        }
        return result;
    }

    public void CommitChunkGeometry(PreparedChunkGeometry geometry)
    {
        if (geometry == null) return;
        foreach (ChunkStagingGeometry staging in geometry.Chunks)
        {
            if (!_chunks.TryGetValue(staging.Index, out ChunkRenderData chunk)) continue;
            CommitChunkStaging(chunk, staging);
        }

        // 【阶段五-法线同步】非 FlatAll 风格：跨 Chunk 边界法线合并（§二十-11，Chunk 拆分后各 mesh
        // 独立 RecalculateNormals 会在边界产生光照接缝）。已提交 mesh 上按世界位置聚合平均后回写。
        if (_config != null && _config.shadingStyle != Enums.ShadingStyle.FlatAll)
            MergeChunkBoundaryNormals(geometry.Chunks);
    }

    // ── IMapRenderBackend：分帧提交（阶段五，§阶段五-分帧提交）────────

    /// <summary>阶段五：计算脏 Chunk 索引集合（不含构建）。</summary>
    public IReadOnlyList<ChunkIndex> ComputeDirtyChunkIndices(IReadOnlyCollection<HexCellData> changedCells)
    {
        if (changedCells == null) return new List<ChunkIndex>();
        return ComputeDirtyChunks(changedCells);
    }

    /// <summary>阶段五：只构建指定 Chunk 的 staging（每帧少量 Chunk，防单帧卡顿）。</summary>
    public PreparedChunkGeometry PrepareChunkGeometrySlice(IReadOnlyList<ChunkIndex> chunkIndices)
    {
        var result = new PreparedChunkGeometry();
        if (chunkIndices == null) return result;

        _view = new MapDataReadOnlyView(_mapDataService);
        foreach (ChunkIndex index in chunkIndices)
        {
            if (!_chunks.TryGetValue(index, out ChunkRenderData chunk)) continue;
            ChunkStagingGeometry staging = BuildChunkStaging(chunk, chunk.Cells);
            result.Chunks.Add(staging);
        }
        return result;
    }

    /// <summary>
    /// 【阶段五-法线同步】跨 Chunk 边界法线合并（§二十-11）：
    /// 把同一批脏 Chunk 的 Terrain mesh 按世界位置聚合法线，边界顶点（出现在 ≥2 个 mesh 的位置）
    /// 写回归一化平均，消除 Chunk 拆分导致的光照接缝；随后重算切线。FlatAll 风格跳过（面法线无接缝）。
    /// </summary>
    private void MergeChunkBoundaryNormals(IReadOnlyList<ChunkStagingGeometry> staged)
    {
        if (staged == null || staged.Count < 2) return;

        var meshes = new List<Mesh>();
        foreach (ChunkStagingGeometry s in staged)
        {
            if (!_chunks.TryGetValue(s.Index, out ChunkRenderData chunk)) continue;
            Mesh mesh = chunk.TerrainFilter != null ? chunk.TerrainFilter.sharedMesh : null;
            if (mesh == null || mesh.vertexCount == 0) continue;
            meshes.Add(mesh);
        }
        if (meshes.Count < 2) return;

        // 1. 位置 → 法线和 + 出现次数（跨 mesh 聚合）
        var sums = new Dictionary<Vector3Int, Vector3>();
        var counts = new Dictionary<Vector3Int, int>();
        foreach (Mesh mesh in meshes)
        {
            Vector3[] verts = mesh.vertices;
            Vector3[] normals = mesh.normals;
            if (verts == null || normals == null || verts.Length != normals.Length) continue;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3Int key = PositionKey(verts[i]);
                sums.TryGetValue(key, out Vector3 sum);
                sums[key] = sum + normals[i];
                counts.TryGetValue(key, out int c);
                counts[key] = c + 1;
            }
        }

        // 2. 回写：仅边界顶点（count > 1）取平均法线，并重算切线
        foreach (Mesh mesh in meshes)
        {
            Vector3[] verts = mesh.vertices;
            Vector3[] normals = mesh.normals;
            if (verts == null || normals == null || verts.Length != normals.Length) continue;
            bool changed = false;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3Int key = PositionKey(verts[i]);
                if (counts.TryGetValue(key, out int c) && c > 1)
                {
                    normals[i] = sums[key].normalized;
                    changed = true;
                }
            }
            if (!changed) continue;
            mesh.normals = normals;
            MapController.RecalculateTangentsSafe(mesh);
        }
    }

    private static Vector3Int PositionKey(Vector3 position)
    {
        const float precision = 10000f;
        return new Vector3Int(
            Mathf.RoundToInt(position.x * precision),
            Mathf.RoundToInt(position.y * precision),
            Mathf.RoundToInt(position.z * precision));
    }

    // ── IMapRenderBackend：动画路径（阶段四，§20-10）──────────

    /// <summary>
    /// 动画几何构建：与 PrepareChunkGeometry 相同脏 Chunk 计算，但生成带 UV2/UV3 顶点动画通道的 staging。
    /// </summary>
    public PreparedChunkGeometry PrepareAnimatedChunkGeometry(
        IReadOnlyCollection<HexCellData> changedCells,
        IReadOnlyDictionary<int, float> oldHeights,
        IReadOnlyDictionary<int, float> staggerDelays)
    {
        if (changedCells == null) return new PreparedChunkGeometry();

        _view = new MapDataReadOnlyView(_mapDataService);
        List<ChunkIndex> dirtyChunks = ComputeDirtyChunks(changedCells);
        float riseWindow = 0f;
        if (staggerDelays != null)
            staggerDelays.TryGetValue(MapVisualTransitionService.RiseWindowKey, out riseWindow);
        var anim = new AnimatedChunkBuildData
        {
            OldHeights = oldHeights,
            StaggerDelays = staggerDelays,
            ElevationStep = _config != null ? _config.elevationStep : 1f,
            RiseWindow = riseWindow
        };
        var result = new PreparedChunkGeometry();
        foreach (ChunkIndex index in dirtyChunks)
        {
            if (!_chunks.TryGetValue(index, out ChunkRenderData chunk)) continue;
            ChunkStagingGeometry staging = BuildChunkStaging(chunk, chunk.Cells, anim);
            result.Chunks.Add(staging);
        }
        return result;
    }

    /// <summary>提交动画 staging，并把该 Chunk 的 MaterialPropertyBlock 进度置 0（动画起点）。
    /// 【阶段四修订】同时把脏 Chunk 的 Terrain 材质切换为 *_Transition 变体（含 UV2/UV3 顶点动画通道），
    /// 并保存稳定材质数组供 Finalize 恢复（§十九-21：动画 Shader 独立命名，稳定渲染不受影响）。</summary>
    public void CommitAnimatedChunkGeometry(PreparedChunkGeometry geometry)
    {
        if (geometry == null) return;

        // Wave 是纯视觉脉冲，不能提交按 Height+Delta 重建的 staging mesh。即使不切 Shader/MPB，
        // CommitChunkGeometry 仍会替换 UV0、法线、submesh 与材质槽，导致动画中地形颜色变化。
        // 直接保留当前稳定 mesh，只从 staging 读取 targetY 与 delay 数据驱动 CPU 顶点动画。
        if (geometry.Chunks.Count > 0 && geometry.Chunks.All(c => c != null && c.AnimationReturnsToStart))
        {
            CommitWavePulseData(geometry);
            return;
        }

        // 阶段四：湖海/河流淡出（§13.4 方案C 简化）——提交前捕获旧水面/河流 mesh 克隆为幽灵，
        // 动画期间经 MPB 淡出，Finalize 销毁。仅当"旧有水而新无"（竞技场清湖海/河流）时创建。
        foreach (ChunkStagingGeometry staging in geometry.Chunks)
        {
            if (!_chunks.TryGetValue(staging.Index, out ChunkRenderData chunk)) continue;
            BindWaveStartVerticesFromActiveMesh(chunk, staging);
            CaptureFadeGhosts(chunk, staging);
            // 【顶出方案-修订】提交前捕获旧地形 mesh 快照（TerrainGhost）：新平台 clip 平面
            // 之上（keep-below）由它完整可见，消除"先变平再升起"的拓扑突变观感；Finalize 销毁。
            // 净下降/波浪测试模式在方法内自行跳过。
            CaptureTerrainGhost(chunk, staging);
        }

        // 动画 mesh 在第一次可见前先落到旧高度。目标高度保存在 UV2.y，避免双缓冲把目标
        // 几何先显示一帧，随后才被 SetChunkAnimationProgress(0) 拉回旧高度而产生整体跳变。
        foreach (ChunkStagingGeometry staging in geometry.Chunks)
            ApplyAnimationStartVertices(staging);

        CommitChunkGeometry(geometry);
        if (geometry == null) return;
        foreach (ChunkStagingGeometry staging in geometry.Chunks)
        {
            // 切换动画材质：保存稳定材质 → 换 *_Transition 变体 → clip 平面参数 → MPB 进度置 0
            if (_chunks.TryGetValue(staging.Index, out ChunkRenderData chunk))
            {
                // Wave 是纯 CPU 顶点脉冲，不需要 *_Transition Shader（clip 平面恒开，无顶出需求）；
                // 换材质反而引入 keep-below clip 参数导致颜色变化。非 Wave 模式仍正常切换。
                if (!staging.AnimationReturnsToStart)
                    SwitchToTransitionMaterials(chunk, staging);
                // Wave 使用稳定 Shader + CPU 顶点动画，不能调用 SetPropertyBlock：该 API 会整块
                // 替换 Renderer 现有 MPB，清掉迷雾/色调等逐 Renderer 参数，表现为动画中变色。
                if (!staging.AnimationReturnsToStart)
                    SetChunkClipPlane(chunk, staging);
                // 【CPU动画-2026-08-05】缓存动画顶点数据（mesh.uv2/uv3/vertices 一次性读取，
                // SetChunkAnimationProgress 每帧据此逐顶点插值写 mesh.vertices）
                if (chunk.TerrainFilter != null && chunk.TerrainFilter.sharedMesh != null)
                {
                    Mesh m = chunk.TerrainFilter.sharedMesh;
                    chunk.AnimUV2Cache = m.uv2;
                    chunk.AnimUV3Cache = m.uv3;
                    chunk.AnimBaseVerts = m.vertices;
                    chunk.AnimVertexBuffer = null;
                    chunk.AnimationReturnsToStart = staging.AnimationReturnsToStart;
                }
                SetChunkAnimationProgress(staging.Index, 0f);
            }
        }
    }

    private void CommitWavePulseData(PreparedChunkGeometry geometry)
    {
        foreach (ChunkStagingGeometry staging in geometry.Chunks)
        {
            if (!_chunks.TryGetValue(staging.Index, out ChunkRenderData chunk)) continue;
            Mesh mesh = chunk.TerrainFilter != null ? chunk.TerrainFilter.sharedMesh : null;
            TerrainGeometry terrain = staging.Terrain;
            if (mesh == null || terrain?.UV2s == null || terrain.UV3s == null ||
                mesh.vertexCount != terrain.UV2s.Length || terrain.UV2s.Length != terrain.UV3s.Length)
            {
                Debug.LogWarning($"[ChunkMapRenderer] Wave 跳过不兼容 Chunk {staging.Index}：" +
                                 $"activeVerts={mesh?.vertexCount ?? -1}, targetUV2={terrain?.UV2s?.Length ?? -1}, " +
                                 $"targetUV3={terrain?.UV3s?.Length ?? -1}。为保持材质/UV 稳定，不回退目标 mesh 提交。");
                continue;
            }

            Vector3[] stableVertices = mesh.vertices;
            int count = stableVertices.Length;
            var uv2 = new Vector2[count];
            for (int i = 0; i < count; i++)
                uv2[i] = new Vector2(stableVertices[i].y, terrain.UV2s[i].y);

            chunk.AnimUV2Cache = uv2;
            chunk.AnimUV3Cache = (Vector2[])terrain.UV3s.Clone();
            chunk.AnimBaseVerts = stableVertices;
            chunk.AnimVertexBuffer = null;
            chunk.AnimationReturnsToStart = true;
            SetChunkAnimationProgress(staging.Index, 0f);
        }
    }

    /// <summary>【阶段四修订】把 Chunk 的 Terrain 材质切换为 *_Transition 变体，并保存稳定材质数组。
    /// 【顶出方案-修订】不在这里处理阴影：三套 *_Transition Shader 内置手动 ShadowCaster pass，
    /// 与 surf 执行同一 clip 平面，阴影几何与可见几何一致（"隐形地形黑块"由 Shader 侧解决）。</summary>
    private void SwitchToTransitionMaterials(ChunkRenderData chunk, ChunkStagingGeometry staging)
    {
        if (chunk.TerrainRenderer == null) return;
        Material[] stable = chunk.TerrainRenderer.sharedMaterials;
        if (stable == null || stable.Length == 0) return;

        chunk.StableTerrainMaterials = stable;
        chunk.TerrainRenderer.sharedMaterials = ResolveTransitionMaterials(staging.Terrain, stable);
    }

    /// <summary>捕获旧水面/河流幽灵（克隆 mesh + 共享材质 + _FadeAlpha=1，动画期间淡出）。</summary>
    private void CaptureFadeGhosts(ChunkRenderData chunk, ChunkStagingGeometry staging)
    {
        // 水面：旧 host 有顶点且新 staging 无水面 → 淡出旧水面
        if (chunk.WaterHost != null && chunk.WaterHost.activeSelf)
        {
            Mesh oldWater = chunk.WaterHost.GetComponent<MeshFilter>()?.sharedMesh;
            if (oldWater != null && oldWater.vertexCount > 0 && staging.Water == null)
                chunk.FadeWaterGhost = CreateFadeGhost(oldWater, _config.lakeOrSeaMaterial);
        }

        // 河流：同理
        if (chunk.RiverHost != null && chunk.RiverHost.activeSelf)
        {
            Mesh oldRiver = chunk.RiverHost.GetComponent<MeshFilter>()?.sharedMesh;
            if (oldRiver != null && oldRiver.vertexCount > 0 && staging.River == null)
                chunk.FadeRiverGhost = CreateFadeGhost(oldRiver, _config.riverMaterial);
        }
    }

    /// <summary>
    /// 【顶出方案-修订】捕获旧地形 mesh 快照：Commit 前把当前 Terrain mesh 克隆为独立渲染层，
    /// 挂在本 Chunk 根下并整体下沉 0.02 世界单位——新平台 clip 平面之上（keep-below）由它完整
    /// 显示旧拓扑（坡面/水岸/海床），消除"先变平再升起"的突变；下沉量保证与新平台不 Z-fighting
    /// （新平台恒在前）。材质共享稳定材质（不实例化，无泄漏）；mesh 为独占克隆，Finalize 时销毁
    /// （DestroyGhost 显式销毁克隆 mesh，防幽灵泄漏）。正常投阴影：阴影几何与可见几何一致。
    /// 幂等：已存在旧快照（动画中途）不再重复捕获。
    /// 【波浪/下降修订-2026-08-05】净下降或测试开关禁用顶出时跳过快照：下降动画中旧地形
    /// （更高）会盖住回落的新地形；波浪模式无顶出观感需求（低格/水域格也不会被 clip 裁掉）。
    /// </summary>
    private void CaptureTerrainGhost(ChunkRenderData chunk, ChunkStagingGeometry staging)
    {
        if (chunk.TerrainGhost != null) return;
        if (chunk.TerrainFilter == null || chunk.TerrainRenderer == null) return;
        if (!chunk.TerrainRenderer.enabled) return;
        if (MapMutationDiagnostics.DisableKeepBelowClip || HasNetDescent(staging)) return;

        Mesh oldMesh = chunk.TerrainFilter.sharedMesh;
        if (oldMesh == null || oldMesh.vertexCount == 0) return;

        var go = new GameObject("TerrainGhost");
        go.transform.SetParent(chunk.Root.transform, false);
        go.transform.localPosition = new Vector3(0f, -0.02f, 0f); // 轻微下沉防 Z-fighting
        int mapLayer = LayerMask.NameToLayer("Map");
        if (mapLayer >= 0) go.layer = mapLayer;

        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = Object.Instantiate(oldMesh);

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = chunk.TerrainRenderer.sharedMaterials; // 稳定材质（未切换 Transition）
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;

        chunk.TerrainGhost = go;
    }

    /// <summary>
    /// 【顶出方案-修订】按本 Chunk 动画 staging 的 UV2 数据计算 clip 平面参数并写入 MPB：
    /// _ChunkAnimBaseY = 全部动画顶点最低 startY；_ChunkAnimRiseHeight = 最高 targetY - 最低 startY。
    /// keep-below clip：progress=0 时线位于最低起点 Y（新平台整体隐藏，只露 TerrainGhost）；
    /// progress=1 时线位于最高目标 Y（新平台完全可见）。动画期间恒定，由 _ChunkProgress 逐帧驱动。
    /// 【波浪/下降修订-2026-08-05】净下降（minStart &gt; maxTarget）或测试开关禁用顶出时，
    /// 必须把 clip 平面钉死在"不裁"位置：Shader 默认 _ChunkAnimRiseHeight=1 会在
    /// progress&gt;0 后裁掉全部地形（clip 线 = base + progress*rise，地形高度 &gt; 线即被丢弃）。
    /// </summary>
    private void SetChunkClipPlane(ChunkRenderData chunk, ChunkStagingGeometry staging)
    {
        if (chunk.TerrainRenderer == null) return;

        EvaluateAnimClip(staging, out bool hasAnimData, out bool pinOpen, out float minStart, out float maxTarget);
        if (!hasAnimData) return; // 无动画顶点数据：不写 MPB，保留 Shader 默认参数

        if (pinOpen)
        {
            // 净下降/测试模式：clip 线钉在 +1000（高于任何地形顶点），全程不裁。
            // Shader keep-below 语义：clip(animClipY - worldPos.y + 0.02) ——
            // 只有 worldPos.y ≤ animClipY 的片元保留。animClipY=+1000 时恒成立，等价于不裁。
            // 注意：-1000 是错误的（低于地形 → 全部片元被裁 → 地图消失）。
            chunk.AnimationBlock.SetFloat(ChunkAnimBaseYId, 1000f);
            chunk.AnimationBlock.SetFloat(ChunkAnimRiseHeightId, 0f);
            chunk.TerrainRenderer.SetPropertyBlock(chunk.AnimationBlock);
            return;
        }

        chunk.AnimationBlock.SetFloat(ChunkAnimBaseYId, minStart);
        chunk.AnimationBlock.SetFloat(ChunkAnimRiseHeightId, maxTarget - minStart);
        chunk.TerrainRenderer.SetPropertyBlock(chunk.AnimationBlock);
    }

    private static readonly int FadeAlphaId = Shader.PropertyToID("_FadeAlpha");

    /// <summary>
    /// 【顶出方案-修订】扫描动画 staging 的 UV2 顶点数据，一次性给出 keep-below clip 决策
    /// （评审 2026-08-05：规则单点维护，SetChunkClipPlane 与 HasNetDescent 共用，消除双份扫描漂移）：
    /// hasAnimData=false → 无动画顶点数据（调用方不写 MPB，保留 Shader 默认）；
    /// pinOpen=true → clip 平面钉死"恒不裁"（净下降 minStart&gt;maxTarget / NaN 数据 / DisableKeepBelowClip 测试开关）；
    /// 否则 (minStart, maxTarget) 为正常 clip 区间（最低 startY → 最高 targetY）。
    /// </summary>
    private static void EvaluateAnimClip(
        ChunkStagingGeometry staging,
        out bool hasAnimData,
        out bool pinOpen,
        out float minStart,
        out float maxTarget)
    {
        minStart = float.MaxValue;
        maxTarget = float.MinValue;
        Vector2[] uv2s = staging?.Terrain?.UV2s;
        hasAnimData = uv2s != null && uv2s.Length > 0;
        if (!hasAnimData)
        {
            pinOpen = false;
            return;
        }
        foreach (Vector2 uv2 in uv2s)
        {
            if (uv2.x < minStart) minStart = uv2.x;
            if (uv2.y > maxTarget) maxTarget = uv2.y;
        }
        pinOpen = float.IsNaN(minStart) || float.IsNaN(maxTarget) || minStart > maxTarget ||
                  MapMutationDiagnostics.DisableKeepBelowClip;
    }

    /// <summary>净下降判定：动画顶点整体向下（全部旧高度 &gt; 全部新高度，含 NaN/测试开关的钉死场景）。
    /// 规则由 EvaluateAnimClip 单点给出（与 SetChunkClipPlane 同一份），供 TerrainGhost 分支使用。</summary>
    private static bool HasNetDescent(ChunkStagingGeometry staging)
    {
        EvaluateAnimClip(staging, out bool hasAnimData, out bool pinOpen, out _, out _);
        return hasAnimData && pinOpen;
    }

    /// <summary>淡出幽灵的缓存属性块（避免动画帧逐帧分配，§18.3）。</summary>
    private readonly Dictionary<GameObject, UnityEngine.MaterialPropertyBlock> _ghostBlocks =
        new Dictionary<GameObject, UnityEngine.MaterialPropertyBlock>();

    private GameObject CreateFadeGhost(Mesh source, Material[] materials)
    {
        var go = new GameObject("FadeGhost");
        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = Object.Instantiate(source);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        var block = new UnityEngine.MaterialPropertyBlock();
        block.SetFloat(FadeAlphaId, 1f);
        renderer.SetPropertyBlock(block);
        _ghostBlocks[go] = block;
        return go;
    }

    /// <summary>
    /// 逐帧驱动 Chunk 动画进度。
    /// 【CPU动画-2026-08-05】顶点动画在 C# 侧执行：按 mesh.uv2/uv3 缓存逐顶点插值写 mesh.vertices
    /// （shader 端 vert 已不再变形——surface shader 编译对未在 Input 声明的 UV 通道读取不可靠，
    /// 由三次实机实验确认：无条件插值无效、无条件 +5 有效）。MPB 的 _ChunkProgress 仍写入，
    /// 供 surf/ShadowCaster 的 keep-below clip 平面使用（§13.2）。
    /// </summary>
    public void SetChunkAnimationProgress(ChunkIndex index, float progress)
    {
        if (!_chunks.TryGetValue(index, out ChunkRenderData chunk)) return;
        if (chunk.TerrainRenderer == null) return;

        // CPU 顶点动画：从 Commit 时缓存的 UV2/UV3 数据插值（与 shader 旧公式同语义）
        if (chunk.AnimUV2Cache != null && chunk.AnimUV3Cache != null && chunk.AnimBaseVerts != null &&
            chunk.AnimUV2Cache.Length == chunk.AnimUV3Cache.Length &&
            chunk.AnimBaseVerts.Length == chunk.AnimUV2Cache.Length &&
            chunk.TerrainFilter != null && chunk.TerrainFilter.sharedMesh != null &&
            chunk.TerrainFilter.sharedMesh.vertexCount == chunk.AnimUV2Cache.Length)
        {
            // 【程序化山脉-阶段 7.8】性能基线：CPU 逐顶点写入帧耗时采样（默认关闭零开销）。
            System.Diagnostics.Stopwatch timing = EnableChunkBuildTiming ? System.Diagnostics.Stopwatch.StartNew() : null;
            int count = chunk.AnimUV2Cache.Length;
            if (chunk.AnimVertexBuffer == null || chunk.AnimVertexBuffer.Length != count)
                chunk.AnimVertexBuffer = new Vector3[count];
            Vector3[] verts = chunk.AnimVertexBuffer;
            for (int i = 0; i < count; i++)
            {
                verts[i] = chunk.AnimBaseVerts[i];
                // UV3.x=延迟起点、UV3.y=延迟终点。Wave 在窗口内执行 0→1→0 脉冲，
                // 形成移动波带；其他模式保持 0→1。
                float delayStart = chunk.AnimUV3Cache[i].x;
                float delayEnd = chunk.AnimUV3Cache[i].y;
                if (delayEnd > delayStart + 0.0001f)
                {
                    float t = Mathf.Clamp01((progress - delayStart) / (delayEnd - delayStart));
                    if (chunk.AnimationReturnsToStart)
                    {
                        float pulse = 1f - Mathf.Abs(t * 2f - 1f);
                        t = pulse * pulse * (3f - 2f * pulse);
                    }
                    verts[i].y = Mathf.Lerp(chunk.AnimUV2Cache[i].x, chunk.AnimUV2Cache[i].y, t);
                }
            }
            chunk.TerrainFilter.sharedMesh.vertices = verts;
            if (timing != null)
            {
                timing.Stop();
                AnimProgressFrameCount++;
                AnimProgressFrameMsTotal += timing.Elapsed.TotalMilliseconds;
                if (timing.Elapsed.TotalMilliseconds > AnimProgressFrameMsMax)
                    AnimProgressFrameMsMax = timing.Elapsed.TotalMilliseconds;
            }
        }

        // Wave 不使用 Transition Shader/clip，且必须保留 Renderer 原有 MPB（迷雾/颜色参数）。
        if (!chunk.AnimationReturnsToStart)
        {
            chunk.AnimationBlock.SetFloat(ChunkProgressId, progress);
            chunk.TerrainRenderer.SetPropertyBlock(chunk.AnimationBlock);
        }

        // 湖海/河流幽灵淡出（§13.4）：alpha = 1 - progress
        UpdateGhostFade(chunk.FadeWaterGhost, progress);
        UpdateGhostFade(chunk.FadeRiverGhost, progress);
    }

    private void UpdateGhostFade(GameObject ghost, float progress)
    {
        if (ghost == null) return;
        var renderer = ghost.GetComponent<MeshRenderer>();
        if (renderer == null) return;
        if (!_ghostBlocks.TryGetValue(ghost, out UnityEngine.MaterialPropertyBlock block))
        {
            block = new UnityEngine.MaterialPropertyBlock();
            _ghostBlocks[ghost] = block;
        }
        block.SetFloat(FadeAlphaId, Mathf.Clamp01(1f - progress));
        renderer.SetPropertyBlock(block);
    }

    /// <summary>动画结束收尾：进度定格 1（顶点停在最终位置），销毁幽灵，恢复稳定材质，并清理属性块。幂等。</summary>
    public void FinalizeChunkAnimation(ChunkIndex index)
    {
        if (!_chunks.TryGetValue(index, out ChunkRenderData chunk)) return;
        if (chunk.TerrainRenderer == null) return;
        bool returnsToStart = chunk.AnimationReturnsToStart;
        if (!returnsToStart)
        {
            chunk.AnimationBlock.SetFloat(ChunkProgressId, 1f);
            chunk.TerrainRenderer.SetPropertyBlock(chunk.AnimationBlock);
        }

        // 【CPU动画-2026-08-05】顶点定格最终位置（progress=1 插值 → targetY），随后清理动画缓存。
        // Complete 路径直接调本方法，不会先 SetChunkAnimationProgress(1)，必须在此补写一次。
        if (chunk.AnimUV2Cache != null && chunk.TerrainFilter != null)
        {
            SetChunkAnimationProgress(index, 1f);
            chunk.AnimUV2Cache = null;
            chunk.AnimUV3Cache = null;
            chunk.AnimBaseVerts = null;
            chunk.AnimVertexBuffer = null;
            chunk.AnimationReturnsToStart = false;
        }

        // 【阶段四修订】恢复稳定材质（退出 *_Transition 动画 Shader；UV2/UV3 数据留在 mesh 内但
        // 稳定 Shader 不读取，且下次非动画重建经 mesh.Clear(false) 自动清除，§FillMeshData）
        if (chunk.StableTerrainMaterials != null)
        {
            chunk.TerrainRenderer.sharedMaterials = chunk.StableTerrainMaterials;
            chunk.StableTerrainMaterials = null;
        }

        if (chunk.FadeWaterGhost != null)
        {
            DestroyGhost(ref chunk.FadeWaterGhost, _ghostBlocks);
        }
        if (chunk.FadeRiverGhost != null)
        {
            DestroyGhost(ref chunk.FadeRiverGhost, _ghostBlocks);
        }
        // 【顶出方案-修订】销毁旧地形快照（含独占克隆 mesh，防泄漏）
        if (chunk.TerrainGhost != null)
        {
            DestroyGhost(ref chunk.TerrainGhost, _ghostBlocks);
        }
    }

    private static void DestroyGhost(
        ref GameObject ghost,
        Dictionary<GameObject, UnityEngine.MaterialPropertyBlock> blocks)
    {
        if (ghost == null) return;
        blocks?.Remove(ghost);
        Mesh mesh = ghost.GetComponent<MeshFilter>()?.sharedMesh;
        if (mesh != null) Object.Destroy(mesh);
        Object.Destroy(ghost);
        ghost = null;
    }

    // ── 脏 Chunk 计算（§七：改格 + 一环邻居 → 所属 Chunk 去重）──────

    private List<ChunkIndex> ComputeDirtyChunks(IReadOnlyCollection<HexCellData> changedCells)
    {
        var dirty = new HashSet<ChunkIndex>();
        int xNumber = _config.xNumber;
        var changedSeen = new HashSet<HexCellData>();
        foreach (HexCellData cell in changedCells)
        {
            if (cell == null || !changedSeen.Add(cell)) continue;
            CollectDirtyChunk(cell, xNumber, dirty);
            for (int d = 0; d < 6; d++)
            {
                HexCellData neighbor = _mapDataService.GetNeighbor(cell, (Enums.HexDirection)d);
                if (neighbor != null)
                    CollectDirtyChunk(neighbor, xNumber, dirty);
            }
        }
        return dirty.ToList();
    }

    private void CollectDirtyChunk(HexCellData cell, int xNumber, HashSet<ChunkIndex> dirty)
    {
        ChunkIndex index = ChunkIndex.Of(cell, xNumber);
        dirty.Add(index);
        if (!_chunks.ContainsKey(index))
        {
            // 首次出现的 Chunk：预建宿主
            List<HexCellData> cells = CollectChunkCells(index, xNumber);
            GetOrCreateChunk(index, cells);
        }
    }

    private List<HexCellData> CollectChunkCells(ChunkIndex index, int xNumber)
    {
        var cells = new List<HexCellData>();
        for (int row = index.Z * ChunkSize; row < (index.Z + 1) * ChunkSize; row++)
        {
            for (int col = index.X * ChunkSize; col < (index.X + 1) * ChunkSize; col++)
            {
                int order = row * xNumber + col;
                if (_mapDataService.TryGetCell(order, out HexCellData cell) && cell != null)
                    cells.Add(cell);
            }
        }
        return cells;
    }

    // ── Chunk 宿主管理 ───────────────────────────────────────

    private ChunkRenderData GetOrCreateChunk(ChunkIndex index, List<HexCellData> cells)
    {
        if (_chunks.TryGetValue(index, out ChunkRenderData existing)) return existing;

        GameObject root = new GameObject($"Chunk_{index.X}_{index.Z}");
        root.transform.SetParent(ChunkRootParent != null ? ChunkRootParent : transform, false);
        MapChunkView view = root.AddComponent<MapChunkView>();
        view.Index = index;

        int mapLayer = LayerMask.NameToLayer("Map");
        if (mapLayer >= 0) root.layer = mapLayer;

        GameObject terrainHost = new GameObject("Terrain");
        terrainHost.transform.SetParent(root.transform, false);
        if (mapLayer >= 0) terrainHost.layer = mapLayer;
        MeshFilter terrainFilter = terrainHost.AddComponent<MeshFilter>();
        MeshRenderer terrainRenderer = terrainHost.AddComponent<MeshRenderer>();
        MeshCollider terrainCollider = terrainHost.AddComponent<MeshCollider>();
        view.TerrainCollider = terrainCollider;

        GameObject waterHost = new GameObject("Water");
        waterHost.transform.SetParent(root.transform, false);
        waterHost.AddComponent<MeshFilter>();
        waterHost.AddComponent<MeshRenderer>();

        GameObject riverHost = new GameObject("River");
        riverHost.transform.SetParent(root.transform, false);
        riverHost.AddComponent<MeshFilter>();
        riverHost.AddComponent<MeshRenderer>();

        GameObject gridHost = new GameObject("Grid");
        gridHost.transform.SetParent(root.transform, false);
        if (mapLayer >= 0) gridHost.layer = mapLayer;
        MeshFilter gridFilter = gridHost.AddComponent<MeshFilter>();
        MeshRenderer gridRenderer = gridHost.AddComponent<MeshRenderer>();
        gridRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        gridRenderer.receiveShadows = false;
        gridHost.SetActive(false);

        var chunk = new ChunkRenderData
        {
            Index = index,
            Root = root,
            View = view,
            TerrainHost = terrainHost,
            TerrainFilter = terrainFilter,
            TerrainRenderer = terrainRenderer,
            TerrainCollider = terrainCollider,
            WaterHost = waterHost,
            RiverHost = riverHost,
            GridHost = gridHost,
            GridFilter = gridFilter,
            GridRenderer = gridRenderer,
            ActiveTerrain = new Mesh { name = $"ChunkTerrain_{index.X}_{index.Z}" },
            StagingTerrain = new Mesh { name = $"ChunkTerrainStaging_{index.X}_{index.Z}" },
            ActiveWater = new Mesh { name = $"ChunkWater_{index.X}_{index.Z}" },
            StagingWater = new Mesh { name = $"ChunkWaterStaging_{index.X}_{index.Z}" },
            ActiveRiver = new Mesh { name = $"ChunkRiver_{index.X}_{index.Z}" },
            StagingRiver = new Mesh { name = $"ChunkRiverStaging_{index.X}_{index.Z}" },
            ActiveGrid = new Mesh { name = $"ChunkGrid_{index.X}_{index.Z}" },
            StagingGrid = new Mesh { name = $"ChunkGridStaging_{index.X}_{index.Z}" },
            Cells = cells
        };
        _chunks[index] = chunk;
        return chunk;
    }

    // ── 两阶段 Chunk 构建（§九）──────────────────────────────

    /// <summary>阶段四：动画几何构建上下文（§20-10）。null = 普通重建（不写 UV2/UV3）。</summary>
    private sealed class AnimatedChunkBuildData
    {
        /// <summary>GenerateOrder → 旧 Height（快照于写数据前）。缺失 = 未变化（delta=0）。</summary>
        public IReadOnlyDictionary<int, float> OldHeights;

        /// <summary>GenerateOrder → 错峰延迟 [0,1]。缺失 = 0。</summary>
        public IReadOnlyDictionary<int, float> StaggerDelays;

        /// <summary>【阶梯修正-2026-08-05】Wave 模式行上升窗口（时间线比例，来自保留键
        /// MapVisualTransitionService.RiseWindowKey；行数≥2 时 = 行间距 × 波前厚度（约 3 行），
        /// 任意时刻约 3 行同时上升中——整行刚性平板仍可辨，但波前有厚度、推进放慢）。
        /// 0 = 非 Wave 模式：UV3.y 写 1，顶点公式回退 (1-delay) 原语义。</summary>
        public float RiseWindow;

        /// <summary>世界 Y 换算系数（elevationStep，§20-10：Height 级差 → 世界 Y 差）。</summary>
        public float ElevationStep = 1f;

        /// <summary>格子的高度变化量（世界 Y）：(新Height - 旧Height) * elevationStep。</summary>
        public float DeltaY(HexCellData cell)
        {
            if (cell == null) return 0f;
            if (OldHeights == null || !OldHeights.TryGetValue(cell.GenerateOrder, out float oldH)) return 0f;
            return (cell.Height - oldH) * ElevationStep;
        }

        /// <summary>格子的错峰延迟起点 [0,1]。</summary>
        public float Delay(HexCellData cell)
        {
            if (cell == null) return 0f;
            if (StaggerDelays == null || !StaggerDelays.TryGetValue(cell.GenerateOrder, out float d)) return 0f;
            return d;
        }

        /// <summary>格子的错峰延迟终点 [0,1]：Wave 模式 = Delay + RiseWindow（整行同窗，快升快停）；
        /// 非 Wave = 1（顶点公式 (p-start)/(end-start) 即回退 (1-delay) 原语义）。</summary>
        public float DelayEnd(HexCellData cell)
        {
            float d = Delay(cell);
            return RiseWindow > 0f ? d + RiseWindow : 1f;
        }
    }

    private ChunkStagingGeometry BuildChunkStaging(ChunkRenderData chunk, List<HexCellData> chunkCells, AnimatedChunkBuildData anim = null)
    {
        // 【程序化山脉-阶段 7.8】性能基线：仅 EnableChunkBuildTiming 时计时（默认关闭零开销）。
        System.Diagnostics.Stopwatch timing = EnableChunkBuildTiming ? System.Diagnostics.Stopwatch.StartNew() : null;

        _solidVertices.Clear();
        _lakeOrSeaVertices.Clear();
        _rectVerticesByCell.Clear();
        _genericRectangleMeshes.Clear();

        // 矩形 profile 需要目标 Chunk + 一环；而一环格生成 profile 时还会读取自己的邻居 solid，
        // 因此 solid 必须再扩一环（目标 Chunk + 二环）。两类依赖集不能共用同一个一环集合。
        HashSet<HexCellData> profileCells = ExpandCells(chunkCells, 1);
        HashSet<HexCellData> solidCells = ExpandCells(chunkCells, 2);
        List<HexCellData> profileList = profileCells.ToList();
        List<HexCellData> solidList = solidCells.ToList();

        NormalizeWaterState(profileList);

        // 阶段 1：预生成矩形 profile（目标 Chunk + halo；只供依赖，不输出几何）
        PreBuildRectProfiles(solidList, profileList);

        var staging = new ChunkStagingGeometry { Index = chunk.Index };
        staging.AnimationReturnsToStart = anim != null && anim.RiseWindow > 0f;
        staging.Terrain = BuildChunkTerrain(chunkCells, profileList, anim);
        staging.River = BuildChunkRiver(chunkCells);
        staging.Water = BuildChunkWater(chunkCells, profileList);
        staging.Grid = BuildChunkGrid(chunkCells, staging.Terrain);

        // 【程序化山脉-阶段 7.8】按"本 Chunk 是否含山体渲染槽"分类累计构建耗时。
        if (timing != null)
        {
            timing.Stop();
            ChunkBuildCount++;
            if (staging.Terrain != null && staging.Terrain.HasMountain)
            {
                MountainChunkBuildCount++;
                MountainChunkBuildMsTotal += timing.Elapsed.TotalMilliseconds;
            }
            else
            {
                ChunkBuildMsTotal += timing.Elapsed.TotalMilliseconds;
            }
        }
        return staging;
    }

    private void NormalizeWaterState(IEnumerable<HexCellData> cells)
    {
        if (cells == null) return;
        foreach (HexCellData cell in cells)
        {
            if (cell == null) continue;
            if (WaterLevelConfig.IsWater(cell))
            {
                cell.waterLevel = _config.seaLevel;
            }
            else
            {
                cell.isCoast = false;
                cell.waterLevel = 0f;
            }
        }
    }

    private HashSet<HexCellData> ExpandCells(IEnumerable<HexCellData> source, int rings)
    {
        var result = new HashSet<HexCellData>();
        var frontier = new HashSet<HexCellData>();
        foreach (HexCellData cell in source)
        {
            if (cell == null || !result.Add(cell)) continue;
            frontier.Add(cell);
        }

        for (int ring = 0; ring < rings; ring++)
        {
            var next = new HashSet<HexCellData>();
            foreach (HexCellData cell in frontier)
            {
                for (int d = 0; d < 6; d++)
                {
                    HexCellData neighbor = _mapDataService.GetNeighbor(cell, (Enums.HexDirection)d);
                    if (neighbor != null && result.Add(neighbor))
                        next.Add(neighbor);
                }
            }
            frontier = next;
            if (frontier.Count == 0) break;
        }
        return result;
    }

    /// <summary>阶段 1：solid 覆盖二环，矩形/湖海 profile 覆盖一环（供跨 Chunk 三角依赖读取，§9-1）。</summary>
    private void PreBuildRectProfiles(List<HexCellData> solidCells, List<HexCellData> profileCells)
    {
        // 实心 44 点（矩形过渡依赖邻居 solid）
        foreach (HexCellData cell in solidCells)
        {
            if (_solidVertices.ContainsKey(cell.GenerateOrder)) continue;
            SolidAreaMeshData solid = _meshGenerator.BuildSolidArea(cell, _view);
            _solidVertices[cell.GenerateOrder] = solid.Vertices;
            cell.RealCenterWorldCoordinate = solid.Center;
        }

        // 湖海 25 点（湖海矩形/三角/海岸依赖）
        foreach (HexCellData cell in profileCells)
        {
            if (_lakeOrSeaVertices.ContainsKey(cell.GenerateOrder)) continue;
            if (!WaterLevelConfig.IsWater(cell)) continue;
            CellBuildContext ctx = MakeBuildContext(cell);
            _lakeOrSeaVertices[cell.GenerateOrder] = _meshGenerator.BuildLakeOrSeaVertices(ctx);
        }

        // 矩形 profile：NE/E/SE 三方向（三角过渡读取邻格 profile，§9-1）
        Enums.HexDirection[] dirs = { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE };
        foreach (HexCellData cell in profileCells)
        {
            foreach (Enums.HexDirection dir in dirs)
            {
                var key = (cell.GenerateOrder, dir);
                if (_genericRectangleMeshes.ContainsKey(key)) continue;
                if (_mapDataService.GetNeighbor(cell, dir) == null) continue;
                CellBuildContext ctx = MakeBuildContext(cell);
                GetGenericRectangleMesh(ctx, dir);
                // 【程序化山脉】山体 rect 同 halo 预构建（tri 复用 profiles；跨 Chunk 一致）
                if (MountainGeometryBuilder.HasVisibleMountain(cell)
                    || MountainGeometryBuilder.HasVisibleMountain(_mapDataService.GetNeighbor(cell, dir)))
                {
                    GetMountainRectangleMesh(ctx, dir);
                }
            }
        }
    }

    // ── 地形构建（阶段 2：只输出目标 Chunk 自有几何）────────────

    private TerrainGeometry BuildChunkTerrain(
        List<HexCellData> chunkCells,
        List<HexCellData> profileCells,
        AnimatedChunkBuildData anim = null)
    {
        var verticesList = new List<Vector3>();
        var uvList = new List<Vector2>();
        // 【阶段四】动画通道（§20-10）：UV2=(startVertexY,targetVertexY)、UV3=(delayStart,delayEnd)
        var uv2List = anim != null ? new List<Vector2>() : null;
        var uv3List = anim != null ? new List<Vector2>() : null;
        var highDrawOrderList = new List<int>();
        var flatDrawOrderList = new List<int>();
        var seafloorDrawOrderList = new List<int>();
        var subList = new List<List<int>> { highDrawOrderList, flatDrawOrderList, seafloorDrawOrderList };
        // 【程序化山脉-阶段 3.6】替换式拓扑：山体面只进 MountainIndices，被替换原始面只进 CollisionIndices
        var mountainIndices = new List<int>();
        // 山-普通山侧半 rect：按普通侧地形材质分组；BlendData 在构建末尾统一回填 UV4。
        var mountainBoundaryGroups = new Dictionary<Material, List<int>>();
        var mountainBoundaryBlendRanges = new List<(int start, Vector4[] data)>();
        var collisionIndices = new List<int>();
        // 【程序化山脉-阶段 5.8】仅山体渲染顶点区间（平坦 (start,count) 对，供碰撞索引校验）
        var mountainRanges = new List<int>();
        System.Func<HexCellData, Enums.HexDirection, HexCellData> neighborOf = (c, d) => _mapDataService.GetNeighbor(c, d);
        // 实心区域（只输出目标 Chunk 格）
        foreach (HexCellData hexCellData in chunkCells)
        {
            Vector3[] solid = _solidVertices[hexCellData.GenerateOrder];
            int solidStart = verticesList.Count;
            verticesList.AddRange(solid);
            uvList.AddRange(_meshGenerator.BuildSolidAreaUV(hexCellData));

            if (anim != null)
            {
                float delta = anim.DeltaY(hexCellData);
                float delay = anim.Delay(hexCellData);
                float delayEnd = anim.DelayEnd(hexCellData);
                for (int c = 0; c < solid.Length; c++)
                {
                    float y = solid[c].y;
                    uv2List.Add(new Vector2(y - delta, y));
                    uv3List.Add(new Vector2(delay, delayEnd));
                }
            }

            List<Enums.HexDirection> d;
            int index = MainMeshSolidAreaDrawOrderFunction(hexCellData, out d);
            List<int> ints = index switch
            {
                2 => _meshGenerator.BuildSolidAreaDrawOrder2(hexCellData, d[0]),
                3 => _meshGenerator.BuildSolidAreaDrawOrder3(hexCellData, d[0], d[1]),
                _ => _meshGenerator.BuildSolidAreaDrawOrder1(hexCellData)
            };
            if (MountainGeometryBuilder.HasVisibleMountain(hexCellData))
            {
                // 有效山格：原 44 点仍写入顶点数组（供 collision 扇形），顶面索引不进地形槽
                foreach (int i in ints) collisionIndices.Add(i + solidStart);
                CellGeometry mountain = MountainGeometryBuilder.BuildSolidMountain(hexCellData, solid, neighborOf);
                int mountainStart = verticesList.Count;
                verticesList.AddRange(mountain.Vertices);
                mountainRanges.Add(mountainStart);
                mountainRanges.Add(mountain.Vertices.Length);
                // 山体槽 UV0 契约见 MountainMaterialContract（UV0.x=ridgeKey01、UV0.y=tier 编码），普通地形 UV 逻辑禁止重解释
                uvList.AddRange(mountain.UVs);
                if (anim != null)
                    MountainGeometryBuilder.AppendMountainAnimUV(mountain,
                        c => anim.DeltaY(c), c => anim.Delay(c), c => anim.DelayEnd(c), uv2List, uv3List);
                foreach (int i in mountain.Indices) mountainIndices.Add(i + mountainStart);
            }
            else
            {
                MainMeshDrawOrderElementAddRule(hexCellData, ints, ref subList, solidStart);
                foreach (int i in ints) collisionIndices.Add(i + solidStart);
            }
        }

        // 矩形过渡（只输出目标 Chunk 格；profile 已在阶段 1 预生成）
        var rectGroups = new Dictionary<(Material, Material), List<int>>();
        Enums.HexDirection[] dirs = { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE };
        foreach (HexCellData hexCellData in chunkCells)
        {
            foreach (Enums.HexDirection dir in dirs)
            {
                if (_mapDataService.GetNeighbor(hexCellData, dir) == null) continue;
                int IndexOffset = verticesList.Count;
                bool isSlope = true, isRiver = false;
                MainMeshRectFunction(hexCellData, dir, out isSlope, out isRiver);
                CellBuildContext ctx = MakeBuildContext(hexCellData);
                List<int> ints = new List<int>();

                if (isRiver)
                {
                    if (isSlope)
                    {
                        int preCount = verticesList.Count;
                        List<Vector3> rectVerts = _meshGenerator.BuildRectVertices(ctx, dir);
                        verticesList.AddRange(rectVerts);
                        uvList.AddRange(_meshGenerator.BuildRectUV(ctx, dir));
                        OtherMeshDrawOrderElementAddRule(hexCellData, _meshGenerator.BuildRectSlopeRiverDrawOrder(ctx, dir), ref ints, IndexOffset);
                        _rectVerticesByCell[(hexCellData.GenerateOrder, dir)] = rectVerts;
                        if (anim != null)
                            AppendRiverRectAnimUV(anim, hexCellData, rectVerts, uv2List, uv3List);
                    }
                    else
                    {
                        int preCount = verticesList.Count;
                        List<Vector3> rectVerts = _meshGenerator.BuildRectStepVertices(ctx, dir);
                        verticesList.AddRange(rectVerts);
                        uvList.AddRange(_meshGenerator.BuildRectStepUV(ctx, rectVerts));
                        OtherMeshDrawOrderElementAddRule(hexCellData, _meshGenerator.BuildRectStepRiverDrawOrder(ctx, rectVerts), ref ints, IndexOffset);
                        _rectVerticesByCell[(hexCellData.GenerateOrder, dir)] = rectVerts;
                        if (anim != null)
                            AppendRiverRectAnimUV(anim, hexCellData, rectVerts, uv2List, uv3List);
                    }
                    // 河 rect 恒不贴山（决策 ③），原始面同时进 collision
                    foreach (int i in ints) collisionIndices.Add(i);

                    Material matA = HexController.SetHexMaterial(hexCellData, _config.mapMaterial);
                    Material matB = HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, dir), _config.mapMaterial);
                    var key = (matA, matB);
                    if (!rectGroups.ContainsKey(key))
                        rectGroups[key] = new List<int>();
                    rectGroups[key].AddRange(ints);
                }
                else
                {
                    _rectVerticesByCell[(hexCellData.GenerateOrder, dir)] = new List<Vector3>();
                    int preCount = verticesList.Count;
                    HexCellData neighbor = _mapDataService.GetNeighbor(hexCellData, dir);
                    bool edgeMountain = MountainGeometryBuilder.HasVisibleMountain(hexCellData)
                        || MountainGeometryBuilder.HasVisibleMountain(neighbor);
                    if (!edgeMountain)
                    {
                        // 原路径完全不变：渲染槽（共享顶点同时进 collision）
                        RectangleTransitionMeshData usedRect = RectFlat(_config.shadingStyle)
                            ? RectangleTransitionMesh.ToFlatShaded(GetGenericRectangleMesh(ctx, dir))
                            : GetGenericRectangleMesh(ctx, dir);
                        verticesList.AddRange(usedRect.Vertices);
                        uvList.AddRange(usedRect.UVs);
                        OtherMeshDrawOrderElementAddRule(hexCellData, usedRect.Indices, ref ints, IndexOffset);
                        foreach (int i in ints) collisionIndices.Add(i);
                        if (anim != null)
                            AppendRectAnimUV(anim, hexCellData, dir, usedRect, uv2List, uv3List);

                        Material matA = HexController.SetHexMaterial(hexCellData, _config.mapMaterial);
                        Material matB = HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, dir), _config.mapMaterial);
                        var key = (matA, matB);
                        if (!rectGroups.ContainsKey(key))
                            rectGroups[key] = new List<int>();
                        rectGroups[key].AddRange(ints);
                    }
                    else
                    {
                        // 山边 rect：原始 surface 只进 collision（替换式拓扑，决策 ⑤）；
                        // collision-only plain 顶点沿用基础地形通道规则（阶段 5.3）
                        RectangleTransitionMeshData plain = GetGenericRectangleMesh(ctx, dir);
                        verticesList.AddRange(plain.Vertices);
                        uvList.AddRange(plain.UVs);
                        if (anim != null)
                            AppendRectAnimUV(anim, hexCellData, dir, plain, uv2List, uv3List);
                        foreach (int i in plain.Indices) collisionIndices.Add(i + IndexOffset);

                        // 山-山 rect 进主山体槽；山-普通山侧半 rect 进按普通侧地形材质分组的融合槽。
                        MountainRectBuild mountainBuild = GetMountainRectangleMesh(ctx, dir);
                        bool boundaryBlend = mountainBuild.PlainRect != null;
                        CellGeometry mountain = boundaryBlend
                            ? MountainGeometryBuilder.RectToTerrainBlendRender(mountainBuild, hexCellData, neighbor)
                            : MountainGeometryBuilder.RectToRender(mountainBuild, hexCellData, neighbor);
                        int mountainOffset = verticesList.Count;
                        verticesList.AddRange(mountain.Vertices);
                        mountainRanges.Add(mountainOffset);
                        mountainRanges.Add(mountain.Vertices.Length);
                        uvList.AddRange(mountain.UVs);
                        if (anim != null)
                            MountainGeometryBuilder.AppendMountainAnimUV(mountain,
                                c => anim.DeltaY(c), c => anim.Delay(c), c => anim.DelayEnd(c), uv2List, uv3List);
                        if (boundaryBlend)
                        {
                            Material terrainMaterial = MountainGeometryBuilder.HasVisibleMountain(hexCellData)
                                ? HexController.SetHexMaterial(neighbor, _config.mapMaterial)
                                : HexController.SetHexMaterial(hexCellData, _config.mapMaterial);
                            if (!mountainBoundaryGroups.TryGetValue(terrainMaterial, out List<int> boundaryIndices))
                            {
                                boundaryIndices = new List<int>();
                                mountainBoundaryGroups[terrainMaterial] = boundaryIndices;
                            }
                            foreach (int i in mountain.Indices) boundaryIndices.Add(i + mountainOffset);
                            mountainBoundaryBlendRanges.Add((mountainOffset, mountain.BlendData));
                        }
                        else
                        {
                            foreach (int i in mountain.Indices) mountainIndices.Add(i + mountainOffset);
                        }

                        // 【2026-08-07 决策 ④ 细化：格界劈半】山-普通 rect 的普通半边回地形槽
                        // （地形材质/格线，与原 rect 同 (matA,matB) 分组键与动画通道规则），
                        // 山体视觉边界收回到格界线；山-山 rect 无 PlainRect（整面山体）。
                        // 几何上两件在格界点严格闭合（同一 boundary 点位，MountainGeometryBuilder 保证）。
                        if (mountainBuild.PlainRect != null)
                        {
                            RectangleTransitionMeshData plainHalf = RectFlat(_config.shadingStyle)
                                ? RectangleTransitionMesh.ToFlatShaded(mountainBuild.PlainRect)
                                : mountainBuild.PlainRect;
                            int plainHalfOffset = verticesList.Count;
                            verticesList.AddRange(plainHalf.Vertices);
                            uvList.AddRange(plainHalf.UVs);
                            var plainHalfInts = new List<int>();
                            OtherMeshDrawOrderElementAddRule(hexCellData, plainHalf.Indices, ref plainHalfInts, plainHalfOffset);
                            if (anim != null)
                                AppendRectAnimUV(anim, hexCellData, dir, plainHalf, uv2List, uv3List);
                            Material halfMatA = HexController.SetHexMaterial(hexCellData, _config.mapMaterial);
                            Material halfMatB = HexController.SetHexMaterial(neighbor, _config.mapMaterial);
                            var halfKey = (halfMatA, halfMatB);
                            if (!rectGroups.ContainsKey(halfKey))
                                rectGroups[halfKey] = new List<int>();
                            rectGroups[halfKey].AddRange(plainHalfInts);
                        }
                    }
                }
            }
        }
        var mergedRectIndices = new List<List<int>>(rectGroups.Values);
        var mergedMaterialAs = rectGroups.Keys.Select(k => k.Item1).ToList();
        var mergedMaterialBs = rectGroups.Keys.Select(k => k.Item2).ToList();

        // 三角过渡（只输出目标 Chunk 格；依赖阶段 1 的 halo profile）
        var triGroups = new Dictionary<(Material, Material, Material), List<int>>();
        Enums.HexDirection[][] triDirs =
        {
            new[] { Enums.HexDirection.NE, Enums.HexDirection.E },
            new[] { Enums.HexDirection.E, Enums.HexDirection.SE }
        };
        foreach (HexCellData hexCellData in chunkCells)
        {
            foreach (Enums.HexDirection[] pair in triDirs)
            {
                if (_mapDataService.GetNeighbor(hexCellData, pair[0]) == null ||
                    _mapDataService.GetNeighbor(hexCellData, pair[1]) == null) continue;
                int IndexOffset = verticesList.Count;
                List<int> ints = new List<int>();
                CellBuildContext ctx = MakeBuildContext(hexCellData);
                HexCellData neighborA = _mapDataService.GetNeighbor(hexCellData, pair[0]);
                HexCellData neighborB = _mapDataService.GetNeighbor(hexCellData, pair[1]);
                bool allMountain = MountainGeometryBuilder.HasVisibleMountain(hexCellData)
                    && MountainGeometryBuilder.HasVisibleMountain(neighborA)
                    && MountainGeometryBuilder.HasVisibleMountain(neighborB);
                if (allMountain)
                {
                    // 3 山格 tri：山体封口进山体槽（决策 ⑤）
                    CellGeometry mountain = MountainGeometryBuilder.BuildTriangleMountain(
                        hexCellData, neighborOf, (c, d) => GetMountainRectangleMesh(MakeBuildContext(c), d).Rect,
                        pair[0], pair[1]);
                    int mountainOffset = verticesList.Count;
                    verticesList.AddRange(mountain.Vertices);
                    mountainRanges.Add(mountainOffset);
                    mountainRanges.Add(mountain.Vertices.Length);
                    uvList.AddRange(mountain.UVs);
                    if (anim != null)
                        MountainGeometryBuilder.AppendMountainAnimUV(mountain,
                            c => anim.DeltaY(c), c => anim.Delay(c), c => anim.DelayEnd(c), uv2List, uv3List);
                    foreach (int i in mountain.Indices) mountainIndices.Add(i + mountainOffset);

                    // 原始 tri 只进 collision（替换式拓扑）；collision-only plain 顶点沿用基础地形通道规则。
                    // 【阶段 7.4/7.6 修复】plain tri 追加在山体 tri 之后，索引偏移必须取 plain 的实际
                    // 追加位置（verticesList.Count），不能复用 1229 行捕获的山体 tri 之前的 IndexOffset——
                    // 否则 collision 索引落入山体顶点区间（MountainVertexRanges 校验拒绝，初始地图缺地形）。
                    TriangleTransitionMeshData plain = GetGenericTriangleMesh(ctx, pair[0], pair[1]);
                    int plainOffset = verticesList.Count;
                    verticesList.AddRange(plain.Vertices);
                    uvList.AddRange(plain.UVs);
                    if (anim != null)
                        AppendTriangleAnimUV(anim, hexCellData, pair, plain, uv2List, uv3List);
                    foreach (int i in plain.Indices) collisionIndices.Add(i + plainOffset);
                }
                else
                {
                    TriangleTransitionMeshData triangle = GetGenericTriangleMesh(ctx, pair[0], pair[1]);

                    // 【2026-08-10 封闭墙鞍部】两封闭墙脊线格 + 一格普通格：原 terrain tri 只进
                    // collision（替换式拓扑，与 3 山格三角同模型），鞍部三角进山体槽；
                    // 普通格表面不动，角点抬升由 CornerHeight 墙鞍规则统一供给（扇面/rect/本三角同源）。
                    CellGeometry wallCol = MountainGeometryBuilder.BuildWallColTriangle(
                        hexCellData, neighborA, neighborB, triangle);
                    if (wallCol != null)
                    {
                        verticesList.AddRange(triangle.Vertices);
                        uvList.AddRange(triangle.UVs);
                        if (anim != null)
                            AppendTriangleAnimUV(anim, hexCellData, pair, triangle, uv2List, uv3List);
                        foreach (int i in triangle.Indices) collisionIndices.Add(i + IndexOffset);

                        int colOffset = verticesList.Count;
                        verticesList.AddRange(wallCol.Vertices);
                        mountainRanges.Add(colOffset);
                        mountainRanges.Add(wallCol.Vertices.Length);
                        uvList.AddRange(wallCol.UVs);
                        if (anim != null)
                            MountainGeometryBuilder.AppendMountainAnimUV(wallCol,
                                c => anim.DeltaY(c), c => anim.Delay(c), c => anim.DelayEnd(c), uv2List, uv3List);
                        foreach (int i in wallCol.Indices) mountainIndices.Add(i + colOffset);
                        continue;
                    }

                    verticesList.AddRange(triangle.Vertices);
                    uvList.AddRange(triangle.UVs);
                    OtherMeshDrawOrderElementAddRule(hexCellData, triangle.Indices, ref ints, IndexOffset);
                    foreach (int i in ints) collisionIndices.Add(i);
                    if (anim != null)
                        AppendTriangleAnimUV(anim, hexCellData, pair, triangle, uv2List, uv3List);

                    Material matA = HexController.SetHexMaterial(hexCellData, _config.mapMaterial);
                    Material matB = HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, pair[0]), _config.mapMaterial);
                    Material matC = HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, pair[1]), _config.mapMaterial);
                    var key = (matA, matB, matC);
                    if (!triGroups.ContainsKey(key))
                        triGroups[key] = new List<int>();
                    triGroups[key].AddRange(ints);

                    // 【2026-08-07 续24】两格连续脊线 + 第三格普通：保留完整 terrain tri，
                    // 另叠加一条由脊边触发的山体肩部 wedge。第三格不变山格、不参与玩法占地，
                    // collision 仍只使用原 terrain tri；wedge 仅进入山体渲染槽。
                    CellGeometry shoulder = MountainGeometryBuilder.BuildRidgeEdgeTriangleShoulder(
                        hexCellData, neighborA, neighborB, triangle);
                    if (shoulder != null && shoulder.Vertices.Length > 0)
                    {
                        int shoulderOffset = verticesList.Count;
                        verticesList.AddRange(shoulder.Vertices);
                        mountainRanges.Add(shoulderOffset);
                        mountainRanges.Add(shoulder.Vertices.Length);
                        uvList.AddRange(shoulder.UVs);
                        if (anim != null)
                            MountainGeometryBuilder.AppendMountainAnimUV(shoulder,
                                c => anim.DeltaY(c), c => anim.Delay(c), c => anim.DelayEnd(c), uv2List, uv3List);
                        foreach (int i in shoulder.Indices) mountainIndices.Add(i + shoulderOffset);
                    }
                }
            }
        }
        var mergedTriIndices = new List<List<int>>(triGroups.Values);
        var mergedMaterialAsTri = triGroups.Keys.Select(k => k.Item1).ToList();
        var mergedMaterialBsTri = triGroups.Keys.Select(k => k.Item2).ToList();
        var mergedMaterialCsTri = triGroups.Keys.Select(k => k.Item3).ToList();
        var mergedMountainBoundaryIndices = new List<List<int>>(mountainBoundaryGroups.Values);
        var mergedMountainBoundaryMaterials = mountainBoundaryGroups.Keys.ToList();

        // 槽布局 = 3 基础 + N rect + M tri + B 山脚融合 + (主山体槽非空 ? 1 : 0)
        int slotCount = 3 + mergedRectIndices.Count + mergedTriIndices.Count
            + mergedMountainBoundaryIndices.Count + (mountainIndices.Count > 0 ? 1 : 0);
        int[][] arrArawOrder = new int[slotCount][];
        arrArawOrder[0] = subList[2].ToArray();
        arrArawOrder[1] = subList[1].ToArray();
        arrArawOrder[2] = subList[0].ToArray();
        int offset = 3;
        foreach (List<int> rect in mergedRectIndices)
            arrArawOrder[offset++] = rect.ToArray();
        foreach (List<int> tri in mergedTriIndices)
            arrArawOrder[offset++] = tri.ToArray();
        foreach (List<int> boundary in mergedMountainBoundaryIndices)
            arrArawOrder[offset++] = boundary.ToArray();
        if (mountainIndices.Count > 0)
            arrArawOrder[offset] = mountainIndices.ToArray();

        Vector4[] uv4 = null;
        if (mergedMountainBoundaryIndices.Count > 0)
        {
            uv4 = new Vector4[verticesList.Count];
            foreach ((int start, Vector4[] data) range in mountainBoundaryBlendRanges)
            {
                if (range.data == null) continue;
                for (int i = 0; i < range.data.Length; i++)
                    uv4[range.start + i] = range.data[i];
            }
        }

        // 【程序化山脉-阶段 5.7】动画构建预扩保守 bounds（覆盖 start→target 全程 + 山峰 + clip 余量）；
        // 普通无动画构建保持 null（FillMeshData 维持 RecalculateBounds 原行为，零变化）。
        Bounds? conservativeBounds = null;
        if (anim != null && verticesList.Count > 0 && uv2List != null && uv2List.Count == verticesList.Count)
            conservativeBounds = MountainGeometryBuilder.ComputeConservativeAnimBounds(verticesList, uv2List);

        return new TerrainGeometry
        {
            Vertices = verticesList.ToArray(),
            UVs = uvList.ToArray(),
            SubMeshIndices = arrArawOrder,
            BaseMaterials = _config.mapMaterial,
            RectAs = mergedMaterialAs.ToArray(),
            RectBs = mergedMaterialBs.ToArray(),
            TriAs = mergedMaterialAsTri.ToArray(),
            TriBs = mergedMaterialBsTri.ToArray(),
            TriCs = mergedMaterialCsTri.ToArray(),
            UV2s = uv2List?.ToArray(),
            UV3s = uv3List?.ToArray(),
            UV4s = uv4,
            MountainBoundaryMaterials = mergedMountainBoundaryMaterials.ToArray(),
            // 【程序化山脉】无山 Chunk 两数组均为 null（碰撞回落渲染 mesh，零额外内存）
            MountainIndices = mountainIndices.Count > 0 ? mountainIndices.ToArray() : null,
            CollisionIndices = (mountainIndices.Count > 0 || mergedMountainBoundaryIndices.Count > 0)
                ? collisionIndices.ToArray() : null,
            MountainVertexRanges = mountainRanges.Count > 0 ? mountainRanges.ToArray() : null,
            ConservativeBounds = conservativeBounds,
        };
    }

    // ── 阶段四：每顶点动画通道生成（§20-10）──────────────────
    // UV2.x=startVertexY（旧高度）、UV2.y=targetVertexY（新高度=当前顶点Y）；
    // UV3.x=错峰延迟起点、UV3.y=错峰延迟终点（动画窗口=end-start；
    //   【阶梯修正-2026-08-05】Wave 模式终点 = 起点 + 行上升窗口（=行间距，任意时刻仅 1 行
    //   上升中；原 y 恒为 1 的 participates 标志废弃——参与判定改由 end &gt; start 隐式给出）。
    //   非 Wave 模式终点恒 1，等价旧公式 (1-delay)）。
    // 矩形/三角过渡顶点按几何端点来源格写 start/end，内部插值点对端点插值（§13.3）。
    // 【程序化山脉-阶段 5.3】山体顶点不再使用恒等通道：走
    // MountainGeometryBuilder.AppendMountainAnimUV（逐顶点来源集合，决策 ㉙）。

    /// <summary>河流矩形过渡（顶点按固定布局混合两端格，按 owner 格 delta 近似，§13.3 第一版简化）。
    /// 【顶出方案】start/end 按 owner 格写。</summary>
    private static void AppendRiverRectAnimUV(
        AnimatedChunkBuildData anim,
        HexCellData owner,
        IReadOnlyList<Vector3> rectVerts,
        List<Vector2> uv2List,
        List<Vector2> uv3List)
    {
        float deltaOwner = anim.DeltaY(owner);
        float delayOwner = anim.Delay(owner);
        float delayEndOwner = anim.DelayEnd(owner);
        for (int c = 0; c < rectVerts.Count; c++)
        {
            float y = rectVerts[c].y;
            uv2List.Add(new Vector2(y - deltaOwner, y));
            uv3List.Add(new Vector2(delayOwner, delayEndOwner));
        }
    }

    /// <summary>非河流矩形过渡：按 UV.v（profile 进度 0=self→1=neighbor）插值两端格 delta/delay/end。</summary>
    private void AppendRectAnimUV(
        AnimatedChunkBuildData anim,
        HexCellData owner,
        Enums.HexDirection dir,
        RectangleTransitionMeshData rect,
        List<Vector2> uv2List,
        List<Vector2> uv3List)
    {
        HexCellData neighbor = _mapDataService.GetNeighbor(owner, dir);
        float deltaOwner = anim.DeltaY(owner);
        float deltaNeighbor = anim.DeltaY(neighbor);
        float delayOwner = anim.Delay(owner);
        float delayNeighbor = anim.Delay(neighbor);
        float delayEndOwner = anim.DelayEnd(owner);
        float delayEndNeighbor = anim.DelayEnd(neighbor);
        for (int c = 0; c < rect.Vertices.Count; c++)
        {
            float t = Mathf.Clamp01(rect.UVs[c].y);
            float delta = Mathf.Lerp(deltaOwner, deltaNeighbor, t);
            float delayStart = Mathf.Lerp(delayOwner, delayNeighbor, t);
            float delayEnd = Mathf.Lerp(delayEndOwner, delayEndNeighbor, t);
            float y = rect.Vertices[c].y;
            uv2List.Add(new Vector2(y - delta, y));
            uv3List.Add(new Vector2(delayStart, delayEnd));
        }
    }

    /// <summary>三角过渡：按重心 UV（(u,v)，self 权重=1-u-v）插值三端格 delta/delay/end。</summary>
    private void AppendTriangleAnimUV(
        AnimatedChunkBuildData anim,
        HexCellData owner,
        Enums.HexDirection[] pair,
        TriangleTransitionMeshData triangle,
        List<Vector2> uv2List,
        List<Vector2> uv3List)
    {
        HexCellData neighborA = _mapDataService.GetNeighbor(owner, pair[0]);
        HexCellData neighborB = _mapDataService.GetNeighbor(owner, pair[1]);
        float dSelf = anim.DeltaY(owner);
        float dA = anim.DeltaY(neighborA);
        float dB = anim.DeltaY(neighborB);
        float delaySelf = anim.Delay(owner);
        float delayA = anim.Delay(neighborA);
        float delayB = anim.Delay(neighborB);
        float endSelf = anim.DelayEnd(owner);
        float endA = anim.DelayEnd(neighborA);
        float endB = anim.DelayEnd(neighborB);
        for (int c = 0; c < triangle.Vertices.Count; c++)
        {
            Vector2 uv = triangle.UVs[c];
            float wSelf = 1f - uv.x - uv.y;
            float delta = wSelf * dSelf + uv.x * dA + uv.y * dB;
            float delayStart = wSelf * delaySelf + uv.x * delayA + uv.y * delayB;
            float delayEnd = wSelf * endSelf + uv.x * endA + uv.y * endB;
            float y = triangle.Vertices[c].y;
            uv2List.Add(new Vector2(y - delta, y));
            uv3List.Add(new Vector2(delayStart, delayEnd));
        }
    }

    // ── 河流构建（只输出目标 Chunk 格）──────────────────────

    private RiverGeometry BuildChunkRiver(
        List<HexCellData> chunkCells)
    {
        var verticesRiverWater = new List<Vector3>();
        var uvRiverWater = new List<Vector2>();
        var drawOrderRiverWater = new List<int>();

        foreach (HexCellData hexCellData in chunkCells)
        {
            List<int> ints = new List<int>();
            int IndexOffset = verticesRiverWater.Count;
            if (RiverMeshSolidAreaDrawOrderFunction(hexCellData) == null ||
                !hexCellData.hasRiverOutgoing ||
                hexCellData.RiverOutgoingDirection == Enums.HexDirection.None ||
                _mapDataService.GetNeighbor(hexCellData, hexCellData.RiverOutgoingDirection) == null)
                continue;

            CellBuildContext ctx = MakeBuildContext(hexCellData);
            Vector3[] riverVerts = _meshGenerator.BuildRiverVertices(ctx);
            verticesRiverWater.AddRange(riverVerts);
            List<int> l = RiverMeshSolidAreaDrawOrderFunction(hexCellData);
            ints.Clear();
            OtherMeshDrawOrderElementAddRule(hexCellData, l, ref ints, IndexOffset);
            drawOrderRiverWater.AddRange(ints);
            uvRiverWater.AddRange(_meshGenerator.BuildRiverUV(ctx, l, riverVerts.Length));
        }
        foreach (HexCellData hexCellData in chunkCells)
        {
            List<int> ints = new List<int>();
            int IndexOffset = verticesRiverWater.Count;
            if (RiverMeshSolidAreaDrawOrderFunction(hexCellData) == null) continue;

            CellBuildContext ctx = MakeBuildContext(hexCellData);
            verticesRiverWater.AddRange(_meshGenerator.BuildOutgoingRiverVertices(ctx));
            List<int> l = new List<int>();
            l.AddRange(RiverMeshDownstreamDrawOrderFunction());
            OtherMeshDrawOrderElementAddRule(hexCellData, l, ref ints, IndexOffset);
            drawOrderRiverWater.AddRange(ints);
            uvRiverWater.AddRange(_meshGenerator.BuildOutgoingRiverSlopUV());
        }

        if (drawOrderRiverWater.Count % 3 == 0 && drawOrderRiverWater.Count != 0)
        {
            return new RiverGeometry
            {
                Vertices = verticesRiverWater.ToArray(),
                UVs = uvRiverWater.ToArray(),
                Indices = drawOrderRiverWater.ToArray()
            };
        }
        return null;
    }

    // ── 湖海构建（只输出目标 Chunk 格；依赖 halo 湖海注册表）────────

    private WaterGeometry BuildChunkWater(
        List<HexCellData> chunkCells,
        List<HexCellData> profileCells)
    {
        var verticesLakeOrSea = new List<Vector3>();
        var uvLakeOrSea = new List<Vector2>();
        var drawOrderLakeOrSea = new List<int>();
        var drawOrderCoast = new List<int>();

        // 湖海实心（只输出目标 Chunk 格；注册表已在阶段 1 就位）
        foreach (HexCellData hexCellData in chunkCells)
        {
            if (!WaterLevelConfig.IsWater(hexCellData)) continue;
            hexCellData.HexType = Enums.HexType.LakeOrSea;
            hexCellData.isCoast = true;
            hexCellData.waterLevel = _config.seaLevel;

            int IndexOffset = verticesLakeOrSea.Count;
            CellBuildContext ctx = MakeBuildContext(hexCellData);
            Vector3[] lakeVerts = _meshGenerator.BuildLakeOrSeaVertices(ctx);
            _lakeOrSeaVertices[hexCellData.GenerateOrder] = lakeVerts;
            verticesLakeOrSea.AddRange(lakeVerts);
            uvLakeOrSea.AddRange(_meshGenerator.BuildLakeOrSeaUV());

            List<int> ints = new List<int>();
            List<int> l = new List<int>();
            l.AddRange(LakeOrSeaMeshSolidAreaDrawOrderFunction());
            OtherMeshDrawOrderElementAddRule(hexCellData, l, ref ints, IndexOffset);
            drawOrderLakeOrSea.AddRange(ints);
        }

        // 湖海矩形过渡（与邻居同为水时生成连接面）
        Enums.HexDirection[] dirs = { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE };
        foreach (HexCellData hexCellData in chunkCells)
        {
            if (!WaterLevelConfig.IsWater(hexCellData)) continue;
            foreach (Enums.HexDirection dir in dirs)
            {
                HexCellData neighbor = _mapDataService.GetNeighbor(hexCellData, dir);
                if (neighbor == null || !_lakeOrSeaVertices.ContainsKey(neighbor.GenerateOrder)) continue;

                int IndexOffset = verticesLakeOrSea.Count;
                CellBuildContext ctx = MakeBuildContext(hexCellData);
                verticesLakeOrSea.AddRange(_meshGenerator.BuildLakeOrSeaRectVertices(ctx, dir));
                uvLakeOrSea.AddRange(_meshGenerator.BuildLakeOrSeaRectUV(dir));

                List<int> ints = new List<int>();
                List<int> l = new List<int>();
                l.AddRange(LakeOrSeaMeshRectDrawOrderFunction(dir));
                ints.Clear();
                OtherMeshDrawOrderElementAddRule(hexCellData, l, ref ints, IndexOffset);
                drawOrderLakeOrSea.AddRange(ints);
            }
        }

        // 湖海三角过渡（两个邻居同为水时生成）
        Enums.HexDirection[][] triDirs =
        {
            new[] { Enums.HexDirection.NE, Enums.HexDirection.E },
            new[] { Enums.HexDirection.E, Enums.HexDirection.SE }
        };
        foreach (HexCellData hexCellData in chunkCells)
        {
            if (!WaterLevelConfig.IsWater(hexCellData)) continue;
            foreach (Enums.HexDirection[] pair in triDirs)
            {
                HexCellData neighborA = _mapDataService.GetNeighbor(hexCellData, pair[0]);
                HexCellData neighborB = _mapDataService.GetNeighbor(hexCellData, pair[1]);
                if (neighborA == null || neighborB == null ||
                    !_lakeOrSeaVertices.ContainsKey(neighborA.GenerateOrder) ||
                    !_lakeOrSeaVertices.ContainsKey(neighborB.GenerateOrder)) continue;

                int IndexOffset = verticesLakeOrSea.Count;
                CellBuildContext ctx = MakeBuildContext(hexCellData);
                verticesLakeOrSea.AddRange(_meshGenerator.BuildLakeOrSeaTriVertices(ctx, pair[0], pair[1]));
                uvLakeOrSea.AddRange(_meshGenerator.BuildLakeOrSeaTriUV(pair[0], pair[1]));

                List<int> ints = new List<int>();
                List<int> l = new List<int>();
                l.AddRange(LakeOrSeaMeshTriDrawOrderFunction(pair[0], pair[1]));
                ints.Clear();
                OtherMeshDrawOrderElementAddRule(hexCellData, l, ref ints, IndexOffset);
                drawOrderLakeOrSea.AddRange(ints);
            }
        }

        // 海岸矩形 + 三角（邻格非水）
        Enums.HexDirection[] allDirs = { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE, Enums.HexDirection.SW, Enums.HexDirection.W, Enums.HexDirection.NW };
        foreach (HexCellData hexCellData in chunkCells)
        {
            if (!WaterLevelConfig.IsWater(hexCellData)) continue;

            List<Enums.HexDirection> coastDirections = new List<Enums.HexDirection>();
            foreach (Enums.HexDirection h in allDirs)
            {
                if (_mapDataService.GetNeighbor(hexCellData, h) != null && !WaterLevelConfig.IsWater(_mapDataService.GetNeighbor(hexCellData, h)))
                    coastDirections.Add(h);
            }

            if (coastDirections.Count == 0) continue;

            int IndexOffset = verticesLakeOrSea.Count;
            CellBuildContext ctx = MakeBuildContext(hexCellData);
            List<Vector3> v = new List<Vector3>();
            foreach (Enums.HexDirection h in coastDirections)
                v.AddRange(_meshGenerator.BuildCoastRectVertices(ctx, h));
            verticesLakeOrSea.AddRange(v);
            uvLakeOrSea.AddRange(_meshGenerator.BuildCoastRectUV(v.ToArray()));

            List<int> ints = new List<int>();
            List<int> l = new List<int>();
            l.AddRange(CoastMeshRectDrawOrderFunction(v.ToArray()));
            ints.Clear();
            OtherMeshDrawOrderElementAddRule(hexCellData, l, ref ints, IndexOffset);
            drawOrderCoast.AddRange(ints);
        }
        foreach (HexCellData hexCellData in chunkCells)
        {
            if (!WaterLevelConfig.IsWater(hexCellData)) continue;

            List<Enums.HexDirection> coastDirections = new List<Enums.HexDirection>();
            foreach (Enums.HexDirection h in allDirs)
            {
                if (_mapDataService.GetNeighbor(hexCellData, h) != null && !WaterLevelConfig.IsWater(_mapDataService.GetNeighbor(hexCellData, h)))
                    coastDirections.Add(h);
            }

            if (coastDirections.Count == 0) continue;

            int IndexOffset = verticesLakeOrSea.Count;
            CellBuildContext ctx = MakeBuildContext(hexCellData);
            List<Vector3> v = new List<Vector3>();
            foreach (Enums.HexDirection h in coastDirections)
                v.AddRange(_meshGenerator.BuildCoastTriVertices(ctx, h));
            verticesLakeOrSea.AddRange(v);
            uvLakeOrSea.AddRange(_meshGenerator.BuildCoastTriUV(v.ToArray()));

            List<int> ints = new List<int>();
            List<int> l = new List<int>();
            l.AddRange(CoastMeshTriDrawOrderFunction(v.ToArray()));
            ints.Clear();
            OtherMeshDrawOrderElementAddRule(hexCellData, l, ref ints, IndexOffset);
            drawOrderCoast.AddRange(ints);
        }

        if (drawOrderLakeOrSea.Count == 0 && drawOrderCoast.Count == 0)
            return null;

        int[][] arrArawOrderLakeOrSea = new int[2][];
        arrArawOrderLakeOrSea[0] = drawOrderCoast.ToArray();
        arrArawOrderLakeOrSea[1] = drawOrderLakeOrSea.ToArray();
        return new WaterGeometry
        {
            Vertices = verticesLakeOrSea.ToArray(),
            UVs = uvLakeOrSea.ToArray(),
            Indices = arrArawOrderLakeOrSea
        };
    }

    private GridGeometry BuildChunkGrid(List<HexCellData> chunkCells, TerrainGeometry terrain)
    {
        if (_config == null || !_config.showHexGrid || terrain == null || terrain.Vertices == null)
            return null;

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        int[] surfaceIndices = GetGridSurfaceIndices(terrain);
        Enums.HexDirection[] directions =
        {
            Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE,
            Enums.HexDirection.SW, Enums.HexDirection.W, Enums.HexDirection.NW
        };

        foreach (HexCellData cell in chunkCells)
        {
            if (cell == null || WaterLevelConfig.IsWater(cell)) continue;
            Vector3[] corners = GetLogicalHexCorners(cell.CenterWorldCoordinate);
            foreach (Enums.HexDirection direction in directions)
            {
                HexCellData neighbor = _mapDataService.GetNeighbor(cell, direction);
                bool positiveDirection = direction == Enums.HexDirection.NE
                    || direction == Enums.HexDirection.E
                    || direction == Enums.HexDirection.SE;
                if (!positiveDirection && neighbor != null) continue;
                if (neighbor != null && WaterLevelConfig.IsWater(neighbor)) continue;

                GetLogicalEdgeCorners(corners, direction, out Vector3 start, out Vector3 end);
                int subdivisions = GetGridEdgeSubdivisions(cell, neighbor);
                AppendGridEdge(vertices, uvs, indices, start, end, subdivisions,
                    terrain.Vertices, surfaceIndices, cell.RealCenterWorldCoordinate.y);
            }
        }

        if (indices.Count == 0) return null;
        return new GridGeometry
        {
            Vertices = vertices.ToArray(),
            UVs = uvs.ToArray(),
            Indices = indices.ToArray()
        };
    }

    private Vector3[] GetLogicalHexCorners(Vector3 center)
    {
        float outer = _config.OuterRadius;
        float inner = _config.InnerRadius;
        return new[]
        {
            new Vector3(center.x, 0f, center.z + outer),
            new Vector3(center.x + inner, 0f, center.z + 0.5f * outer),
            new Vector3(center.x + inner, 0f, center.z - 0.5f * outer),
            new Vector3(center.x, 0f, center.z - outer),
            new Vector3(center.x - inner, 0f, center.z - 0.5f * outer),
            new Vector3(center.x - inner, 0f, center.z + 0.5f * outer),
        };
    }

    private static void GetLogicalEdgeCorners(
        Vector3[] corners, Enums.HexDirection direction, out Vector3 start, out Vector3 end)
    {
        int first = direction switch
        {
            Enums.HexDirection.NE => 0,
            Enums.HexDirection.E => 1,
            Enums.HexDirection.SE => 2,
            Enums.HexDirection.SW => 3,
            Enums.HexDirection.W => 4,
            _ => 5,
        };
        start = corners[first];
        end = corners[(first + 1) % 6];
    }

    private int GetGridEdgeSubdivisions(HexCellData cell, HexCellData neighbor)
    {
        if (neighbor == null || !_config.useHeightBasedSubdivision) return 1;
        float stepHeight = Mathf.Max(0.0001f, _config.stepHeight);
        int byHeight = Mathf.CeilToInt(Mathf.Abs(cell.Height - neighbor.Height) / stepHeight);
        return Mathf.Clamp(Mathf.Max(1, byHeight), 1, Mathf.Max(1, _config.maxStepSubdivision));
    }

    private void AppendGridEdge(
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<int> indices,
        Vector3 start,
        Vector3 end,
        int subdivisions,
        Vector3[] surfaceVertices,
        int[] surfaceIndices,
        float fallbackY)
    {
        Vector3 edge = end - start;
        Vector3 side = new Vector3(-edge.z, 0f, edge.x).normalized * (_config.gridLineWidth * 0.5f);
        float yOffset = _config.gridSurfaceOffset;
        int rowStart = vertices.Count;
        for (int i = 0; i <= subdivisions; i++)
        {
            float t = i / (float)subdivisions;
            Vector3 center = Vector3.Lerp(start, end, t);
            Vector3 left = center - side;
            Vector3 right = center + side;
            left.y = SampleTerrainSurfaceY(left, surfaceVertices, surfaceIndices, fallbackY) + yOffset;
            right.y = SampleTerrainSurfaceY(right, surfaceVertices, surfaceIndices, fallbackY) + yOffset;
            vertices.Add(left);
            vertices.Add(right);
            uvs.Add(new Vector2(t, 0f));
            uvs.Add(new Vector2(t, 1f));
        }

        for (int i = 0; i < subdivisions; i++)
        {
            int a = rowStart + i * 2;
            int b = a + 1;
            int c = a + 2;
            int d = a + 3;
            indices.Add(a);
            indices.Add(c);
            indices.Add(b);
            indices.Add(b);
            indices.Add(c);
            indices.Add(d);
        }
    }

    private static int[] GetGridSurfaceIndices(TerrainGeometry terrain)
    {
        if (terrain.CollisionIndices != null && terrain.CollisionIndices.Length > 0)
            return terrain.CollisionIndices;

        int count = terrain.SubMeshIndices.Sum(submesh => submesh != null ? submesh.Length : 0);
        var result = new int[count];
        int offset = 0;
        foreach (int[] submesh in terrain.SubMeshIndices)
        {
            if (submesh == null) continue;
            System.Array.Copy(submesh, 0, result, offset, submesh.Length);
            offset += submesh.Length;
        }
        return result;
    }

    private static float SampleTerrainSurfaceY(
        Vector3 point, Vector3[] vertices, int[] indices, float fallbackY)
    {
        const float tolerance = 0.0001f;
        float bestY = float.NegativeInfinity;
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            Vector3 a = vertices[indices[i]];
            Vector3 b = vertices[indices[i + 1]];
            Vector3 c = vertices[indices[i + 2]];
            float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(denominator) < tolerance) continue;
            float wa = ((b.z - c.z) * (point.x - c.x) + (c.x - b.x) * (point.z - c.z)) / denominator;
            float wb = ((c.z - a.z) * (point.x - c.x) + (a.x - c.x) * (point.z - c.z)) / denominator;
            float wc = 1f - wa - wb;
            if (wa < -tolerance || wb < -tolerance || wc < -tolerance) continue;
            bestY = Mathf.Max(bestY, wa * a.y + wb * b.y + wc * c.y);
        }
        return float.IsNegativeInfinity(bestY) ? fallbackY : bestY;
    }

    // ── staging → active 提交（双缓冲交换 + 材质复用）────────

    private void CommitChunkStaging(ChunkRenderData chunk, ChunkStagingGeometry staging)
    {
        if (!ValidateChunkStaging(staging, out string validationError))
        {
            Debug.LogError($"[ChunkMapRenderer] 拒绝提交无效几何 {staging.Index}: {validationError}");
            return;
        }

        // 【程序化山脉-阶段 5.6】提交前记录本 Chunk 是否残留动画状态（提交后统一清理，
        // 防旧 Finalize/进度驱动把过期动画数据带到新 mesh）。
        bool hadAnimation = chunk.AnimUV2Cache != null || chunk.AnimBaseVerts != null
            || chunk.StableTerrainMaterials != null || chunk.AnimationReturnsToStart;

        // Terrain
        if (staging.Terrain != null && staging.Terrain.Vertices != null && staging.Terrain.Vertices.Length > 0)
        {
            FillMeshData(chunk.StagingTerrain, staging.Terrain);
            chunk.TerrainFilter.sharedMesh = chunk.StagingTerrain;
            chunk.TerrainRenderer.sharedMaterials = ResolveTerrainMaterials(staging.Terrain);
            // 【程序化山脉-阶段 3.2】碰撞分离（决策 ㉚）：山体 Chunk 使用共享顶点 +
            // CollisionIndices 的独立碰撞网格；无山 Chunk 回落渲染 mesh（零额外内存，行为不变）。
            if (staging.Terrain.CollisionIndices != null)
            {
                if (chunk.StagingCollision == null)
                    chunk.StagingCollision = new Mesh { name = $"ChunkCollisionStaging_{chunk.Index.X}_{chunk.Index.Z}" };
                FillCollisionMeshData(chunk.StagingCollision, staging.Terrain);
                chunk.TerrainCollider.sharedMesh = chunk.StagingCollision;
                // 【程序化山脉-阶段 7.8】collision cooking（提交）次数 = 含山 Chunk 碰撞网格切换次数。
                if (EnableChunkBuildTiming) CollisionCommitCount++;

                Mesh tempCollision = chunk.ActiveCollision;
                chunk.ActiveCollision = chunk.StagingCollision;
                chunk.StagingCollision = tempCollision;
            }
            else
            {
                chunk.TerrainCollider.sharedMesh = chunk.StagingTerrain;
            }
            chunk.TerrainRenderer.enabled = true;
        }
        else
        {
            chunk.TerrainRenderer.enabled = false;
            chunk.TerrainCollider.sharedMesh = null;
        }

        // 【程序化山脉-阶段 5.1】提交后保存山体拓扑签名（供下一次动画准备阶段比较）。
        // 无山/无地形 Chunk 恒为 Empty（提前返回，零分配；签名不包含纯 Y 值）。
        chunk.LastMountainTopology = staging.Terrain != null && staging.Terrain.HasMountain
            ? BuildMountainTopologySignature(staging.Terrain, chunk.Cells)
            : MountainTopologySignature.Empty;

        // Water
        if (staging.Water != null)
        {
            FillWaterMeshData(chunk.StagingWater, staging.Water);
            chunk.WaterHost.GetComponent<MeshFilter>().sharedMesh = chunk.StagingWater;
            chunk.WaterHost.GetComponent<MeshRenderer>().sharedMaterials = _config.lakeOrSeaMaterial;
            chunk.WaterHost.SetActive(true);
        }
        else
        {
            chunk.WaterHost.SetActive(false);
        }

        // River
        if (staging.River != null)
        {
            FillRiverMeshData(chunk.StagingRiver, staging.River);
            chunk.RiverHost.GetComponent<MeshFilter>().sharedMesh = chunk.StagingRiver;
            chunk.RiverHost.GetComponent<MeshRenderer>().sharedMaterials = _config.riverMaterial;
            chunk.RiverHost.SetActive(true);
        }
        else
        {
            chunk.RiverHost.SetActive(false);
        }

        if (_config.showHexGrid && staging.Grid != null)
        {
            Material gridMaterial = ResolveGridMaterial();
            if (gridMaterial != null)
            {
                FillGridMeshData(chunk.StagingGrid, staging.Grid);
                chunk.GridFilter.sharedMesh = chunk.StagingGrid;
                chunk.GridRenderer.sharedMaterial = gridMaterial;
                chunk.GridHost.SetActive(true);
            }
            else
            {
                chunk.GridHost.SetActive(false);
            }
        }
        else
        {
            chunk.GridHost.SetActive(false);
        }

        // 交换双缓冲（旧 active 即 staging——Chunk 后端以 staging 复用为唯一 mesh）
        Mesh temp = chunk.ActiveTerrain;
        chunk.ActiveTerrain = chunk.StagingTerrain;
        chunk.StagingTerrain = temp;
        temp = chunk.ActiveWater;
        chunk.ActiveWater = chunk.StagingWater;
        chunk.StagingWater = temp;
        temp = chunk.ActiveRiver;
        chunk.ActiveRiver = chunk.StagingRiver;
        chunk.StagingRiver = temp;
        temp = chunk.ActiveGrid;
        chunk.ActiveGrid = chunk.StagingGrid;
        chunk.StagingGrid = temp;

        // 【阶段四修订-审查修复】清除动画开始时保存的过期稳定材质：动画进行中被非动画路径
        // （Duration=0 提交/分帧提交）重建的 Chunk，Finalize 不应恢复旧数组（submesh 布局可能
        // 已变）。动画路径中 SwitchToTransitionMaterials 在本方法之后重新保存当前稳定数组，不受影响。
        chunk.StableTerrainMaterials = null;

        // 【程序化山脉-阶段 5.6】同步提交接管 Chunk 所有权：清除过期动画缓存与 clip MPB，
        // 防止旧动画 Finalize/进度驱动把过期 subMesh 布局或 keep-below 平面残留到新 mesh。
        // （正常路径旧动画已被 ForceCompleteConflicting 完成；此处为防御性兜底，幂等。）
        if (hadAnimation)
            ClearAnimationState(chunk);
    }

    /// <summary>
    /// 【程序化山脉-阶段 5.6】清理 Chunk 的动画状态（幂等）：缓存清空、返回值标记复位、
    /// clip MPB 钉死"恒不裁"（稳定 Shader 不读这些参数，仅防止旧动画期间钉死的 keep-below
    /// 平面在后续动画外残留）。TerrainGhost/FadeGhost 由 Finalize 路径销毁，同步提交不创建。
    /// </summary>
    private static void ClearAnimationState(ChunkRenderData chunk)
    {
        chunk.AnimUV2Cache = null;
        chunk.AnimUV3Cache = null;
        chunk.AnimBaseVerts = null;
        chunk.AnimVertexBuffer = null;
        chunk.AnimationReturnsToStart = false;
        if (chunk.TerrainRenderer != null)
        {
            chunk.AnimationBlock.SetFloat(ChunkAnimBaseYId, 1000f);
            chunk.AnimationBlock.SetFloat(ChunkAnimRiseHeightId, 0f);
            chunk.TerrainRenderer.SetPropertyBlock(chunk.AnimationBlock);
        }
    }

    /// <summary>
    /// 【程序化山脉-阶段 5.1】从已构建几何 + 本 Chunk 格集合派生山体拓扑签名（纯函数）。
    /// 有效山格集合用 HasVisibleMountain（与 Height 无关，决策 ⑳ 阈值已并入）；
    /// 山体/碰撞 indices 内容摘要覆盖 halo 驱动的 rect/tri 变化（跨 Chunk 邻居可见性翻转也会改布局）。
    /// </summary>
    private static MountainTopologySignature BuildMountainTopologySignature(
        TerrainGeometry terrain, IReadOnlyList<HexCellData> chunkCells)
    {
        var subMeshCounts = new List<int>(terrain.SubMeshIndices != null ? terrain.SubMeshIndices.Length : 0);
        if (terrain.SubMeshIndices != null)
        {
            foreach (int[] sub in terrain.SubMeshIndices)
                subMeshCounts.Add(sub != null ? sub.Length : 0);
        }

        var visibleOrders = new List<int>();
        if (chunkCells != null)
        {
            foreach (HexCellData cell in chunkCells)
            {
                if (cell != null && MountainGeometryBuilder.HasVisibleMountain(cell))
                    visibleOrders.Add(cell.GenerateOrder);
            }
        }

        return MountainTopologySignature.Build(
            hasMountain: true,
            totalVertexCount: terrain.Vertices != null ? terrain.Vertices.Length : 0,
            mountainIndexCount: terrain.MountainIndices != null ? terrain.MountainIndices.Length : 0,
            subMeshIndexCounts: subMeshCounts,
            mountainIndices: terrain.MountainIndices,
            collisionIndices: terrain.CollisionIndices,
            visibleMountainCellOrders: visibleOrders);
    }

    private static void FillMeshData(Mesh mesh, TerrainGeometry geometry)
    {
        // Drop stale animated UV2/UV3 layout when this buffer is reused by an ordinary build.
        mesh.Clear(false);
        mesh.indexFormat = geometry.Vertices.Length > ushort.MaxValue
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = geometry.Vertices;
        mesh.uv = geometry.UVs;
        // 【阶段四】动画通道（§20-10）：仅在动画构建时写入，普通重建不写（shader 读到 0 → 不参与）
        if (geometry.UV2s != null) mesh.uv2 = geometry.UV2s;
        if (geometry.UV3s != null) mesh.uv3 = geometry.UV3s;
        if (geometry.UV4s != null) mesh.SetUVs(3, new List<Vector4>(geometry.UV4s));
        mesh.subMeshCount = geometry.SubMeshIndices.Length;
        for (int i = 0; i < geometry.SubMeshIndices.Length; i++)
        {
            if (geometry.SubMeshIndices[i] != null && geometry.SubMeshIndices[i].Length > 0)
                mesh.SetTriangles(geometry.SubMeshIndices[i], i);
        }
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        MapController.SanitizeNormalsAndTangents(mesh);
        mesh.RecalculateBounds();
        // 【程序化山脉-阶段 5.7】动画构建预扩保守 bounds（CPU 逐帧写 vertices 不更新 bounds；
        // 覆盖 start→target 全程 + 山峰 + clip 余量，防视锥/阴影剔除峰顶）。普通构建 null 跳过。
        if (geometry.ConservativeBounds.HasValue)
            mesh.bounds = geometry.ConservativeBounds.Value;
    }

    /// <summary>
    /// 【程序化山脉-阶段 3.2】填充独立碰撞网格：共享渲染顶点 + CollisionIndices（单 subMesh，
    /// 无洞基础表面，山体替换面不参与碰撞）。MeshCollider cooking 只消费位置，
    /// 无需法线/切线，跳过 RecalculateNormals/Tangents 省开销。
    /// 动画路径下 staging 顶点已被 ApplyAnimationStartVertices 写为旧高度（UV2.y 存目标），
    /// 碰撞必须一次性切到终态（决策 ㉚：collision 提交时切到基础终态地表）。
    /// </summary>
    private static void FillCollisionMeshData(Mesh mesh, TerrainGeometry geometry)
    {
        Vector3[] vertices = geometry.Vertices;
        if (geometry.UV2s != null && geometry.UV2s.Length == geometry.Vertices.Length)
        {
            vertices = new Vector3[geometry.Vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = geometry.Vertices[i];
                v.y = geometry.UV2s[i].y;
                vertices[i] = v;
            }
        }
        mesh.Clear(false);
        mesh.indexFormat = vertices.Length > ushort.MaxValue
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.SetTriangles(geometry.CollisionIndices, 0);
        mesh.RecalculateBounds();
    }

    private static void FillRiverMeshData(Mesh mesh, RiverGeometry geometry)
    {
        mesh.Clear(false);
        mesh.indexFormat = geometry.Vertices.Length > ushort.MaxValue
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = geometry.Vertices;
        mesh.uv = geometry.UVs;
        mesh.triangles = geometry.Indices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private static void FillWaterMeshData(Mesh mesh, WaterGeometry geometry)
    {
        mesh.Clear(false);
        mesh.indexFormat = geometry.Vertices.Length > ushort.MaxValue
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = geometry.Vertices;
        mesh.uv = geometry.UVs;
        mesh.subMeshCount = geometry.Indices.Length;
        for (int i = 0; i < geometry.Indices.Length; i++)
        {
            if (geometry.Indices[i] != null && geometry.Indices[i].Length > 0)
                mesh.SetTriangles(geometry.Indices[i], i);
        }
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private static void FillGridMeshData(Mesh mesh, GridGeometry geometry)
    {
        mesh.Clear(false);
        mesh.indexFormat = geometry.Vertices.Length > ushort.MaxValue
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = geometry.Vertices;
        mesh.uv = geometry.UVs;
        mesh.triangles = geometry.Indices;
        mesh.RecalculateBounds();
    }

    private Material ResolveGridMaterial()
    {
        Material material = _config.gridMaterial;
        if (material == null)
        {
            if (_generatedGridMaterial == null)
            {
                Shader shader = Shader.Find("Custom/HexGridOverlay");
                if (shader == null)
                {
                    Debug.LogError("[ChunkMapRenderer] Shader Custom/HexGridOverlay not found; grid disabled.");
                    return null;
                }
                _generatedGridMaterial = new Material(shader) { name = "HexGridOverlay_Runtime" };
            }
            material = _generatedGridMaterial;
        }

        Color color = _config.gridColor;
        color.a *= _config.gridAlpha;
        material.SetColor(GridColorId, color);
        return material;
    }

    private static bool ValidateChunkStaging(ChunkStagingGeometry staging, out string error)
    {
        if (staging == null)
        {
            error = "staging=null";
            return false;
        }

        if (!ValidateTerrain(staging.Terrain, out error)) return false;
        if (!ValidateRiver(staging.River, out error)) return false;
        if (!ValidateWater(staging.Water, out error)) return false;
        if (!ValidateGrid(staging.Grid, out error)) return false;
        return true;
    }

    private static bool ValidateTerrain(TerrainGeometry geometry, out string error)
    {
        if (geometry == null)
        {
            error = null;
            return true;
        }
        if (!ValidateVertexChannels(geometry.Vertices, geometry.UVs, "Terrain", out error))
            return false;
        if (geometry.UV2s != null && geometry.UV2s.Length != geometry.Vertices.Length)
        {
            error = $"Terrain UV2={geometry.UV2s.Length}, vertices={geometry.Vertices.Length}";
            return false;
        }
        if (geometry.UV3s != null && geometry.UV3s.Length != geometry.Vertices.Length)
        {
            error = $"Terrain UV3={geometry.UV3s.Length}, vertices={geometry.Vertices.Length}";
            return false;
        }
        if (geometry.UV4s != null && geometry.UV4s.Length != geometry.Vertices.Length)
        {
            error = $"Terrain UV4={geometry.UV4s.Length}, vertices={geometry.Vertices.Length}";
            return false;
        }
        if (geometry.UV4s != null)
        {
            for (int i = 0; i < geometry.UV4s.Length; i++)
            {
                Vector4 uv4 = geometry.UV4s[i];
                if (!IsFinite(uv4.x) || !IsFinite(uv4.y) || !IsFinite(uv4.z) || !IsFinite(uv4.w)
                    || uv4.z < -1e-5f || uv4.z > 1.00001f)
                {
                    error = $"Terrain UV4 融合通道无效（顶点 {i}）";
                    return false;
                }
            }
        }
        // 【程序化山脉-阶段 5.3】动画通道有效性（决策 ㉛）：全部顶点 start/target/delay 有限，delayEnd ≥ delayStart。
        // 山体顶点通道由逐顶点来源集合生成（AppendMountainAnimUV），普通地形通道亦一并校验（无回归风险：
        // solid 用本格 delta、rect/tri 用端点加权混合，均满足 delayEnd ≥ delayStart）。
        if (geometry.UV2s != null && geometry.UV3s != null)
        {
            for (int i = 0; i < geometry.UV2s.Length; i++)
            {
                Vector2 uv2 = geometry.UV2s[i];
                Vector2 uv3 = geometry.UV3s[i];
                if (!IsFinite(uv2.x) || !IsFinite(uv2.y) || !IsFinite(uv3.x) || !IsFinite(uv3.y))
                {
                    error = $"Terrain 动画通道含非有限值（顶点 {i}）";
                    return false;
                }
                if (uv3.y < uv3.x - 1e-6f)
                {
                    error = $"Terrain UV3 delayEnd < delayStart（顶点 {i}）";
                    return false;
                }
            }
        }
        if (geometry.SubMeshIndices == null)
        {
            error = "Terrain submeshes=null";
            return false;
        }
        for (int i = 0; i < geometry.SubMeshIndices.Length; i++)
        {
            if (!ValidateIndices(geometry.SubMeshIndices[i], geometry.Vertices.Length, $"Terrain submesh {i}", out error))
                return false;
        }
        // 【程序化山脉】山体槽/独立碰撞网格索引范围校验；
        // HasMountain 以最终非空 MountainIndices 为准（结构一致，无需额外判断）。
        if (!ValidateIndices(geometry.MountainIndices, geometry.Vertices.Length, "Terrain mountain", out error))
            return false;
        if (!ValidateIndices(geometry.CollisionIndices, geometry.Vertices.Length, "Terrain collision", out error))
            return false;
        // 【程序化山脉-阶段 5.8】碰撞索引不得引用仅山体渲染顶点（决策 ㉛ 回归拦截；
        // 构造上由分槽构建保证，此校验只防未来构建顺序回归导致碰撞面挂在山体顶点上）。
        if (geometry.CollisionIndices != null && geometry.MountainVertexRanges != null)
        {
            for (int i = 0; i < geometry.CollisionIndices.Length; i++)
            {
                int vertex = geometry.CollisionIndices[i];
                for (int r = 0; r + 1 < geometry.MountainVertexRanges.Length; r += 2)
                {
                    int start = geometry.MountainVertexRanges[r];
                    int count = geometry.MountainVertexRanges[r + 1];
                    if (vertex >= start && vertex < start + count)
                    {
                        error = $"Terrain collision index {vertex} 引用了仅山体渲染顶点（区间 {start}..{start + count}）";
                        return false;
                    }
                }
            }
        }
        // 槽布局一致性：山脚融合槽位于 tri 后，主山体槽仅在 HasMainMountainSlot 时存在。
        if (geometry.HasMountain)
        {
            int expectedSlots = 3 + (geometry.RectAs != null ? geometry.RectAs.Length : 0)
                + (geometry.TriAs != null ? geometry.TriAs.Length : 0)
                + (geometry.MountainBoundaryMaterials != null ? geometry.MountainBoundaryMaterials.Length : 0)
                + (geometry.HasMainMountainSlot ? 1 : 0);
            if (geometry.SubMeshIndices.Length != expectedSlots)
            {
                error = $"Terrain 山体槽布局不一致：{geometry.SubMeshIndices.Length} != {expectedSlots}";
                return false;
            }
        }
        return true;
    }

    private static bool ValidateRiver(RiverGeometry geometry, out string error)
    {
        if (geometry == null)
        {
            error = null;
            return true;
        }
        if (!ValidateVertexChannels(geometry.Vertices, geometry.UVs, "River", out error))
            return false;
        return ValidateIndices(geometry.Indices, geometry.Vertices.Length, "River", out error);
    }

    private static bool ValidateWater(WaterGeometry geometry, out string error)
    {
        if (geometry == null)
        {
            error = null;
            return true;
        }
        if (!ValidateVertexChannels(geometry.Vertices, geometry.UVs, "Water", out error))
            return false;
        if (geometry.Indices == null)
        {
            error = "Water submeshes=null";
            return false;
        }
        for (int i = 0; i < geometry.Indices.Length; i++)
        {
            if (!ValidateIndices(geometry.Indices[i], geometry.Vertices.Length, $"Water submesh {i}", out error))
                return false;
        }
        return true;
    }

    private static bool ValidateGrid(GridGeometry geometry, out string error)
    {
        if (geometry == null)
        {
            error = null;
            return true;
        }
        if (!ValidateVertexChannels(geometry.Vertices, geometry.UVs, "Grid", out error))
            return false;
        return ValidateIndices(geometry.Indices, geometry.Vertices.Length, "Grid", out error);
    }

    private static bool ValidateVertexChannels(Vector3[] vertices, Vector2[] uvs, string label, out string error)
    {
        if (vertices == null)
        {
            error = $"{label} vertices=null";
            return false;
        }
        if (uvs == null || uvs.Length != vertices.Length)
        {
            error = $"{label} UV={uvs?.Length ?? -1}, vertices={vertices.Length}";
            return false;
        }
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i];
            if (!IsFinite(v.x) || !IsFinite(v.y) || !IsFinite(v.z))
            {
                error = $"{label} vertex {i} is non-finite: {v}";
                return false;
            }
        }
        error = null;
        return true;
    }

    private static bool ValidateIndices(int[] indices, int vertexCount, string label, out string error)
    {
        if (indices == null)
        {
            error = null;
            return true;
        }
        if (indices.Length % 3 != 0)
        {
            error = $"{label} index count {indices.Length} is not divisible by 3";
            return false;
        }
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] < 0 || indices[i] >= vertexCount)
            {
                error = $"{label} index[{i}]={indices[i]} outside [0,{vertexCount})";
                return false;
            }
        }
        error = null;
        return true;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private Material[] ResolveTerrainMaterials(TerrainGeometry geometry)
    {
        Material[] allMaterials = new Material[geometry.SubMeshIndices.Length];
        float worldTextureScale = 1f / Mathf.Max(0.0001f, 2f * _config.OuterRadius * _config.SolidAreaRatio);
        if (_terrainBaseMaterial0 == null)
        {
            Shader terrainFogShader = Shader.Find("Custom/TerrainBase_Fog") ?? Shader.Find("Standard");
            _terrainBaseMaterial0 = MapController.CreateTerrainFogMaterial(geometry.BaseMaterials[0], terrainFogShader, worldTextureScale);
            _terrainBaseMaterial1 = MapController.CreateTerrainFogMaterial(geometry.BaseMaterials[1], terrainFogShader, worldTextureScale);
            _terrainBaseMaterial2 = MapController.CreateTerrainFogMaterial(geometry.BaseMaterials[2], terrainFogShader, worldTextureScale);
        }
        allMaterials[0] = _terrainBaseMaterial0;
        allMaterials[1] = _terrainBaseMaterial1;
        allMaterials[2] = _terrainBaseMaterial2;

        for (int i = 0; i < geometry.RectAs.Length; i++)
        {
            var key = (geometry.RectAs[i], geometry.RectBs[i]);
            if (!_rectMaterialCache.TryGetValue(key, out Material mat))
            {
                mat = MapController.ConfigureBlendMaterial(
                    key.Item2, key.Item1, _config.blendMask, _config.blendContrast, _config.blendSmooth, worldTextureScale);
                _rectMaterialCache[key] = mat;
            }
            allMaterials[3 + i] = mat;
        }

        var triMask = MapController.GetOrCreateBarycentricMask();
        for (int i = 0; i < geometry.TriAs.Length; i++)
        {
            var key = (geometry.TriAs[i], geometry.TriBs[i], geometry.TriCs[i]);
            if (!_triMaterialCache.TryGetValue(key, out Material mat))
            {
                mat = MapController.ConfigureBlendMaterial(
                    key.Item1, key.Item2, key.Item3, triMask, _config.blendContrast, _config.globalSmoothness, worldTextureScale);
                _triMaterialCache[key] = mat;
            }
            allMaterials[3 + geometry.RectAs.Length + i] = mat;
        }
        Material[] boundaryMaterials = geometry.MountainBoundaryMaterials ?? System.Array.Empty<Material>();
        int boundaryOffset = 3 + geometry.RectAs.Length + geometry.TriAs.Length;
        for (int i = 0; i < boundaryMaterials.Length; i++)
        {
            Material terrain = boundaryMaterials[i];
            if (!_mountainBoundaryMaterialCache.TryGetValue(terrain, out Material mat))
            {
                mat = CreateMountainBoundaryMaterial(terrain);
                _mountainBoundaryMaterialCache[terrain] = mat;
            }
            ApplyMountainMaterialConfig(mat, _config != null ? _config.mountainConfig : null);
            mat.EnableKeyword("_MOUNTAIN_TERRAIN_BLEND");
            allMaterials[boundaryOffset + i] = mat;
        }
        // 【程序化山脉-阶段 4.2】山体槽专属稳定材质（Custom/MountainLowPoly_Fog；查找失败回落
        // _terrainBaseMaterial0，只记录一次错误；Transition 变体属阶段 5，动画路径继续回落稳定材质）。
        if (geometry.HasMainMountainSlot)
            allMaterials[boundaryOffset + boundaryMaterials.Length] = GetOrCreateMountainMaterial();
        return allMaterials;
    }

    private Material CreateMountainBoundaryMaterial(Material terrainMaterial)
    {
        Material material = new Material(GetOrCreateMountainMaterial());
        material.name = $"MountainTerrainBlend_{(terrainMaterial != null ? terrainMaterial.name : "Null")}";
        material.EnableKeyword("_MOUNTAIN_TERRAIN_BLEND");
        material.SetTexture("_TerrainTex", terrainMaterial != null && terrainMaterial.mainTexture != null
            ? terrainMaterial.mainTexture : Texture2D.whiteTexture);
        material.SetTextureScale("_TerrainTex", terrainMaterial != null ? terrainMaterial.mainTextureScale : Vector2.one);
        material.SetTextureOffset("_TerrainTex", terrainMaterial != null ? terrainMaterial.mainTextureOffset : Vector2.zero);
        material.SetTexture("_TerrainNormal", terrainMaterial != null && terrainMaterial.HasProperty("_BumpMap")
            ? terrainMaterial.GetTexture("_BumpMap") : Texture2D.normalTexture);
        material.SetColor("_TerrainColor", terrainMaterial != null && terrainMaterial.HasProperty("_Color")
            ? terrainMaterial.GetColor("_Color") : Color.white);
        material.SetFloat("_TerrainSmoothness", terrainMaterial != null && terrainMaterial.HasProperty("_Glossiness")
            ? terrainMaterial.GetFloat("_Glossiness") : 0.15f);
        return material;
    }

    /// <summary>
    /// 【程序化山脉-阶段 4.2】懒创建山体稳定材质：优先克隆 MountainConfigSO.stableMaterial 资产
    /// （属性全部继承），否则 Shader.Find 专属 Shader 后按配置推参数。Shader 查找只尝试一次；
    /// 失败时回落阶段 3 的 _terrainBaseMaterial0 并只记录一次错误，禁止每 Chunk/每帧重复
    /// Shader.Find 或刷日志。实例由本 Renderer 独占，OnDestroy 显式销毁。
    /// </summary>
    private Material GetOrCreateMountainMaterial()
    {
        MountainConfigSO mountainConfig = _config != null ? _config.mountainConfig : null;
        Material source = mountainConfig != null ? mountainConfig.stableMaterial : null;
        if (_mountainMaterial != null)
        {
            ApplyMountainMaterialConfig(_mountainMaterial, mountainConfig);
            return _mountainMaterial;
        }

        if (!_mountainShaderLookupAttempted)
        {
            _mountainShaderLookupAttempted = true;
            _mountainShader = source != null ? source.shader : Shader.Find(MountainMaterialContract.StableShaderName);
            if (_mountainShader == null)
                Debug.LogError($"[ChunkMapRenderer] 找不到山体稳定 Shader {MountainMaterialContract.StableShaderName}" +
                               "（且 MountainConfigSO.stableMaterial 未配置），山体槽回落 _terrainBaseMaterial0（阶段 3 临时材质）。");
        }
        if (_mountainShader == null)
            return _terrainBaseMaterial0;

        _mountainMaterial = source != null ? new Material(source) : new Material(_mountainShader);
        ApplyMountainMaterialConfig(_mountainMaterial, mountainConfig);
        return _mountainMaterial;
    }

    /// <summary>
    /// 同步 MountainConfigSO 到运行时材质实例。配置可能在材质实例创建后才被 Inspector 修改，
    /// 因此不能只在首次 Instantiate 时读取一次 Rock Texture 和关键字。
    /// </summary>
    private void ApplyMountainMaterialConfig(Material material, MountainConfigSO mountainConfig)
    {
        if (material == null || mountainConfig == null)
            return;

        material.SetColor("_ColorLow", mountainConfig.tierColorLow);
        material.SetColor("_ColorMid", mountainConfig.tierColorMid);
        material.SetColor("_ColorHigh", mountainConfig.tierColorHigh);
        material.SetTexture("_RockTexture", mountainConfig.rockTexture);
        material.SetFloat("_RockTextureEnabled", mountainConfig.rockTexture != null ? 1f : 0f);
        // 【Excel 数值化】Triplanar/粗糙度/金属度/阴影走山体配置表（资源引用与色阶仍在 SO）。
        material.SetFloat("_TriplanarWorldScale", MountainConfigProvider.TriplanarWorldScale);
        material.SetFloat("_TriplanarBlendSharpness", MountainConfigProvider.TriplanarBlendSharpness);
        material.SetFloat("_Roughness", MountainConfigProvider.Roughness);
        material.SetFloat("_Metallic", MountainConfigProvider.Metallic);
        material.SetFloat("_ShadowStrength", MountainConfigProvider.ShadowStrength);

        bool textureEnabled = mountainConfig.rockTexture != null;
        if (textureEnabled)
            material.EnableKeyword("_ROCK_TEXTURE");
        else
            material.DisableKeyword("_ROCK_TEXTURE");

    }

    /// <summary>
    /// 【程序化山脉-阶段 5.4】懒创建山体 Transition 材质（动画期间 keep-below clip）：
    /// 属性从稳定山体材质克隆 + 替换为山体 Transition Shader（Triplanar/色阶/法线/雾化契约完整保留，
    /// 与 TerrainBase_Fog_Transition 同模式）。每 Renderer 只缓存一份；Shader 查找只尝试一次，
    /// 缺失时回落稳定山体材质并只报一次错误，绝不回落普通 Terrain shader。
    /// 稳定材质实例尚未创建时先走 GetOrCreateMountainMaterial（保证克隆源齐备）。
    /// </summary>
    private Material GetOrCreateMountainTransitionMaterial(Material stable)
    {
        if (_mountainTransitionMaterial != null)
        {
            ApplyMountainMaterialConfig(_mountainTransitionMaterial,
                _config != null ? _config.mountainConfig : null);
            return _mountainTransitionMaterial;
        }
        if (stable == null)
            stable = GetOrCreateMountainMaterial();
        if (!_mountainTransitionShaderLookupAttempted)
        {
            _mountainTransitionShaderLookupAttempted = true;
            _mountainTransitionShader = Shader.Find(MountainMaterialContract.TransitionShaderName);
            if (_mountainTransitionShader == null)
                Debug.LogError($"[ChunkMapRenderer] 找不到山体 Transition Shader {MountainMaterialContract.TransitionShaderName}" +
                               "，山体槽回落稳定山体材质（动画期间无 keep-below clip，山体槽不参与顶出）。");
        }
        if (_mountainTransitionShader == null)
            return stable;

        _mountainTransitionMaterial = MakeTransitionMaterial(stable, _mountainTransitionShader);
        if (_mountainTransitionMaterial != null)
        {
            // 换 Shader 后同步完整表现参数与 Triplanar 关键字，保证动画期间外观与稳定态一致。
            ApplyMountainMaterialConfig(_mountainTransitionMaterial,
                _config != null ? _config.mountainConfig : null);
        }
        return _mountainTransitionMaterial ?? stable;
    }

    /// <summary>
    /// 【阶段四修订】按稳定材质数组构建一一对应的 *_Transition 变体材质（§十九-21：动画 Shader
    /// 独立命名、独立材质）。以稳定材质为源 `new Material(source)` 拷贝全部纹理/属性后替换 Shader，
    /// 因此动画期间外观与稳定渲染完全一致，仅多出 _ChunkProgress/UV2/UV3 顶点动画能力。
    /// 找不到 Transition Shader 时原样返回稳定材质（降级：动画期间无顶点变形，但渲染不受损）。
    /// </summary>
    private Material[] ResolveTransitionMaterials(TerrainGeometry geometry, Material[] stableMaterials)
    {
        if (geometry == null || stableMaterials == null || stableMaterials.Length == 0)
            return stableMaterials;

        // 【阶段四修订-审查修复】惰性缓存 Transition Shader 引用（每种仅 Find 一次；null 时
        // 降级返回稳定材质，动画期间无顶点变形但渲染不受损）
        if (_transitionBaseShader == null)
            _transitionBaseShader = Shader.Find("Custom/TerrainBase_Fog_Transition");
        if (_transitionRectShader == null)
            _transitionRectShader = Shader.Find("Custom/RealMaterialMaskBlend_Transition");
        if (_transitionTriShader == null)
            _transitionTriShader = Shader.Find("Custom/ThreeMaterialBlend_Land_Transition");

        // 【阶段四修订-审查修复】布局一致性校验：稳定数组 = 3 基础 + rect 组合 + tri 组合
        // + (HasMountain ? 1 山体槽)。若与稳定路径 ResolveTerrainMaterials 的槽布局漂移
        // （未来新增 submesh 槽/改组合顺序），降级返回稳定材质并报错，避免 Transition
        // 材质落错槽或 result 出现 null 混入 sharedMaterials。
        int rectCount = geometry.RectAs != null ? geometry.RectAs.Length : 0;
        int triCount = geometry.TriAs != null ? geometry.TriAs.Length : 0;
        int boundaryCount = geometry.MountainBoundaryMaterials != null ? geometry.MountainBoundaryMaterials.Length : 0;
        int expectedSlots = 3 + rectCount + triCount + boundaryCount + (geometry.HasMainMountainSlot ? 1 : 0);
        if (stableMaterials.Length != expectedSlots)
        {
            Debug.LogError($"[ChunkMapRenderer] Transition 材质布局漂移：stable={stableMaterials.Length}, " +
                           $"rect={rectCount}, tri={triCount}, boundary={boundaryCount}, mountain={(geometry.HasMainMountainSlot ? 1 : 0)}，降级为稳定材质（动画期间无顶点变形）。");
            return stableMaterials;
        }

        // 基础三材质（稳定数组前 3 项 = _terrainBaseMaterial0/1/2）
        if (_terrainBaseMaterial0Transition == null)
        {
            if (_transitionBaseShader != null && stableMaterials.Length >= 3)
            {
                _terrainBaseMaterial0Transition = MakeTransitionMaterial(stableMaterials[0], _transitionBaseShader);
                _terrainBaseMaterial1Transition = MakeTransitionMaterial(stableMaterials[1], _transitionBaseShader);
                _terrainBaseMaterial2Transition = MakeTransitionMaterial(stableMaterials[2], _transitionBaseShader);
            }
        }

        Material[] result = new Material[stableMaterials.Length];
        result[0] = _terrainBaseMaterial0Transition ?? stableMaterials[0];
        result[1] = _terrainBaseMaterial1Transition ?? stableMaterials[1];
        result[2] = _terrainBaseMaterial2Transition ?? stableMaterials[2];

        for (int i = 0; i < rectCount; i++)
        {
            var key = (geometry.RectAs[i], geometry.RectBs[i]);
            if (!_rectTransitionCache.TryGetValue(key, out Material mat))
            {
                mat = _transitionRectShader != null
                    ? MakeTransitionMaterial(stableMaterials[3 + i], _transitionRectShader)
                    : stableMaterials[3 + i];
                _rectTransitionCache[key] = mat;
            }
            result[3 + i] = mat;
        }

        for (int i = 0; i < triCount; i++)
        {
            var key = (geometry.TriAs[i], geometry.TriBs[i], geometry.TriCs[i]);
            if (!_triTransitionCache.TryGetValue(key, out Material mat))
            {
                mat = _transitionTriShader != null
                    ? MakeTransitionMaterial(stableMaterials[3 + rectCount + i], _transitionTriShader)
                    : stableMaterials[3 + rectCount + i];
                _triTransitionCache[key] = mat;
            }
            result[3 + rectCount + i] = mat;
        }

        int boundaryOffset = 3 + rectCount + triCount;
        if (boundaryCount > 0 && !_mountainTransitionShaderLookupAttempted)
            GetOrCreateMountainTransitionMaterial(GetOrCreateMountainMaterial());
        for (int i = 0; i < boundaryCount; i++)
        {
            Material terrain = geometry.MountainBoundaryMaterials[i];
            if (!_mountainBoundaryTransitionCache.TryGetValue(terrain, out Material mat))
            {
                Material stableBoundary = stableMaterials[boundaryOffset + i];
                mat = _mountainTransitionShader != null
                    ? MakeTransitionMaterial(stableBoundary, _mountainTransitionShader)
                    : stableBoundary;
                if (mat != null) mat.EnableKeyword("_MOUNTAIN_TERRAIN_BLEND");
                _mountainBoundaryTransitionCache[terrain] = mat;
            }
            ApplyMountainMaterialConfig(mat, _config != null ? _config.mountainConfig : null);
            if (mat != null) mat.EnableKeyword("_MOUNTAIN_TERRAIN_BLEND");
            result[boundaryOffset + i] = mat;
        }

        // 【程序化山脉-阶段 5.4】山体槽改用山体 Transition 变体（keep-below clip 与 surf/ShadowCaster
        // 同契约；动画期间外观 = 稳定版 Triplanar/色阶/法线/雾化）。Shader 缺失时回落稳定山体材质
        // 并只报一次（GetOrCreateMountainTransitionMaterial），绝不回落普通 Terrain shader。
        if (geometry.HasMainMountainSlot)
            result[boundaryOffset + boundaryCount] = GetOrCreateMountainTransitionMaterial(
                stableMaterials[boundaryOffset + boundaryCount]);

        return result;
    }

    /// <summary>拷贝稳定材质全部属性，仅替换为 Transition Shader（§19-21：独立材质，不共享实例）。</summary>
    private static Material MakeTransitionMaterial(Material source, Shader transitionShader)
    {
        if (source == null || transitionShader == null) return source;
        var material = new Material(source);
        material.shader = transitionShader;
        material.name = $"{source.name}_Transition";
        return material;
    }

    /// <summary>显式销毁本 Renderer 独占创建的材质（§6-2：禁止依赖"引擎自动回收"）。</summary>
    private void OnDestroy()
    {
        // 【程序化山脉-阶段 3.7】独立碰撞双缓冲 mesh 由 Renderer 独占持有，显式销毁
        foreach (ChunkRenderData chunk in _chunks.Values)
        {
            if (chunk.ActiveCollision != null) Object.Destroy(chunk.ActiveCollision);
            if (chunk.StagingCollision != null) Object.Destroy(chunk.StagingCollision);
            if (chunk.ActiveGrid != null) Object.Destroy(chunk.ActiveGrid);
            if (chunk.StagingGrid != null) Object.Destroy(chunk.StagingGrid);
        }

        DestroyMaterialIfNotNull(_terrainBaseMaterial0);
        DestroyMaterialIfNotNull(_terrainBaseMaterial1);
        DestroyMaterialIfNotNull(_terrainBaseMaterial2);
        // 【程序化山脉-阶段 4.2】山体稳定材质为本 Renderer 独占创建的实例，一并显式销毁
        DestroyMaterialIfNotNull(_mountainMaterial);
        // 【程序化山脉-阶段 5.4】山体 Transition 材质同属本 Renderer 独占创建，一并显式销毁
        DestroyMaterialIfNotNull(_mountainTransitionMaterial);
        DestroyMaterialIfNotNull(_generatedGridMaterial);
        foreach (Material mat in _rectMaterialCache.Values)
            DestroyMaterialIfNotNull(mat);
        foreach (Material mat in _triMaterialCache.Values)
            DestroyMaterialIfNotNull(mat);
        foreach (Material mat in _mountainBoundaryMaterialCache.Values)
            DestroyMaterialIfNotNull(mat);

        // 阶段四修订：Transition 变体材质同属本 Renderer 独占创建，一并显式销毁
        DestroyMaterialIfNotNull(_terrainBaseMaterial0Transition);
        DestroyMaterialIfNotNull(_terrainBaseMaterial1Transition);
        DestroyMaterialIfNotNull(_terrainBaseMaterial2Transition);
        foreach (Material mat in _rectTransitionCache.Values)
            DestroyMaterialIfNotNull(mat);
        foreach (Material mat in _triTransitionCache.Values)
            DestroyMaterialIfNotNull(mat);
        foreach (Material mat in _mountainBoundaryTransitionCache.Values)
            if (!_mountainBoundaryMaterialCache.ContainsValue(mat)) DestroyMaterialIfNotNull(mat);
        _rectTransitionCache.Clear();
        _triTransitionCache.Clear();
        _mountainBoundaryMaterialCache.Clear();
        _mountainBoundaryTransitionCache.Clear();
    }

    private static void DestroyMaterialIfNotNull(Material material)
    {
        if (material != null) Object.Destroy(material);
    }

    // ── 构建辅助 ──────────────────────────────────────────────

    private CellBuildContext MakeBuildContext(HexCellData hexCellData)
    {
        _solidVertices.TryGetValue(hexCellData.GenerateOrder, out Vector3[] solid);
        return new CellBuildContext
        {
            Cell = hexCellData,
            View = _view,
            Solid = solid,
            Solids = _solidVertices,
            LakeOrSeas = _lakeOrSeaVertices,
            RectVertices = _rectVerticesByCell,
            InterpCount = hexCellData.interpCount
        };
    }

    private int MainMeshSolidAreaDrawOrderFunction(HexCellData hexCellData, out List<Enums.HexDirection> direction)
    {
        int drawOrder;
        direction = new List<Enums.HexDirection>();
        if (hexCellData.HexType == Enums.HexType.RiverSource)
        {
            drawOrder = 2;
            direction.Add(hexCellData.RiverOutgoingDirection);
        }
        else if (hexCellData.HexType == Enums.HexType.RiverMidstream)
        {
            drawOrder = 3;
            direction.Add(hexCellData.RiverIncomingDirection);
            direction.Add(hexCellData.RiverOutgoingDirection);
        }
        else if (hexCellData.HexType == Enums.HexType.RiverEnd)
        {
            drawOrder = 2;
            direction.Add(hexCellData.RiverIncomingDirection);
        }
        else
        {
            drawOrder = 1;
        }
        return drawOrder;
    }

    private void MainMeshRectFunction(HexCellData hexCellData, Enums.HexDirection direction, out bool isSlope, out bool isRiver)
    {
        isRiver = false;
        isSlope = true;
        if (_mapDataService.GetNeighbor(hexCellData, direction) == null) return;

        if ((hexCellData.RiverIncomingDirection == direction || hexCellData.RiverOutgoingDirection == direction) &&
            (hexCellData.hasRiver && _mapDataService.GetNeighbor(hexCellData, direction).hasRiver))
        {
            isRiver = true;
        }

        Enums.RectType[] rectTypes;
        Enums.TriType[] triTypes;
        TerrainGenerator.IsType(hexCellData, out rectTypes, out triTypes, _mapDataService);
        if ((int)direction >= rectTypes.Length) return;
        switch (rectTypes[(int)direction])
        {
            case Enums.RectType.slope:
                isSlope = true;
                break;
            case Enums.RectType.step:
                isSlope = false;
                break;
        }
    }

    private TriangleTransitionMeshData GetGenericTriangleMesh(
        CellBuildContext ctx,
        Enums.HexDirection directionA,
        Enums.HexDirection directionB)
    {
        HexCellData hexCellData = ctx.Cell;
        if (directionA == Enums.HexDirection.NE && directionB == Enums.HexDirection.E)
        {
            HexCellData neighborNE = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE);
            var raw = RectangleDrivenTriangleMesh.BuildNEE(
                GetRectangleMesh(hexCellData, Enums.HexDirection.NE),
                GetRectangleMesh(neighborNE, Enums.HexDirection.SE),
                GetRectangleMesh(hexCellData, Enums.HexDirection.E));
            return TriFlat(_config.shadingStyle) ? TriangleTransitionMesh.ToFlatShaded(raw) : raw;
        }
        if (directionA == Enums.HexDirection.E && directionB == Enums.HexDirection.SE)
        {
            HexCellData neighborSE = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE);
            var raw = RectangleDrivenTriangleMesh.BuildESE(
                GetRectangleMesh(hexCellData, Enums.HexDirection.E),
                GetRectangleMesh(neighborSE, Enums.HexDirection.NE),
                GetRectangleMesh(hexCellData, Enums.HexDirection.SE));
            return TriFlat(_config.shadingStyle) ? TriangleTransitionMesh.ToFlatShaded(raw) : raw;
        }
        throw new System.ArgumentException("Unsupported triangle directions.");
    }

    private RectangleTransitionMeshData GetRectangleMesh(HexCellData owner, Enums.HexDirection direction)
    {
        if (!_genericRectangleMeshes.TryGetValue((owner.GenerateOrder, direction), out RectangleTransitionMeshData rectangle))
        {
            throw new System.InvalidOperationException(
                $"ChunkMapRenderer: Triangle transition requires rectangle profile {owner.GenerateOrder}:{direction}.");
        }
        return rectangle;
    }

    private RectangleTransitionMeshData GetGenericRectangleMesh(CellBuildContext ctx, Enums.HexDirection direction)
    {
        HexCellData hexCellData = ctx.Cell;
        HexCellData neighbor = _mapDataService.GetNeighbor(hexCellData, direction);
        Vector3[] solid = ctx.Solid;
        Vector3[] neighborSolid = ctx.GetNeighborSolid(direction);
        if (hexCellData == null || neighbor == null || solid == null || neighborSolid == null)
        {
            string owner = hexCellData != null ? hexCellData.GenerateOrder.ToString() : "null";
            string neighborOrder = neighbor != null ? neighbor.GenerateOrder.ToString() : "null";
            throw new System.InvalidOperationException(
                $"ChunkMapRenderer rectangle profile dependency missing: owner={owner}, direction={direction}, " +
                $"neighbor={neighborOrder}, ownerSolid={solid != null}, neighborSolid={neighborSolid != null}.");
        }
        var starts = new List<Vector3>(4);
        var ends = new List<Vector3>(4);

        switch (direction)
        {
            case Enums.HexDirection.NE:
                starts.Add(solid[1]); starts.Add(solid[7]); starts.Add(solid[8]); starts.Add(solid[2]);
                ends.Add(neighborSolid[5]); ends.Add(neighborSolid[14]); ends.Add(neighborSolid[13]); ends.Add(neighborSolid[4]);
                break;
            case Enums.HexDirection.E:
                starts.Add(solid[2]); starts.Add(solid[9]); starts.Add(solid[10]); starts.Add(solid[3]);
                ends.Add(neighborSolid[6]); ends.Add(neighborSolid[16]); ends.Add(neighborSolid[15]); ends.Add(neighborSolid[5]);
                break;
            case Enums.HexDirection.SE:
                starts.Add(solid[3]); starts.Add(solid[11]); starts.Add(solid[12]); starts.Add(solid[4]);
                ends.Add(neighborSolid[1]); ends.Add(neighborSolid[18]); ends.Add(neighborSolid[17]); ends.Add(neighborSolid[6]);
                break;
            default:
                throw new System.ArgumentException("Unsupported generic rectangle direction.");
        }

        Enums.TransitionEdgeType type;
        int subdivision;
        // 【2026-08-10】含山边强制直坡：山体 rect（山-山整面 / 山-普通格界劈半）恒为直斜面，
        // 若通用 rect 在 ΔH≥2 时用阶梯 profile，交界 tri 继承阶梯角点序列会与山体 rect 的直
        // profile 错开成三角裂缝（低角度透视可见）；普通-普通边维持阶梯不变。
        bool edgeMountain = MountainGeometryBuilder.HasVisibleMountain(hexCellData)
            || MountainGeometryBuilder.HasVisibleMountain(neighbor);
        if (_config.useHeightBasedSubdivision && !edgeMountain)
        {
            bool sameHeight = Mathf.Approximately(hexCellData.Height, neighbor.Height);
            type = sameHeight ? Enums.TransitionEdgeType.Slope : Enums.TransitionEdgeType.Step;
            subdivision = GetSubdivision(hexCellData.Height, neighbor.Height);
        }
        else
        {
            type = Enums.TransitionEdgeType.Slope;
            subdivision = 0;
        }
        bool perturbIntermediate =
            type == Enums.TransitionEdgeType.Step &&
            !Mathf.Approximately(hexCellData.Height, neighbor.Height);
        RectangleTransitionMeshData rectangle = RectangleTransitionMesh.Build(
            starts, ends, type, subdivision, perturbIntermediate);
        _genericRectangleMeshes[(hexCellData.GenerateOrder, direction)] = rectangle;
        return rectangle;
    }

    /// <summary>
    /// 【程序化山脉-阶段 3.6】山体 rect（缓存；与普通 rect 同键）。山格可能是 owner 也可能是
    /// neighbor，两侧高度统一走规范化角点/边点函数（翻转对称，跨 Chunk 一致）。
    /// </summary>
    private MountainRectBuild GetMountainRectangleMesh(CellBuildContext ctx, Enums.HexDirection direction)
    {
        var key = (ctx.Cell.GenerateOrder, direction);
        if (_mountainRectangleMeshes.TryGetValue(key, out MountainRectBuild cached))
            return cached;

        HexCellData neighbor = _mapDataService.GetNeighbor(ctx.Cell, direction);
        Vector3[] neighborSolid = ctx.GetNeighborSolid(direction);
        if (neighbor == null || ctx.Solid == null || neighborSolid == null)
        {
            throw new System.InvalidOperationException(
                $"ChunkMapRenderer mountain rectangle dependency missing: owner={ctx.Cell.GenerateOrder}, direction={direction}.");
        }
        MountainRectBuild build = MountainGeometryBuilder.BuildMountainRectData(
            ctx.Cell, neighbor, ctx.Solid, neighborSolid, direction,
            (c, d) => _mapDataService.GetNeighbor(c, d));
        _mountainRectangleMeshes[key] = build;
        return build;
    }

    private int GetSubdivision(float heightA, float heightB)
    {
        int levels = Mathf.RoundToInt(Mathf.Abs(heightA - heightB) / _config.stepHeight);
        return Mathf.Clamp(levels - 1, 0, _config.maxStepSubdivision);
    }

    private List<int> RiverMeshSolidAreaDrawOrderFunction(HexCellData hexCellData)
    {
        switch (hexCellData.HexType)
        {
            case Enums.HexType.RiverSource:
                return _meshGenerator.BuildRiverWater2DrawOrder(hexCellData.RiverOutgoingDirection);
            case Enums.HexType.RiverMidstream:
                return _meshGenerator.BuildRiverWater3DrawOrder(hexCellData);
            case Enums.HexType.RiverEnd:
                return _meshGenerator.BuildRiverWater2DrawOrder(hexCellData.RiverIncomingDirection);
            default:
                return null;
        }
    }

    private int[] RiverMeshDownstreamDrawOrderFunction() => _meshGenerator.BuildOutgoingRiverSlopDrawOrder();
    private int[] LakeOrSeaMeshSolidAreaDrawOrderFunction() => _meshGenerator.BuildLakeOrSeaDrawOrder();
    private List<int> LakeOrSeaMeshRectDrawOrderFunction(Enums.HexDirection direction) => _meshGenerator.BuildLakeOrSeaRectDrawOrder(direction);
    private List<int> LakeOrSeaMeshTriDrawOrderFunction(Enums.HexDirection a, Enums.HexDirection b) => _meshGenerator.BuildLakeOrSeaTriDrawOrder(a, b);
    private List<int> CoastMeshRectDrawOrderFunction(Vector3[] v) => _meshGenerator.BuildCoastRectDrawOrder(v);
    private List<int> CoastMeshTriDrawOrderFunction(Vector3[] v) => _meshGenerator.BuildCoastTriDrawOrder(v);

    private void MainMeshDrawOrderElementAddRule(HexCellData hexCellData, List<int> drawOrder, ref List<List<int>> subList, int IndexOffset)
    {
        int bucket = WaterLevelConfig.ClassifyHeight(hexCellData.Height);
        List<int> target = bucket switch
        {
            0 => subList[0],
            1 => subList[1],
            _ => subList[2]
        };
        foreach (int i in drawOrder) target.Add(i + IndexOffset);
    }

    private void OtherMeshDrawOrderElementAddRule(HexCellData hexCellData, List<int> drawOrder, ref List<int> ints, int IndexOffset)
    {
        foreach (int i in drawOrder) ints.Add(i + IndexOffset);
    }

    private static bool RectFlat(Enums.ShadingStyle style)
    {
        return style == Enums.ShadingStyle.FlatAll || style == Enums.ShadingStyle.FlatRect_SmoothTri;
    }

    private static bool TriFlat(Enums.ShadingStyle style)
    {
        return style == Enums.ShadingStyle.FlatAll || style == Enums.ShadingStyle.SmoothRect_FlatTri;
    }

    // ── RefreshCellObjects：变化格对象刷新 ───────────────────
    public void RefreshCellObjects(IReadOnlyCollection<HexCellData> changedCells, RemovedVisualHandle removed,
        bool snapToFinalPosition = true)
    {
        if (changedCells == null) return;

        // Chunk 后端：网格线重建 → 统一在"变化格所属 Chunk"重建路径（Geometry 随 Chunk 重建）。
        // 此处只处理地貌/资源模型移除与归位。
        foreach (HexCellData cell in changedCells)
        {
            if (cell == null) continue;

            if ((cell.landForm == null || cell.landForm.modelPrefab == null) && cell.landFormModel != null)
            {
                removed?.Add(cell.landFormModel);
                cell.landFormModel = null;
            }
            if (cell.resource == null && cell.resourceModel != null)
            {
                removed?.Add(cell.resourceModel);
                cell.resourceModel = null;
            }

            if (snapToFinalPosition && cell.landFormModel != null)
                cell.landFormModel.transform.position = cell.RealCenterWorldCoordinate;
            if (snapToFinalPosition && cell.resourceModel != null)
                cell.resourceModel.transform.position = cell.RealCenterWorldCoordinate;
        }
    }

    private static void ApplyAnimationStartVertices(ChunkStagingGeometry staging)
    {
        TerrainGeometry terrain = staging?.Terrain;
        if (terrain?.Vertices == null || terrain.UV2s == null ||
            terrain.Vertices.Length != terrain.UV2s.Length)
            return;

        for (int i = 0; i < terrain.Vertices.Length; i++)
        {
            Vector3 vertex = terrain.Vertices[i];
            vertex.y = terrain.UV2s[i].x;
            terrain.Vertices[i] = vertex;
        }
    }

    /// <summary>
    /// Wave 是纯高度脉冲且不改拓扑。目标 staging 与当前稳定 mesh 顶点数一致时，直接用当前
    /// mesh 的逐顶点 Y 覆盖 UV2.x，作为权威动画起点。这样不依赖 Height 差值反推，可避免
    /// UV2.x 错写为目标高度导致波后仍整体 +4。拓扑不一致时保留原推导值，供一般动画使用。
    /// </summary>
    private static void BindWaveStartVerticesFromActiveMesh(ChunkRenderData chunk, ChunkStagingGeometry staging)
    {
        if (chunk == null || staging == null || !staging.AnimationReturnsToStart) return;
        TerrainGeometry terrain = staging.Terrain;
        Mesh activeMesh = chunk.TerrainFilter != null ? chunk.TerrainFilter.sharedMesh : null;
        if (terrain?.UV2s == null || activeMesh == null ||
            activeMesh.vertexCount != terrain.UV2s.Length)
            return;

        Vector3[] oldVertices = activeMesh.vertices;
        if (oldVertices.Length != terrain.UV2s.Length) return;
        for (int i = 0; i < oldVertices.Length; i++)
            terrain.UV2s[i].x = oldVertices[i].y;
    }

    // ── 迷雾（全局贴图由 Bootstrap 创建，内容由 Chunk 后端维护）──────

    public void ForceRefreshFogVisuals(IReadOnlyCollection<HexCellData> snapCells = null)
    {
        if (!_fogInitialized) return;
        UpdateFogTransitionTargets(snapCells);
        RebuildFogMask();
        _fogTransition.ClearDirty();
        _fogRefreshTimer = 0f;
    }

    private void UpdateFogTransitionTargets(IReadOnlyCollection<HexCellData> snapCells = null)
    {
        const int PlayerViewerFactionId = 0;
        foreach (HexCellData cell in _mapDataService.GetAllCells())
        {
            if (cell == null) continue;
            bool isVisible;
            if (_visibilityResolver != null)
                isVisible = _visibilityResolver.IsVisibleToFaction(cell, PlayerViewerFactionId);
            else if (_logisticsService != null)
                isVisible = _logisticsService.IsVisibleToFaction(cell, PlayerViewerFactionId);
            else
                isVisible = cell.IsExplored;

            float targetAlpha = isVisible ? 1f : 0f;
            // 【实机修订-2026-08-04】snapCells 指定格瞬间 Snap（突起帧 37 格立即点亮）
            if (_fogInitialized && (snapCells == null || !snapCells.Contains(cell)))
                _fogTransition.RequestTransition(cell, targetAlpha);
            else
                _fogTransition.SnapTransition(cell, targetAlpha);
        }

        if (!_fogInitialized)
        {
            _fogInitialized = true;
            RebuildFogMask();
            _fogTransition.ClearDirty();
        }
    }

    /// <summary>
    /// 【迷雾修复-2026-08-04】重建全局探索遮罩贴图（_FogMaskTex）。
    /// 迷雾最终视觉（FogBlend_final）只采样 _FogMaskTex 判定已探索/未探索（顶点色 R 不参与混合），
    /// 遮罩贴图由 MapPresentationBootstrap 创建并绑定全局，Chunk 后端负责按 FogAlpha 自行盖章重建。
    /// 贴图尺寸与包围盒直接读取全局属性。
    /// </summary>
    private void RebuildFogMask()
    {
        Texture2D mask = Shader.GetGlobalTexture("_FogMaskTex") as Texture2D;
        if (mask == null) return;

        Vector4 origin = Shader.GetGlobalVector("_FogMapOrigin");
        Vector4 size = Shader.GetGlobalVector("_FogMapSize");
        int w = mask.width, h = mask.height;
        if (w <= 0 || h <= 0 || size.x <= 0.0001f || size.y <= 0.0001f) return;

        if (_fogMaskData == null || _fogMaskData.Length != w * h)
            _fogMaskData = new Color32[w * h];

        // 全清 0（每帧重建时的初始状态 = 全未探索）
        for (int i = 0; i < _fogMaskData.Length; i++)
            _fogMaskData[i].r = 0;

        // 盖章所有 cell，强度由 FogAlpha 控制。
        foreach (HexCellData cell in _mapDataService.GetAllCells())
        {
            if (cell == null) continue;
            StampCellToFogMask(cell, mask, origin, size, w, h);
        }

        mask.SetPixels32(_fogMaskData);
        mask.Apply(false);
    }

    /// <summary>把一格的六边形足迹盖章进遮罩 R 通道（探索状态由 FogAlpha 驱动，0-255 连续值）。
    /// 从全局属性解析贴图坐标。</summary>
    private void StampCellToFogMask(HexCellData cell, Texture2D mask, Vector4 origin, Vector4 size, int w, int h)
    {
        Vector3 c = cell.CenterWorldCoordinate;
        float o = _config != null ? _config.OuterRadius : 3f;
        float ir = _config != null ? _config.InnerRadius : 2.598f;
        const float H = 0.8660254f;

        int px0 = Mathf.Clamp(Mathf.FloorToInt((c.x - o - origin.x) / size.x * w), 0, w - 1);
        int px1 = Mathf.Clamp(Mathf.CeilToInt((c.x + o - origin.x) / size.x * w), 0, w - 1);
        int py0 = Mathf.Clamp(Mathf.FloorToInt((c.z - o - origin.y) / size.y * h), 0, h - 1);
        int py1 = Mathf.Clamp(Mathf.CeilToInt((c.z + o - origin.y) / size.y * h), 0, h - 1);

        byte intensity = (byte)Mathf.RoundToInt(cell.FogAlpha * 255f);

        for (int py = py0; py <= py1; py++)
        {
            float wz = origin.y + (py + 0.5f) / h * size.y;
            for (int px = px0; px <= px1; px++)
            {
                float wx = origin.x + (px + 0.5f) / w * size.x;
                float dx = wx - c.x, dz = wz - c.z;
                if (Mathf.Abs(dx) <= ir &&
                    Mathf.Abs(0.5f * dx + H * dz) <= ir &&
                    Mathf.Abs(-0.5f * dx + H * dz) <= ir)
                {
                    int idx = py * w + px;
                    _fogMaskData[idx].r = intensity;
                }
            }
        }
    }
}

/// <summary>单个 Chunk 的渲染宿主数据（双缓冲 Mesh + 材质 + cell→range 映射）。</summary>
public sealed class ChunkRenderData
{
    public ChunkIndex Index;
    public GameObject Root;
    public MapChunkView View;
    public GameObject TerrainHost;
    public MeshFilter TerrainFilter;
    public MeshRenderer TerrainRenderer;
    public MeshCollider TerrainCollider;
    public GameObject WaterHost;
    public GameObject RiverHost;
    public GameObject GridHost;
    public MeshFilter GridFilter;
    public MeshRenderer GridRenderer;

    public Mesh ActiveTerrain;
    public Mesh StagingTerrain;
    public Mesh ActiveWater;
    public Mesh StagingWater;
    public Mesh ActiveRiver;
    public Mesh StagingRiver;
    public Mesh ActiveGrid;
    public Mesh StagingGrid;

    /// <summary>【程序化山脉-阶段 3.2】独立碰撞双缓冲 Mesh（决策 ㉚：山体不参与 MeshCollider）。
    /// 延迟创建：仅 HasMountain Chunk 提交时分配，无山 Chunk 保持碰撞 = 渲染 mesh。</summary>
    public Mesh ActiveCollision;
    public Mesh StagingCollision;

    /// <summary>【程序化山脉-阶段 5.1】最后一次已提交的山体拓扑签名（决策 ㉙/㉛）。
    /// 供下一次动画准备阶段与新的 staging 比较：仅 Height 变化 ⇒ 签名不变（可走高度动画）；
    /// 清除/水淹/恢复/阈值跨越 ⇒ 签名改变（阶段 5.5 据此整笔事务降级同步提交）。
    /// 无山 Chunk 恒为 MountainTopologySignature.Empty，零分配。</summary>
    public MountainTopologySignature LastMountainTopology;

    /// <summary>【动态地图-阶段四】本 Chunk 的动画进度属性块（_ChunkProgress，§20-10）。</summary>
    public readonly UnityEngine.MaterialPropertyBlock AnimationBlock = new UnityEngine.MaterialPropertyBlock();

    /// <summary>【阶段四修订】动画开始前保存的稳定 Terrain 材质数组（§19-21：动画期间切换 *_Transition
    /// 变体，Finalize 恢复稳定材质；null = 当前不在动画中）。</summary>
    public Material[] StableTerrainMaterials;

    /// <summary>【动态地图-阶段四】旧水面/河流淡出幽灵（§13.4 方案C 简化；Finalize 销毁）。</summary>
    public GameObject FadeWaterGhost;
    public GameObject FadeRiverGhost;

    /// <summary>【顶出方案-修订】旧地形 mesh 快照（动画期间 clip 平面之上垫底显示旧拓扑；
    /// Finalize 销毁，含独占克隆 mesh）。</summary>
    public GameObject TerrainGhost;

    /// <summary>【CPU动画-2026-08-05】动画期间逐帧写 mesh.vertices 用的缓存数据
    /// （Commit 时从 mesh.uv2/uv3/vertices 一次性读取，避免每帧调用 mesh getter 分配数组；
    /// 顶点动画已从 shader 移至 C#，surface shader 对未声明 UV 通道的读取不可靠）。
    /// AnimBaseVerts = 提交后的顶点（动画时仅沿用 X/Z；Y 无条件以 UV2.x 旧高度为基线，
    /// 防止无有效窗口的顶点泄漏目标高度）。</summary>
    public Vector2[] AnimUV2Cache;
    public Vector2[] AnimUV3Cache;
    public Vector3[] AnimBaseVerts;
    public Vector3[] AnimVertexBuffer;
    public bool AnimationReturnsToStart;

    public List<HexCellData> Cells = new List<HexCellData>();
}
