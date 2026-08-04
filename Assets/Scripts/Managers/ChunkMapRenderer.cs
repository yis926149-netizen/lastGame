using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zenject;

//****************************************
// 【动态地图-阶段三】ChunkMapRenderer：8×8 offset-grid 分块渲染后端（§六/§九/§十一）。
// - 双后端并存：WholeMap / Chunked 由 MapGenerationConfigSO.mapRenderMode 配置切换（§二十-2）。
// - 每个 Chunk 持有 Terrain/Water/River 的 active/staging Mesh 双缓冲 + MeshCollider +
//   cell→顶点范围映射（迷雾顶点色回写，§6-1）+ 材质缓存（§6-2）。
// - 脏范围规则：改格 → 收集该格 + 一环邻居 → 所属 Chunk 去重（§七）。
// - 两阶段构建：阶段 1 为目标 Chunk + halo 预生成矩形 profile；阶段 2 生成目标 Chunk 自有几何（§九）。
// - 卡牌射线兼容：Chunk 根挂 MapChunkView，落点经 GetComponentInParent&lt;MapChunkView&gt; 判定（§11）。
// 阶段三范围：支持 FlatAll 法线（§二十-11），非 FlatAll 打运行时警告、不保证无缝。
//****************************************

public class ChunkMapRenderer : MonoBehaviour, IMapRenderBackend
{
    public const int ChunkSize = 8;

    // 【动态地图-阶段四】每 Chunk 动画进度属性（MaterialPropertyBlock，§20-10）
    private static readonly int ChunkProgressId = Shader.PropertyToID("_ChunkProgress");

    [Inject] private IMapDataService _mapDataService;
    [Inject] private MapGenerationConfigSO _config;
    [Inject] private IMeshGenerator _meshGenerator;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private GoldWallet _goldWallet;
    [Inject(Id = "TargetUICanvas")] private Canvas _targetUICanvas;
    [Inject] private IExplorationService _explorationService;
    [Inject(Optional = true)] private ILogisticsService _logisticsService;
    [Inject(Optional = true)] private IMapVisibilityResolver _visibilityResolver;

    /// <summary>地图根（WholeMap 后端把 mesh 挂这里；Chunk 后端仅用其作为 Chunk 根父节点）。</summary>
    public Transform ChunkRootParent;

    // Chunk 运行时数据：ChunkIndex → 渲染宿主
    private readonly Dictionary<ChunkIndex, ChunkRenderData> _chunks = new Dictionary<ChunkIndex, ChunkRenderData>();

    // 构建期注册表（每次 Prepare 重建；仅覆盖目标 Chunk + halo，§九）
    private IReadOnlyMapView _view;
    private readonly Dictionary<int, Vector3[]> _solidVertices = new Dictionary<int, Vector3[]>();
    private readonly Dictionary<int, Vector3[]> _lakeOrSeaVertices = new Dictionary<int, Vector3[]>();
    private readonly Dictionary<(int, Enums.HexDirection), List<Vector3>> _rectVerticesByCell = new Dictionary<(int, Enums.HexDirection), List<Vector3>>();
    private readonly Dictionary<(int owner, Enums.HexDirection direction), RectangleTransitionMeshData> _genericRectangleMeshes
        = new Dictionary<(int, Enums.HexDirection), RectangleTransitionMeshData>();

    // 材质缓存（按材质组合键共享，§6-2）
    private Material _terrainBaseMaterial0;
    private Material _terrainBaseMaterial1;
    private Material _terrainBaseMaterial2;
    private readonly Dictionary<(Material, Material), Material> _rectMaterialCache = new Dictionary<(Material, Material), Material>();
    private readonly Dictionary<(Material, Material, Material), Material> _triMaterialCache = new Dictionary<(Material, Material, Material), Material>();

    // 迷雾状态（与 MapRenderer 同构：过渡管理器 + 限频；全局遮罩贴图由 MapRenderer 统一维护）
    private readonly FogTransitionManager _fogTransition = new FogTransitionManager();
    private float _fogRefreshTimer;
    private const float FogRefreshInterval = 1f / 20f;
    private bool _fogInitialized;
    private bool _isSubscribed;

    public bool SupportsChunkedRebuild => true;

    /// <summary>【动态地图-阶段四】Chunked 后端支持 Shader 顶点动画（§20-10）。</summary>
    public bool SupportsAnimatedTransition => true;

    // ── 生命周期 ─────────────────────────────────────────────

    private void Awake()
    {
        Subscribe();
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void Update()
    {
        _fogTransition.Tick(Time.deltaTime);
        if (_fogTransition.IsDirty)
        {
            _fogRefreshTimer += Time.deltaTime;
            if (_fogRefreshTimer >= FogRefreshInterval)
            {
                _fogRefreshTimer = 0f;
                UpdateExplorationVisuals();
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
    }

    private void Subscribe()
    {
        if (!_isSubscribed && _mapVisualEvent != null)
        {
            _mapVisualEvent.OnMapVisualChanged.AddListener(OnMapVisualChanged);
            _isSubscribed = true;
        }
    }

    private void OnMapVisualChanged()
    {
        UpdateFogTransitionTargets();
    }

    // ── 首帧全量构建（由 MapRenderer.MapRender 在 Chunked 模式下分派调用）──────────

    /// <summary>首次全量渲染入口（Chunked 模式替代 MapRenderer 的网格构建部分；
    /// 迷雾全局属性/地貌/资源/费用标签由 MapRenderer.MapRender 统一处理）。</summary>
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

    // ── IMapRenderBackend：WholeMap 全量路径（Chunked 后端退化为全 Chunk 重建）────

    public PreparedWholeMapGeometry PrepareWholeMapGeometry()
    {
        // Chunked 后端：全量 = 所有 Chunk 都是脏 Chunk。产出 staging 由 CommitWholeMapGeometry 逐 Chunk 提交。
        var all = new List<HexCellData>(_mapDataService.GetAllCells());
        PreparedChunkGeometry chunkStaging = PrepareChunkGeometry(all);
        return new PreparedWholeMapGeometry { Chunked = chunkStaging };
    }

    public void CommitWholeMapGeometry(PreparedWholeMapGeometry geometry)
    {
        if (geometry?.Chunked == null) return;
        CommitChunkGeometry(geometry.Chunked);
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
        var anim = new AnimatedChunkBuildData
        {
            OldHeights = oldHeights,
            StaggerDelays = staggerDelays,
            ElevationStep = _config != null ? _config.elevationStep : 1f
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

    /// <summary>提交动画 staging，并把该 Chunk 的 MaterialPropertyBlock 进度置 0（动画起点）。</summary>
    public void CommitAnimatedChunkGeometry(PreparedChunkGeometry geometry)
    {
        if (geometry == null) return;

        // 阶段四：湖海/河流淡出（§13.4 方案C 简化）——提交前捕获旧水面/河流 mesh 克隆为幽灵，
        // 动画期间经 MPB 淡出，Finalize 销毁。仅当"旧有水而新无"（竞技场清湖海/河流）时创建。
        foreach (ChunkStagingGeometry staging in geometry.Chunks)
        {
            if (!_chunks.TryGetValue(staging.Index, out ChunkRenderData chunk)) continue;
            CaptureFadeGhosts(chunk, staging);
        }

        CommitChunkGeometry(geometry);
        if (geometry == null) return;
        foreach (ChunkStagingGeometry staging in geometry.Chunks)
            SetChunkAnimationProgress(staging.Index, 0f);
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

    private static readonly int FadeAlphaId = Shader.PropertyToID("_FadeAlpha");

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

    /// <summary>逐帧驱动 Chunk 动画进度：MaterialPropertyBlock 设置 _ChunkProgress（§20-10）。</summary>
    public void SetChunkAnimationProgress(ChunkIndex index, float progress)
    {
        if (!_chunks.TryGetValue(index, out ChunkRenderData chunk)) return;
        if (chunk.TerrainRenderer == null) return;
        chunk.AnimationBlock.SetFloat(ChunkProgressId, progress);
        chunk.TerrainRenderer.SetPropertyBlock(chunk.AnimationBlock);

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

    /// <summary>动画结束收尾：进度定格 1（顶点停在最终位置），销毁幽灵，并清理属性块。幂等。</summary>
    public void FinalizeChunkAnimation(ChunkIndex index)
    {
        if (!_chunks.TryGetValue(index, out ChunkRenderData chunk)) return;
        if (chunk.TerrainRenderer == null) return;
        chunk.AnimationBlock.SetFloat(ChunkProgressId, 1f);
        chunk.TerrainRenderer.SetPropertyBlock(chunk.AnimationBlock);

        if (chunk.FadeWaterGhost != null)
        {
            _ghostBlocks.Remove(chunk.FadeWaterGhost);
            Object.Destroy(chunk.FadeWaterGhost);
            chunk.FadeWaterGhost = null;
        }
        if (chunk.FadeRiverGhost != null)
        {
            _ghostBlocks.Remove(chunk.FadeRiverGhost);
            Object.Destroy(chunk.FadeRiverGhost);
            chunk.FadeRiverGhost = null;
        }
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
            ActiveTerrain = new Mesh { name = $"ChunkTerrain_{index.X}_{index.Z}" },
            StagingTerrain = new Mesh { name = $"ChunkTerrainStaging_{index.X}_{index.Z}" },
            ActiveWater = new Mesh { name = $"ChunkWater_{index.X}_{index.Z}" },
            StagingWater = new Mesh { name = $"ChunkWaterStaging_{index.X}_{index.Z}" },
            ActiveRiver = new Mesh { name = $"ChunkRiver_{index.X}_{index.Z}" },
            StagingRiver = new Mesh { name = $"ChunkRiverStaging_{index.X}_{index.Z}" },
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

        /// <summary>世界 Y 换算系数（elevationStep，§20-10：Height 级差 → 世界 Y 差）。</summary>
        public float ElevationStep = 1f;

        /// <summary>格子的高度变化量（世界 Y）：(新Height - 旧Height) * elevationStep。</summary>
        public float DeltaY(HexCellData cell)
        {
            if (cell == null) return 0f;
            if (OldHeights == null || !OldHeights.TryGetValue(cell.GenerateOrder, out float oldH)) return 0f;
            return (cell.Height - oldH) * ElevationStep;
        }

        /// <summary>格子的错峰延迟 [0,1]。</summary>
        public float Delay(HexCellData cell)
        {
            if (cell == null) return 0f;
            if (StaggerDelays == null || !StaggerDelays.TryGetValue(cell.GenerateOrder, out float d)) return 0f;
            return d;
        }
    }

    private ChunkStagingGeometry BuildChunkStaging(ChunkRenderData chunk, List<HexCellData> chunkCells, AnimatedChunkBuildData anim = null)
    {
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
        staging.Terrain = BuildChunkTerrain(chunkCells, profileList, staging.CellRanges, anim);
        staging.River = BuildChunkRiver(chunkCells, staging.CellRanges);
        staging.Water = BuildChunkWater(chunkCells, profileList, staging.CellRanges);
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
            }
        }
    }

    // ── 地形构建（阶段 2：只输出目标 Chunk 自有几何）────────────

    private MapRenderer.TerrainGeometry BuildChunkTerrain(
        List<HexCellData> chunkCells,
        List<HexCellData> profileCells,
        Dictionary<int, CellVertexRanges> cellRanges,
        AnimatedChunkBuildData anim = null)
    {
        var verticesList = new List<Vector3>();
        var uvList = new List<Vector2>();
        var allColors = new List<Color>();
        // 【阶段四】动画通道（§20-10）：UV2=(startVertexY,targetVertexY)、UV3=(staggerDelay,participates)
        var uv2List = anim != null ? new List<Vector2>() : null;
        var uv3List = anim != null ? new List<Vector2>() : null;
        var highDrawOrderList = new List<int>();
        var flatDrawOrderList = new List<int>();
        var seafloorDrawOrderList = new List<int>();
        var subList = new List<List<int>> { highDrawOrderList, flatDrawOrderList, seafloorDrawOrderList };
        var rectangleVertexRanges = new List<(int start, int count)>();
        var triangleVertexRanges = new List<(int start, int count)>();

        // 实心区域（只输出目标 Chunk 格）
        foreach (HexCellData hexCellData in chunkCells)
        {
            Vector3[] solid = _solidVertices[hexCellData.GenerateOrder];
            CellVertexRanges ranges = GetOrCreateCellRanges(cellRanges, hexCellData);
            ranges.SolidStart = verticesList.Count;
            ranges.SolidCount = solid.Length;

            Color cellColor = FogVertexColor(hexCellData);
            verticesList.AddRange(solid);
            uvList.AddRange(_meshGenerator.BuildSolidAreaUV(hexCellData));
            for (int c = 0; c < solid.Length; c++) allColors.Add(cellColor);

            if (anim != null)
            {
                float delta = anim.DeltaY(hexCellData);
                float delay = anim.Delay(hexCellData);
                for (int c = 0; c < solid.Length; c++)
                {
                    float y = solid[c].y;
                    uv2List.Add(new Vector2(y - delta, y));
                    uv3List.Add(new Vector2(delay, 1f));
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
            MainMeshDrawOrderElementAddRule(hexCellData, ints, ref subList, ranges.SolidStart);
        }

        // 矩形过渡（只输出目标 Chunk 格；profile 已在阶段 1 预生成）
        var rectGroups = new Dictionary<(Material, Material), List<int>>();
        Enums.HexDirection[] dirs = { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE };
        foreach (HexCellData hexCellData in chunkCells)
        {
            Color cellColor = FogVertexColor(hexCellData);
            CellVertexRanges ranges = cellRanges[hexCellData.GenerateOrder];
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
                        int addedCount = verticesList.Count - preCount;
                        for (int c = 0; c < addedCount; c++) allColors.Add(cellColor);
                        ranges.TransitionRanges.Add((preCount, addedCount));
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
                        int addedCount = verticesList.Count - preCount;
                        for (int c = 0; c < addedCount; c++) allColors.Add(cellColor);
                        ranges.TransitionRanges.Add((preCount, addedCount));
                        OtherMeshDrawOrderElementAddRule(hexCellData, _meshGenerator.BuildRectStepRiverDrawOrder(ctx, rectVerts), ref ints, IndexOffset);
                        _rectVerticesByCell[(hexCellData.GenerateOrder, dir)] = rectVerts;
                        if (anim != null)
                            AppendRiverRectAnimUV(anim, hexCellData, rectVerts, uv2List, uv3List);
                    }
                }
                else
                {
                    _rectVerticesByCell[(hexCellData.GenerateOrder, dir)] = new List<Vector3>();
                    int preCount = verticesList.Count;
                    RectangleTransitionMeshData usedRect = RectFlat(_config.shadingStyle)
                        ? RectangleTransitionMesh.ToFlatShaded(GetGenericRectangleMesh(ctx, dir))
                        : GetGenericRectangleMesh(ctx, dir);
                    verticesList.AddRange(usedRect.Vertices);
                    uvList.AddRange(usedRect.UVs);
                    int addedCount = verticesList.Count - preCount;
                    for (int c = 0; c < addedCount; c++) allColors.Add(cellColor);
                    ranges.TransitionRanges.Add((preCount, addedCount));
                    OtherMeshDrawOrderElementAddRule(hexCellData, usedRect.Indices, ref ints, IndexOffset);
                    if (anim != null)
                        AppendRectAnimUV(anim, hexCellData, dir, usedRect, uv2List, uv3List);
                }

                int rectangleVertexCount = verticesList.Count - IndexOffset;
                if (rectangleVertexCount > 0)
                    rectangleVertexRanges.Add((IndexOffset, rectangleVertexCount));

                Material matA = HexController.SetHexMaterial(hexCellData, _config.mapMaterial);
                Material matB = HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, dir), _config.mapMaterial);
                var key = (matA, matB);
                if (!rectGroups.ContainsKey(key))
                    rectGroups[key] = new List<int>();
                rectGroups[key].AddRange(ints);
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
            Color cellColor = FogVertexColor(hexCellData);
            CellVertexRanges ranges = cellRanges[hexCellData.GenerateOrder];
            foreach (Enums.HexDirection[] pair in triDirs)
            {
                if (_mapDataService.GetNeighbor(hexCellData, pair[0]) == null ||
                    _mapDataService.GetNeighbor(hexCellData, pair[1]) == null) continue;
                int IndexOffset = verticesList.Count;
                List<int> ints = new List<int>();
                int preCount = verticesList.Count;
                CellBuildContext ctx = MakeBuildContext(hexCellData);
                TriangleTransitionMeshData triangle = GetGenericTriangleMesh(ctx, pair[0], pair[1]);
                verticesList.AddRange(triangle.Vertices);
                uvList.AddRange(triangle.UVs);
                int addedCount = verticesList.Count - preCount;
                for (int c = 0; c < addedCount; c++) allColors.Add(cellColor);
                ranges.TransitionRanges.Add((preCount, addedCount));
                triangleVertexRanges.Add((preCount, addedCount));
                OtherMeshDrawOrderElementAddRule(hexCellData, triangle.Indices, ref ints, IndexOffset);

                if (anim != null)
                    AppendTriangleAnimUV(anim, hexCellData, pair, triangle, uv2List, uv3List);

                Material matA = HexController.SetHexMaterial(hexCellData, _config.mapMaterial);
                Material matB = HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, pair[0]), _config.mapMaterial);
                Material matC = HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, pair[1]), _config.mapMaterial);
                var key = (matA, matB, matC);
                if (!triGroups.ContainsKey(key))
                    triGroups[key] = new List<int>();
                triGroups[key].AddRange(ints);
            }
        }
        var mergedTriIndices = new List<List<int>>(triGroups.Values);
        var mergedMaterialAsTri = triGroups.Keys.Select(k => k.Item1).ToList();
        var mergedMaterialBsTri = triGroups.Keys.Select(k => k.Item2).ToList();
        var mergedMaterialCsTri = triGroups.Keys.Select(k => k.Item3).ToList();

        int[][] arrArawOrder = new int[3 + mergedRectIndices.Count + mergedTriIndices.Count][];
        arrArawOrder[0] = subList[2].ToArray();
        arrArawOrder[1] = subList[1].ToArray();
        arrArawOrder[2] = subList[0].ToArray();
        int offset = 3;
        foreach (List<int> rect in mergedRectIndices)
            arrArawOrder[offset++] = rect.ToArray();
        foreach (List<int> tri in mergedTriIndices)
            arrArawOrder[offset++] = tri.ToArray();

        return new MapRenderer.TerrainGeometry
        {
            Vertices = verticesList.ToArray(),
            UVs = uvList.ToArray(),
            Colors = allColors.ToArray(),
            SubMeshIndices = arrArawOrder,
            BaseMaterials = _config.mapMaterial,
            RectAs = mergedMaterialAs.ToArray(),
            RectBs = mergedMaterialBs.ToArray(),
            TriAs = mergedMaterialAsTri.ToArray(),
            TriBs = mergedMaterialBsTri.ToArray(),
            TriCs = mergedMaterialCsTri.ToArray(),
            RectangleRanges = rectangleVertexRanges,
            TriangleRanges = triangleVertexRanges,
            VerticesList = verticesList,
            UV2s = uv2List?.ToArray(),
            UV3s = uv3List?.ToArray()
        };
    }

    // ── 阶段四：每顶点动画通道生成（§20-10）──────────────────
    // UV2.x=startVertexY（旧高度）、UV2.y=targetVertexY（新高度=当前顶点Y）；
    // UV3.x=错峰延迟、UV3.y=participatesInTransition=1。
    // 矩形/三角过渡顶点按几何端点来源格写 startY/delay，内部插值点对端点插值（§13.3）。

    /// <summary>河流矩形过渡（顶点按固定布局混合两端格，按 owner 格 delta 近似，§13.3 第一版简化）。</summary>
    private static void AppendRiverRectAnimUV(
        AnimatedChunkBuildData anim,
        HexCellData owner,
        IReadOnlyList<Vector3> rectVerts,
        List<Vector2> uv2List,
        List<Vector2> uv3List)
    {
        float deltaOwner = anim.DeltaY(owner);
        float delayOwner = anim.Delay(owner);
        for (int c = 0; c < rectVerts.Count; c++)
        {
            float y = rectVerts[c].y;
            uv2List.Add(new Vector2(y - deltaOwner, y));
            uv3List.Add(new Vector2(delayOwner, 1f));
        }
    }

    /// <summary>非河流矩形过渡：按 UV.v（profile 进度 0=self→1=neighbor）插值两端格 delta/delay。</summary>
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
        for (int c = 0; c < rect.Vertices.Count; c++)
        {
            float t = Mathf.Clamp01(rect.UVs[c].y);
            float delta = Mathf.Lerp(deltaOwner, deltaNeighbor, t);
            float delay = Mathf.Lerp(delayOwner, delayNeighbor, t);
            float y = rect.Vertices[c].y;
            uv2List.Add(new Vector2(y - delta, y));
            uv3List.Add(new Vector2(delay, 1f));
        }
    }

    /// <summary>三角过渡：按重心 UV（(u,v)，self 权重=1-u-v）插值三端格 delta/delay。</summary>
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
        for (int c = 0; c < triangle.Vertices.Count; c++)
        {
            Vector2 uv = triangle.UVs[c];
            float wSelf = 1f - uv.x - uv.y;
            float delta = wSelf * dSelf + uv.x * dA + uv.y * dB;
            float delay = wSelf * delaySelf + uv.x * delayA + uv.y * delayB;
            float y = triangle.Vertices[c].y;
            uv2List.Add(new Vector2(y - delta, y));
            uv3List.Add(new Vector2(delay, 1f));
        }
    }

    // ── 河流构建（只输出目标 Chunk 格）──────────────────────

    private MapRenderer.RiverGeometry BuildChunkRiver(
        List<HexCellData> chunkCells,
        Dictionary<int, CellVertexRanges> cellRanges)
    {
        var verticesRiverWater = new List<Vector3>();
        var uvRiverWater = new List<Vector2>();
        var riverColors = new List<Color>();
        var drawOrderRiverWater = new List<int>();

        void RecordRiver(HexCellData cell, CellVertexRanges ranges, int preCount)
        {
            int added = verticesRiverWater.Count - preCount;
            if (added <= 0) return;
            Color col = FogVertexColor(cell);
            for (int c = 0; c < added; c++) riverColors.Add(col);
            ranges.RiverRanges.Add((preCount, added));
        }

        foreach (HexCellData hexCellData in chunkCells)
        {
            List<int> ints = new List<int>();
            int IndexOffset = verticesRiverWater.Count;
            CellVertexRanges ranges = cellRanges[hexCellData.GenerateOrder];
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
            RecordRiver(hexCellData, ranges, IndexOffset);
        }
        foreach (HexCellData hexCellData in chunkCells)
        {
            List<int> ints = new List<int>();
            int IndexOffset = verticesRiverWater.Count;
            CellVertexRanges ranges = cellRanges[hexCellData.GenerateOrder];
            if (RiverMeshSolidAreaDrawOrderFunction(hexCellData) == null) continue;

            CellBuildContext ctx = MakeBuildContext(hexCellData);
            verticesRiverWater.AddRange(_meshGenerator.BuildOutgoingRiverVertices(ctx));
            List<int> l = new List<int>();
            l.AddRange(RiverMeshDownstreamDrawOrderFunction());
            OtherMeshDrawOrderElementAddRule(hexCellData, l, ref ints, IndexOffset);
            drawOrderRiverWater.AddRange(ints);
            uvRiverWater.AddRange(_meshGenerator.BuildOutgoingRiverSlopUV());
            RecordRiver(hexCellData, ranges, IndexOffset);
        }

        if (drawOrderRiverWater.Count % 3 == 0 && drawOrderRiverWater.Count != 0)
        {
            return new MapRenderer.RiverGeometry
            {
                Vertices = verticesRiverWater.ToArray(),
                UVs = uvRiverWater.ToArray(),
                Indices = drawOrderRiverWater.ToArray(),
                Colors = riverColors.ToArray()
            };
        }
        return null;
    }

    // ── 湖海构建（只输出目标 Chunk 格；依赖 halo 湖海注册表）────────

    private MapRenderer.WaterGeometry BuildChunkWater(
        List<HexCellData> chunkCells,
        List<HexCellData> profileCells,
        Dictionary<int, CellVertexRanges> cellRanges)
    {
        var verticesLakeOrSea = new List<Vector3>();
        var uvLakeOrSea = new List<Vector2>();
        var lakeColors = new List<Color>();
        var drawOrderLakeOrSea = new List<int>();
        var drawOrderCoast = new List<int>();

        void RecordWater(HexCellData cell, CellVertexRanges ranges, int preCount)
        {
            int added = verticesLakeOrSea.Count - preCount;
            if (added <= 0) return;
            Color col = FogVertexColor(cell);
            for (int c = 0; c < added; c++) lakeColors.Add(col);
            ranges.WaterRanges.Add((preCount, added));
        }

        // 湖海实心（只输出目标 Chunk 格；注册表已在阶段 1 就位）
        foreach (HexCellData hexCellData in chunkCells)
        {
            if (!WaterLevelConfig.IsWater(hexCellData)) continue;
            hexCellData.HexType = Enums.HexType.LakeOrSea;
            hexCellData.isCoast = true;
            hexCellData.waterLevel = _config.seaLevel;

            int IndexOffset = verticesLakeOrSea.Count;
            CellVertexRanges ranges = cellRanges[hexCellData.GenerateOrder];
            CellBuildContext ctx = MakeBuildContext(hexCellData);
            Vector3[] lakeVerts = _meshGenerator.BuildLakeOrSeaVertices(ctx);
            _lakeOrSeaVertices[hexCellData.GenerateOrder] = lakeVerts;
            verticesLakeOrSea.AddRange(lakeVerts);
            uvLakeOrSea.AddRange(_meshGenerator.BuildLakeOrSeaUV());
            RecordWater(hexCellData, ranges, IndexOffset);

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
            CellVertexRanges ranges = cellRanges[hexCellData.GenerateOrder];
            foreach (Enums.HexDirection dir in dirs)
            {
                HexCellData neighbor = _mapDataService.GetNeighbor(hexCellData, dir);
                if (neighbor == null || !_lakeOrSeaVertices.ContainsKey(neighbor.GenerateOrder)) continue;

                int IndexOffset = verticesLakeOrSea.Count;
                CellBuildContext ctx = MakeBuildContext(hexCellData);
                verticesLakeOrSea.AddRange(_meshGenerator.BuildLakeOrSeaRectVertices(ctx, dir));
                uvLakeOrSea.AddRange(_meshGenerator.BuildLakeOrSeaRectUV(dir));
                RecordWater(hexCellData, ranges, IndexOffset);

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
            CellVertexRanges ranges = cellRanges[hexCellData.GenerateOrder];
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
                RecordWater(hexCellData, ranges, IndexOffset);

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
            CellVertexRanges ranges = cellRanges[hexCellData.GenerateOrder];

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
            RecordWater(hexCellData, ranges, IndexOffset);

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
            CellVertexRanges ranges = cellRanges[hexCellData.GenerateOrder];

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
            RecordWater(hexCellData, ranges, IndexOffset);

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
        return new MapRenderer.WaterGeometry
        {
            Vertices = verticesLakeOrSea.ToArray(),
            UVs = uvLakeOrSea.ToArray(),
            Indices = arrArawOrderLakeOrSea,
            Colors = lakeColors.ToArray()
        };
    }

    // ── staging → active 提交（双缓冲交换 + 材质复用）────────

    private void CommitChunkStaging(ChunkRenderData chunk, ChunkStagingGeometry staging)
    {
        if (!ValidateChunkStaging(staging, out string validationError))
        {
            Debug.LogError($"[ChunkMapRenderer] 拒绝提交无效几何 {staging.Index}: {validationError}");
            return;
        }

        // Terrain
        if (staging.Terrain != null && staging.Terrain.Vertices != null && staging.Terrain.Vertices.Length > 0)
        {
            FillMeshData(chunk.StagingTerrain, staging.Terrain);
            chunk.TerrainFilter.sharedMesh = chunk.StagingTerrain;
            chunk.TerrainRenderer.sharedMaterials = ResolveTerrainMaterials(staging.Terrain);
            chunk.TerrainCollider.sharedMesh = chunk.StagingTerrain;
            chunk.TerrainRenderer.enabled = true;
        }
        else
        {
            chunk.TerrainRenderer.enabled = false;
            chunk.TerrainCollider.sharedMesh = null;
        }

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

        chunk.CellRanges.Clear();
        foreach (KeyValuePair<int, CellVertexRanges> kv in staging.CellRanges)
            chunk.CellRanges[kv.Key] = kv.Value;
    }

    private static void FillMeshData(Mesh mesh, MapRenderer.TerrainGeometry geometry)
    {
        // Drop stale animated UV2/UV3 layout when this buffer is reused by an ordinary build.
        mesh.Clear(false);
        mesh.indexFormat = geometry.Vertices.Length > ushort.MaxValue
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = geometry.Vertices;
        mesh.uv = geometry.UVs;
        mesh.colors = geometry.Colors;
        // 【阶段四】动画通道（§20-10）：仅在动画构建时写入，普通重建不写（shader 读到 0 → 不参与）
        if (geometry.UV2s != null) mesh.uv2 = geometry.UV2s;
        if (geometry.UV3s != null) mesh.uv3 = geometry.UV3s;
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
    }

    private static void FillRiverMeshData(Mesh mesh, MapRenderer.RiverGeometry geometry)
    {
        mesh.Clear(false);
        mesh.indexFormat = geometry.Vertices.Length > ushort.MaxValue
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = geometry.Vertices;
        mesh.uv = geometry.UVs;
        mesh.colors = geometry.Colors;
        mesh.triangles = geometry.Indices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private static void FillWaterMeshData(Mesh mesh, MapRenderer.WaterGeometry geometry)
    {
        mesh.Clear(false);
        mesh.indexFormat = geometry.Vertices.Length > ushort.MaxValue
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = geometry.Vertices;
        mesh.uv = geometry.UVs;
        mesh.colors = geometry.Colors;
        mesh.subMeshCount = geometry.Indices.Length;
        for (int i = 0; i < geometry.Indices.Length; i++)
        {
            if (geometry.Indices[i] != null && geometry.Indices[i].Length > 0)
                mesh.SetTriangles(geometry.Indices[i], i);
        }
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
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
        return true;
    }

    private static bool ValidateTerrain(MapRenderer.TerrainGeometry geometry, out string error)
    {
        if (geometry == null)
        {
            error = null;
            return true;
        }
        if (!ValidateVertexChannels(geometry.Vertices, geometry.UVs, geometry.Colors, "Terrain", out error))
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
        return true;
    }

    private static bool ValidateRiver(MapRenderer.RiverGeometry geometry, out string error)
    {
        if (geometry == null)
        {
            error = null;
            return true;
        }
        if (!ValidateVertexChannels(geometry.Vertices, geometry.UVs, geometry.Colors, "River", out error))
            return false;
        return ValidateIndices(geometry.Indices, geometry.Vertices.Length, "River", out error);
    }

    private static bool ValidateWater(MapRenderer.WaterGeometry geometry, out string error)
    {
        if (geometry == null)
        {
            error = null;
            return true;
        }
        if (!ValidateVertexChannels(geometry.Vertices, geometry.UVs, geometry.Colors, "Water", out error))
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

    private static bool ValidateVertexChannels(Vector3[] vertices, Vector2[] uvs, Color[] colors, string label, out string error)
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
        if (colors == null || colors.Length != vertices.Length)
        {
            error = $"{label} colors={colors?.Length ?? -1}, vertices={vertices.Length}";
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

    private Material[] ResolveTerrainMaterials(MapRenderer.TerrainGeometry geometry)
    {
        Material[] allMaterials = new Material[geometry.SubMeshIndices.Length];
        if (_terrainBaseMaterial0 == null)
        {
            Shader terrainFogShader = Shader.Find("Custom/TerrainBase_Fog") ?? Shader.Find("Standard");
            _terrainBaseMaterial0 = MapController.CreateTerrainFogMaterial(geometry.BaseMaterials[0], terrainFogShader);
            _terrainBaseMaterial1 = MapController.CreateTerrainFogMaterial(geometry.BaseMaterials[1], terrainFogShader);
            _terrainBaseMaterial2 = MapController.CreateTerrainFogMaterial(geometry.BaseMaterials[2], terrainFogShader);
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
                    key.Item2, key.Item1, _config.blendMask, _config.blendContrast, _config.blendSmooth);
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
                    key.Item1, key.Item2, key.Item3, triMask, _config.blendContrast, _config.globalSmoothness);
                _triMaterialCache[key] = mat;
            }
            allMaterials[3 + geometry.RectAs.Length + i] = mat;
        }
        return allMaterials;
    }

    // ── 构建辅助（与 MapRenderer 一致的判定/分发逻辑）─────────

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

    private static CellVertexRanges GetOrCreateCellRanges(Dictionary<int, CellVertexRanges> cellRanges, HexCellData cell)
    {
        if (cellRanges.TryGetValue(cell.GenerateOrder, out CellVertexRanges ranges)) return ranges;
        ranges = new CellVertexRanges();
        cellRanges[cell.GenerateOrder] = ranges;
        return ranges;
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
        if (_config.useHeightBasedSubdivision)
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

    private static Color FogVertexColor(HexCellData cell)
    {
        float r = cell.FogAlpha;
        return new Color(r, 0f, 0f, 1f);
    }

    // ── RefreshCellObjects：变化格对象刷新 ───────────────────

    public void RefreshCellObjects(IReadOnlyCollection<HexCellData> changedCells, RemovedVisualHandle removed)
    {
        if (changedCells == null) return;

        // Chunk 后端：网格线重建 → 统一在"变化格所属 Chunk"重建路径（Geometry 随 Chunk 重建）。
        // 此处只处理地貌/资源模型移除与归位。
        foreach (HexCellData cell in changedCells)
        {
            if (cell == null) continue;

            if (cell.landForm == null && cell.landFormModel != null)
            {
                removed?.Add(cell.landFormModel);
                cell.landFormModel = null;
            }
            if (cell.resource == null && cell.resourceModel != null)
            {
                removed?.Add(cell.resourceModel);
                cell.resourceModel = null;
            }

            if (cell.landFormModel != null)
                cell.landFormModel.transform.position = cell.RealCenterWorldCoordinate;
            if (cell.resourceModel != null)
                cell.resourceModel.transform.position = cell.RealCenterWorldCoordinate;
        }
    }

    // ── 迷雾（全局遮罩贴图由 MapRenderer 统一维护；此处只做逐 Chunk 顶点色）──────

    public void ForceRefreshFogVisuals()
    {
        if (!_fogInitialized) return;
        UpdateFogTransitionTargets();
        UpdateExplorationVisuals();
        RebuildFogMask();
        _fogTransition.ClearDirty();
        _fogRefreshTimer = 0f;
    }

    private void UpdateFogTransitionTargets()
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
            if (_fogInitialized)
                _fogTransition.RequestTransition(cell, targetAlpha);
            else
                _fogTransition.SnapTransition(cell, targetAlpha);
        }

        if (!_fogInitialized)
        {
            _fogInitialized = true;
            UpdateExplorationVisuals();
            RebuildFogMask();
            _fogTransition.ClearDirty();
        }
    }

    private void UpdateExplorationVisuals()
    {
        foreach (ChunkRenderData chunk in _chunks.Values)
        {
            if (chunk.CellRanges.Count == 0) continue;
            Mesh terrainMesh = chunk.TerrainFilter.sharedMesh;
            if (terrainMesh == null || terrainMesh.vertexCount == 0) continue;

            Color[] colors = terrainMesh.colors;
            if (colors == null || colors.Length != terrainMesh.vertexCount)
                colors = new Color[terrainMesh.vertexCount];

            foreach (KeyValuePair<int, CellVertexRanges> kv in chunk.CellRanges)
            {
                if (!_mapDataService.TryGetCell(kv.Key, out HexCellData cell) || cell == null) continue;
                Color newColor = FogVertexColor(cell);
                CellVertexRanges ranges = kv.Value;
                if (ranges.SolidStart >= 0)
                {
                    for (int i = 0; i < ranges.SolidCount && ranges.SolidStart + i < colors.Length; i++)
                        colors[ranges.SolidStart + i] = newColor;
                }
                foreach (var range in ranges.TransitionRanges)
                {
                    for (int i = 0; i < range.count && range.start + i < colors.Length; i++)
                        colors[range.start + i] = newColor;
                }
            }
            terrainMesh.colors = colors;

            // 水面顶点色
            Mesh waterMesh = chunk.WaterHost.GetComponent<MeshFilter>().sharedMesh;
            if (waterMesh != null && waterMesh.vertexCount > 0)
            {
                Color[] waterColors = waterMesh.colors;
                if (waterColors == null || waterColors.Length != waterMesh.vertexCount)
                    waterColors = new Color[waterMesh.vertexCount];
                foreach (KeyValuePair<int, CellVertexRanges> kv in chunk.CellRanges)
                {
                    if (!_mapDataService.TryGetCell(kv.Key, out HexCellData cell) || cell == null) continue;
                    Color newColor = FogVertexColor(cell);
                    foreach (var range in kv.Value.WaterRanges)
                    {
                        for (int i = 0; i < range.count && range.start + i < waterColors.Length; i++)
                            waterColors[range.start + i] = newColor;
                    }
                }
                waterMesh.colors = waterColors;
            }

            // 河流顶点色
            Mesh riverMesh = chunk.RiverHost.GetComponent<MeshFilter>().sharedMesh;
            if (riverMesh != null && riverMesh.vertexCount > 0)
            {
                Color[] riverColors = riverMesh.colors;
                if (riverColors == null || riverColors.Length != riverMesh.vertexCount)
                    riverColors = new Color[riverMesh.vertexCount];
                foreach (KeyValuePair<int, CellVertexRanges> kv in chunk.CellRanges)
                {
                    if (!_mapDataService.TryGetCell(kv.Key, out HexCellData cell) || cell == null) continue;
                    Color newColor = FogVertexColor(cell);
                    foreach (var range in kv.Value.RiverRanges)
                    {
                        for (int i = 0; i < range.count && range.start + i < riverColors.Length; i++)
                            riverColors[range.start + i] = newColor;
                    }
                }
                riverMesh.colors = riverColors;
            }
        }
    }

    private void RebuildFogMask()
    {
        // 【阶段三】全局探索遮罩贴图由 MapRenderer（WholeMap 统一迷雾管线）持有并重建；
        // Chunk 后端只负责逐 Chunk 顶点色回写（UpdateExplorationVisuals），此处为空实现兜底。
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

    public Mesh ActiveTerrain;
    public Mesh StagingTerrain;
    public Mesh ActiveWater;
    public Mesh StagingWater;
    public Mesh ActiveRiver;
    public Mesh StagingRiver;

    /// <summary>【动态地图-阶段四】本 Chunk 的动画进度属性块（_ChunkProgress，§20-10）。</summary>
    public readonly UnityEngine.MaterialPropertyBlock AnimationBlock = new UnityEngine.MaterialPropertyBlock();

    /// <summary>【动态地图-阶段四】旧水面/河流淡出幽灵（§13.4 方案C 简化；Finalize 销毁）。</summary>
    public GameObject FadeWaterGhost;
    public GameObject FadeRiverGhost;

    public readonly Dictionary<int, CellVertexRanges> CellRanges = new Dictionary<int, CellVertexRanges>();
    public List<HexCellData> Cells = new List<HexCellData>();
}
