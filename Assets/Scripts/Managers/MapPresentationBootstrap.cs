using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Zenject;
using Debug = UnityEngine.Debug;

// The scene keeps this component and its .meta GUID so CostLabelPrefab remains serialized.
// WholeMap rendering was removed; this class now only coordinates backend-independent map visuals.
public sealed class MapPresentationBootstrap : MonoBehaviour, IMapPresentationBootstrap
{
    /// <summary>探索费用标签预制体：需在 Inspector 中指定（子物体需有 Text 组件）。</summary>
    public GameObject CostLabelPrefab;

    /// <summary>伤害飘字预制体：需在 Inspector 中指定（Assets/UI/FloatingText.prefab，根节点需有 TextMeshProUGUI）。</summary>
    public GameObject FloatingTextPrefab;

    // ── 【P0-1 地图初始化分帧】分帧参数（可在 Inspector 按真机实测调整）────────
    [Header("分帧初始化（P0-1）")]
    [Tooltip("每帧提交的 Chunk 数量（对齐 MapMutationService.maxChunksPerFrame 默认值 2）。")]
    [SerializeField, Min(1)] private int _chunksPerFrame = 2;

    [Tooltip("每帧实例化的地貌/资源 prefab 数量（只统计真正 Instantiate 的格，跳过的空格不计）。")]
    [SerializeField, Min(1)] private int _instantiatesPerFrame = 30;

    [Tooltip("分帧期间是否锁定全图交互，防止玩家在半成品地图上点击。")]
    [SerializeField] private bool _lockInteractionWhileSlicing = true;

    [Inject] private IMapDataService _mapDataService;
    [Inject] private MapGenerationConfigSO _config;
    [Inject] private ChunkMapRenderer _chunkMapRenderer;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private GoldWallet _goldWallet;
    [Inject(Id = "TargetUICanvas")] private Canvas _targetUICanvas;
    [Inject] private IExplorationService _explorationService;
    [Inject] private IExplorationCostProvider _costProvider;
    [Inject(Optional = true)] private ILogisticsService _logisticsService;
    [Inject(Optional = true)] private MapInteractionGate _interactionGate;
    // 【伤害飘字】表现层事件总线
    [Inject] private DamageEventBroker _damageEventBroker;

    private Texture2D _fogMaskTex;
    private GameObject _landFormRoot;
    private GameObject _resourceRoot;
    private FogEnvironmentSelectiveEffect _environmentFogEffect;
    private CostLabelRenderer _costLabelRenderer;
    private DamageFloatTextRenderer _damageFloatTextRenderer;
    private bool _isMapVisualSubscribed;
    private bool _isLogisticsSubscribed;

    // ── 分帧状态机 ───────────────────────────────────────────
    private enum SlicedStage
    {
        /// <summary>未开始分帧（同步路径或尚未 Begin）。</summary>
        Idle,
        Chunks,
        LandForms,
        Resources,
        Finalize,
        Done
    }

    private SlicedStage _stage = SlicedStage.Idle;
    private readonly List<ChunkIndex> _pendingChunks = new List<ChunkIndex>();
    private readonly List<ChunkStagingGeometry> _builtStaging = new List<ChunkStagingGeometry>();
    private readonly List<ChunkIndex> _sliceBuffer = new List<ChunkIndex>();
    private readonly List<HexCellData> _landFormQueue = new List<HexCellData>();
    private readonly List<HexCellData> _resourceQueue = new List<HexCellData>();
    private readonly List<HexCellData> _lockedCells = new List<HexCellData>();
    private int _chunkCursor;
    private int _landFormCursor;
    private int _resourceCursor;
    private Action _onPresentationReady;

    // 缓存委托：避免每帧方法组转换产生一次 GC 分配（WebGL 上 GC 尖刺可见）
    private Func<HexCellData, bool> _spawnLandFormFn;
    private Func<HexCellData, bool> _spawnResourceFn;

    public bool IsPresentationReady { get; private set; }

    // ── 分帧耗时埋点（默认关闭、零开销；对照 ChunkMapRenderer.EnableChunkBuildTiming）────
    public static bool EnableSlicedInitTiming;
    public static double SlicedInitBeginMs;
    public static double SlicedInitChunkMs;
    public static double SlicedInitLandFormMs;
    public static double SlicedInitResourceMs;
    public static double SlicedInitFinalizeMs;
    public static double SlicedInitMaxSliceMs;
    public static int SlicedInitTickCount;

    /// <summary>清零分帧埋点计数（真机对照前调用）。</summary>
    public static void ResetSlicedInitTiming()
    {
        SlicedInitBeginMs = 0;
        SlicedInitChunkMs = 0;
        SlicedInitLandFormMs = 0;
        SlicedInitResourceMs = 0;
        SlicedInitFinalizeMs = 0;
        SlicedInitMaxSliceMs = 0;
        SlicedInitTickCount = 0;
    }

    [Inject]
    private void InitializeAfterInjection()
    {
        SubscribeVisualRefresh();
    }

    private void OnEnable() => SubscribeVisualRefresh();

    private void OnDisable()
    {
        if (_isMapVisualSubscribed && _mapVisualEvent != null)
        {
            _mapVisualEvent.OnMapVisualChanged.RemoveListener(OnMapVisualChanged);
            _isMapVisualSubscribed = false;
        }
        if (_isLogisticsSubscribed && _logisticsService != null)
        {
            _logisticsService.LogisticsChanged -= OnLogisticsChanged;
            _isLogisticsSubscribed = false;
        }
    }

    public void InitializeMapPresentation()
    {
        Vector3[] hexVertices = _mapDataService.GetHexVertices();
        SetupFogGlobalShaderProperties(hexVertices);
        _chunkMapRenderer.ChunkMapRender(hexVertices);
        InstantiateLandForms(hexVertices);
        InstantiateResources(hexVertices);
        SetupEnvironmentFogEffect();
        _mapVisualEvent.FogInit();
        EnsureCostLabelRenderer();
        EnsureDamageFloatTextRenderer();

        _stage = SlicedStage.Done;
        IsPresentationReady = true;
    }

    // ── 【P0-1 地图初始化分帧】────────────────────────────────

    /// <summary>
    /// 同帧只建骨架：雾全局属性（含遮罩纹理）+ Chunk 宿主 GameObject + LandForm/Resource 根节点，
    /// 并把 mesh 构建与 prefab 实例化排入分帧队列。返回后 <c>GameObject.Find("LandForm"/"Resource")</c>
    /// 与 Chunk 节点均已存在，<see cref="GameFlowManager"/> 的后续数据层步骤可原样同帧执行。
    /// </summary>
    public void BeginInitializeMapPresentation(Action onReady = null)
    {
        if (IsPresentationReady)
        {
            // 已就绪（含同步路径已跑过）：立即回调，保持调用方无分支。
            onReady?.Invoke();
            return;
        }

        _onPresentationReady += onReady;
        if (_stage != SlicedStage.Idle) return; // 已在分帧推进中，仅追加回调

        long startTicks = EnableSlicedInitTiming ? Stopwatch.GetTimestamp() : 0L;

        Vector3[] hexVertices = _mapDataService.GetHexVertices();
        SetupFogGlobalShaderProperties(hexVertices);

        _pendingChunks.Clear();
        _builtStaging.Clear();
        _chunkCursor = 0;
        IReadOnlyList<ChunkIndex> chunkIndices = _chunkMapRenderer.PrepareChunkHosts(hexVertices);
        for (int i = 0; i < chunkIndices.Count; i++)
            _pendingChunks.Add(chunkIndices[i]);

        _landFormRoot = RecreateEnvironmentRoot(_landFormRoot, "LandForm");
        _resourceRoot = RecreateEnvironmentRoot(_resourceRoot, "Resource");
        BuildInstantiateQueues(hexVertices);
        _spawnLandFormFn = SpawnLandFormModel;
        _spawnResourceFn = SpawnResourceModel;

        LockMapWhileSlicing();

        _stage = SlicedStage.Chunks;

        if (EnableSlicedInitTiming)
            SlicedInitBeginMs = TicksToMs(Stopwatch.GetTimestamp() - startTicks);
    }

    /// <summary>每帧推进一批（由 <see cref="MapPresentationSlicedInitExecutor"/> 驱动）。</summary>
    public bool TickInitializeMapPresentation()
    {
        if (_stage == SlicedStage.Done) return true;
        if (_stage == SlicedStage.Idle) return false;

        long startTicks = EnableSlicedInitTiming ? Stopwatch.GetTimestamp() : 0L;

        switch (_stage)
        {
            case SlicedStage.Chunks:
                TickChunkSlice();
                break;
            case SlicedStage.LandForms:
                TickInstantiateSlice(_landFormQueue, ref _landFormCursor, _spawnLandFormFn,
                    SlicedStage.Resources);
                break;
            case SlicedStage.Resources:
                TickInstantiateSlice(_resourceQueue, ref _resourceCursor, _spawnResourceFn,
                    SlicedStage.Finalize);
                break;
            case SlicedStage.Finalize:
                FinalizeSlicedInit();
                break;
        }

        if (EnableSlicedInitTiming)
        {
            SlicedInitTickCount++;
            double ms = TicksToMs(Stopwatch.GetTimestamp() - startTicks);
            if (ms > SlicedInitMaxSliceMs) SlicedInitMaxSliceMs = ms;
        }

        return _stage == SlicedStage.Done;
    }

    /// <summary>阶段 A：每帧构建并提交 <see cref="_chunksPerFrame"/> 个 Chunk 的几何。</summary>
    private void TickChunkSlice()
    {
        long startTicks = EnableSlicedInitTiming ? Stopwatch.GetTimestamp() : 0L;

        _sliceBuffer.Clear();
        int budget = Mathf.Max(1, _chunksPerFrame);
        while (_chunkCursor < _pendingChunks.Count && _sliceBuffer.Count < budget)
            _sliceBuffer.Add(_pendingChunks[_chunkCursor++]);

        if (_sliceBuffer.Count > 0)
        {
            PreparedChunkGeometry prepared = _chunkMapRenderer.PrepareChunkGeometrySlice(_sliceBuffer);
            // 逐帧提交时跳过跨 Chunk 边界法线合并——它读取「已提交」mesh，
            // 必须等全部 Chunk 就位后在 Finalize 统一执行一次（见 FinishInitialChunkBuild）。
            _chunkMapRenderer.CommitChunkGeometrySlice(prepared);
            _builtStaging.AddRange(prepared.Chunks);
        }

        if (EnableSlicedInitTiming)
            SlicedInitChunkMs += TicksToMs(Stopwatch.GetTimestamp() - startTicks);

        if (_chunkCursor >= _pendingChunks.Count)
            _stage = SlicedStage.LandForms;
    }

    /// <summary>
    /// 阶段 B/C 通用：每帧最多实例化 <see cref="_instantiatesPerFrame"/> 个模型。
    /// 预算只在 <paramref name="spawn"/> 返回 true（真正 Instantiate）时消耗，
    /// 空格不占预算，避免大量空格把队列拖成几十帧。
    /// </summary>
    private void TickInstantiateSlice(
        List<HexCellData> queue, ref int cursor, Func<HexCellData, bool> spawn, SlicedStage nextStage)
    {
        long startTicks = EnableSlicedInitTiming ? Stopwatch.GetTimestamp() : 0L;

        if (spawn == null)
        {
            _stage = nextStage;
            return;
        }

        int budget = Mathf.Max(1, _instantiatesPerFrame);
        while (cursor < queue.Count && budget > 0)
        {
            if (spawn(queue[cursor++])) budget--;
        }

        if (EnableSlicedInitTiming)
        {
            double ms = TicksToMs(Stopwatch.GetTimestamp() - startTicks);
            if (nextStage == SlicedStage.Resources) SlicedInitLandFormMs += ms;
            else SlicedInitResourceMs += ms;
        }

        if (cursor >= queue.Count)
            _stage = nextStage;
    }

    /// <summary>收尾（仅一次）：边界法线合并 → 雾效渲染器绑定 → FogInit → 费用标签 → 解锁 → 回调。</summary>
    private void FinalizeSlicedInit()
    {
        long startTicks = EnableSlicedInitTiming ? Stopwatch.GetTimestamp() : 0L;

        // 必须等全部 Chunk 提交完毕；FlatAll 风格下为 no-op（本工程当前配置即 FlatAll）
        _chunkMapRenderer.FinishInitialChunkBuild(_builtStaging);
        _builtStaging.Clear();

        SetupEnvironmentFogEffect();
        _mapVisualEvent.FogInit();
        EnsureCostLabelRenderer();
        EnsureDamageFloatTextRenderer();

        UnlockMapAfterSlicing();
        ReleaseSliceQueues();

        _stage = SlicedStage.Done;
        IsPresentationReady = true;

        if (EnableSlicedInitTiming)
        {
            SlicedInitFinalizeMs = TicksToMs(Stopwatch.GetTimestamp() - startTicks);
            Debug.Log($"[MapPresentationBootstrap] 分帧初始化完成：tick={SlicedInitTickCount} " +
                      $"begin={SlicedInitBeginMs:F1}ms chunk={SlicedInitChunkMs:F1}ms " +
                      $"landForm={SlicedInitLandFormMs:F1}ms resource={SlicedInitResourceMs:F1}ms " +
                      $"finalize={SlicedInitFinalizeMs:F1}ms 最长单次 Tick={SlicedInitMaxSliceMs:F1}ms");
        }

        Action callback = _onPresentationReady;
        _onPresentationReady = null;
        callback?.Invoke();
    }

    private void BuildInstantiateQueues(Vector3[] hexVertices)
    {
        _landFormQueue.Clear();
        _resourceQueue.Clear();
        _landFormCursor = 0;
        _resourceCursor = 0;
        if (hexVertices == null) return;

        foreach (Vector3 coordinate in hexVertices)
        {
            HexCellData cell = _mapDataService.GetCell(coordinate);
            if (cell == null) continue;
            if (cell.landForm != null && cell.landForm.modelPrefab != null) _landFormQueue.Add(cell);
            if (cell.resource != null && cell.resource.modelPrefab != null) _resourceQueue.Add(cell);
        }
    }

    private void ReleaseSliceQueues()
    {
        _pendingChunks.Clear();
        _sliceBuffer.Clear();
        _landFormQueue.Clear();
        _resourceQueue.Clear();
        _lockedCells.Clear();
        _chunkCursor = 0;
        _landFormCursor = 0;
        _resourceCursor = 0;
    }

    private void LockMapWhileSlicing()
    {
        _lockedCells.Clear();
        if (!_lockInteractionWhileSlicing || _interactionGate == null) return;

        foreach (HexCellData cell in _mapDataService.GetAllCells())
        {
            if (cell != null) _lockedCells.Add(cell);
        }
        _interactionGate.LockCells(_lockedCells);
    }

    private void UnlockMapAfterSlicing()
    {
        if (_interactionGate == null || _lockedCells.Count == 0) return;
        // 只解锁自己锁的格（对齐并行动画约定），不用 UnlockAll 清掉他人的锁。
        _interactionGate.UnlockCells(_lockedCells);
    }

    private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private void SubscribeVisualRefresh()
    {
        if (!_isMapVisualSubscribed && _mapVisualEvent != null)
        {
            _mapVisualEvent.OnMapVisualChanged.AddListener(OnMapVisualChanged);
            _isMapVisualSubscribed = true;
        }
        if (!_isLogisticsSubscribed && _logisticsService != null)
        {
            _logisticsService.LogisticsChanged += OnLogisticsChanged;
            _isLogisticsSubscribed = true;
        }
    }

    private void OnMapVisualChanged() => _environmentFogEffect?.RefreshRenderers();

    private void OnLogisticsChanged() => _environmentFogEffect?.RefreshRenderers();

    private void InstantiateLandForms(Vector3[] hexVertices)
    {
        _landFormRoot = RecreateEnvironmentRoot(_landFormRoot, "LandForm");

        foreach (Vector3 coordinate in hexVertices)
            SpawnLandFormModel(_mapDataService.GetCell(coordinate));
    }

    private void InstantiateResources(Vector3[] hexVertices)
    {
        _resourceRoot = RecreateEnvironmentRoot(_resourceRoot, "Resource");

        foreach (Vector3 coordinate in hexVertices)
            SpawnResourceModel(_mapDataService.GetCell(coordinate));
    }

    /// <summary>销毁旧根节点并新建同名空节点（下游 <c>GameObject.Find</c> 依赖这两个名字）。</summary>
    private GameObject RecreateEnvironmentRoot(GameObject existing, string rootName)
    {
        if (existing != null) Destroy(existing);
        var root = new GameObject(rootName);
        SetLayerRecursively(root, LayerMask.NameToLayer("FogAffectedEnvironment"));
        return root;
    }

    /// <summary>实例化单格地貌模型。返回 true 表示确实 Instantiate 了（分帧预算按此计数）。</summary>
    private bool SpawnLandFormModel(HexCellData cell)
    {
        MapLandFormSO landForm = cell?.landForm;
        if (landForm == null || landForm.modelPrefab == null) return false;
        if (_landFormRoot == null) return false;

        cell.landFormModel = Instantiate(landForm.modelPrefab, cell.RealCenterWorldCoordinate, Quaternion.identity, _landFormRoot.transform);
        if (cell.landFormModel.GetComponent<ModelController>() == null)
            cell.landFormModel.AddComponent<ModelController>();
        SetLayerRecursively(cell.landFormModel, _landFormRoot.layer);
        return true;
    }

    /// <summary>实例化单格资源模型。返回 true 表示确实 Instantiate 了（分帧预算按此计数）。</summary>
    private bool SpawnResourceModel(HexCellData cell)
    {
        MapResourceSO resource = cell?.resource;
        if (resource == null || resource.modelPrefab == null) return false;
        if (_resourceRoot == null) return false;

        cell.resourceModel = Instantiate(resource.modelPrefab, cell.RealCenterWorldCoordinate, Quaternion.identity, _resourceRoot.transform);
        if (cell.resourceModel.GetComponent<ModelController>() == null)
            cell.resourceModel.AddComponent<ModelController>();
        SetLayerRecursively(cell.resourceModel, _resourceRoot.layer);

        // 分帧路径下，资源模型晚于 PublicBuildingGenerator.MarkUnexplorableArea 才诞生，
        // 隐藏意图由 PublicBuildingGenerator.ApplyResourceVisibility() 在收尾回调里重放。
        return true;
    }

    private void SetupEnvironmentFogEffect()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogWarning("MapPresentationBootstrap: 找不到 Main Camera，跳过资源/地貌选择性雾化效果。");
            return;
        }

        _environmentFogEffect = mainCamera.GetComponent<FogEnvironmentSelectiveEffect>();
        if (_environmentFogEffect == null)
            _environmentFogEffect = mainCamera.gameObject.AddComponent<FogEnvironmentSelectiveEffect>();

        GameObject[] buildingRoots = { GameObject.Find("PlayerBuilding"), GameObject.Find("EnemyBuilding") };
        GameObject[] unitRoots = { GameObject.Find("PlayerUnit"), GameObject.Find("EnemyUnit") };
        _environmentFogEffect.Initialize(_landFormRoot, _resourceRoot, buildingRoots, unitRoots);
    }

    private void EnsureCostLabelRenderer()
    {
        if (CostLabelPrefab == null || _costLabelRenderer != null) return;
        var labelGo = new GameObject("CostLabelRenderer");
        labelGo.transform.SetParent(transform);
        _costLabelRenderer = labelGo.AddComponent<CostLabelRenderer>();
        _costLabelRenderer.Initialize(
            _mapDataService,
            _costProvider,
            _goldWallet,
            CostLabelPrefab,
            _targetUICanvas,
            _explorationService,
            _mapVisualEvent,
            _logisticsService);
    }

    /// <summary>
    /// 创建伤害飘字渲染器（屏幕空间）。FloatingTextPrefab 未指定时 LogError 并降级为不显示，
    /// 不阻断地图初始化（对齐 CardDragTargetMarkerController 的降级约定）。
    /// </summary>
    private void EnsureDamageFloatTextRenderer()
    {
        if (_damageFloatTextRenderer != null) return;
        if (FloatingTextPrefab == null)
        {
            Debug.LogError("[MapPresentationBootstrap] FloatingTextPrefab 未指定：伤害飘字降级为不显示。" +
                           "请在场景的 MapPresentationBootstrap 组件上指定 Assets/UI/FloatingText.prefab。");
            return;
        }
        var go = new GameObject("DamageFloatTextRenderer");
        go.transform.SetParent(transform);
        _damageFloatTextRenderer = go.AddComponent<DamageFloatTextRenderer>();
        _damageFloatTextRenderer.Initialize(
            FloatingTextPrefab,
            _targetUICanvas,
            _damageEventBroker);
    }

    private void SetupFogGlobalShaderProperties(Vector3[] hexVertices)
    {
        Material fogMaterial = _config != null ? _config.fogMaterial : null;
        Texture fogTexture = fogMaterial != null ? fogMaterial.GetTexture("_MainTex") : null;
        bool textureMissing = fogTexture == null;
        if (textureMissing) fogTexture = Texture2D.whiteTexture;
        else fogTexture.wrapMode = TextureWrapMode.Clamp;

        Color fogColor = fogMaterial != null && fogMaterial.HasProperty("_Color")
            ? fogMaterial.GetColor("_Color")
            : new Color(0.735f, 0.663f, 0.590f, 1f);

        float minX = float.MaxValue;
        float minZ = float.MaxValue;
        float maxX = float.MinValue;
        float maxZ = float.MinValue;
        if (hexVertices != null)
        {
            foreach (Vector3 coordinate in hexVertices)
            {
                HexCellData cell = _mapDataService.GetCell(coordinate);
                if (cell == null) continue;
                Vector3 center = cell.CenterWorldCoordinate;
                minX = Mathf.Min(minX, center.x);
                maxX = Mathf.Max(maxX, center.x);
                minZ = Mathf.Min(minZ, center.z);
                maxZ = Mathf.Max(maxZ, center.z);
            }
        }

        float padding = _config != null ? _config.OuterRadius : 3f;
        if (minX > maxX)
        {
            minX = minZ = 0f;
            maxX = maxZ = 1f;
        }
        minX -= padding;
        minZ -= padding;
        maxX += padding;
        maxZ += padding;
        float sizeX = Mathf.Max(0.0001f, maxX - minX);
        float sizeZ = Mathf.Max(0.0001f, maxZ - minZ);

        Shader.SetGlobalTexture("_FogTex", fogTexture);
        Shader.SetGlobalColor("_FogColor", fogColor);
        Shader.SetGlobalFloat("_FogEmission", 1f);
        Shader.SetGlobalFloat("_FogTexAmount", 1f);
        Shader.SetGlobalVector("_FogMapOrigin", new Vector4(minX, minZ, 0f, 0f));
        Shader.SetGlobalVector("_FogMapSize", new Vector4(sizeX, sizeZ, 0f, 0f));
        Shader.SetGlobalFloat("_FogPixelSize", _config != null ? _config.fogPixelSize : 0f);
        Shader.SetGlobalFloat("_FogJaggedAmount", _config != null ? _config.fogJaggedAmount : 1f);
        Shader.SetGlobalFloat("_FogNoiseWavelength", _config != null ? _config.fogNoiseWavelength : 2f);
        Shader.SetGlobalFloat("_FogEdgeStyle", _config != null ? (float)(int)_config.fogEdgeStyle : 0f);
        Shader.SetGlobalFloat("_FogEdgeSoftness", _config != null ? _config.fogEdgeSoftness : 0.8f);
        Shader.SetGlobalFloat("_FogEdgeAnimSpeed", _config != null ? _config.fogEdgeAnimSpeed : 0.25f);
        Shader.SetGlobalFloat("_FogEdgeTransparency", _config != null ? _config.fogEdgeTransparency : 0f);
        Shader.SetGlobalFloat("_FogUnexploredDesaturate", _config != null ? _config.fogUnexploredDesaturate : 0.5f);
        Shader.SetGlobalFloat("_FogUnexploredBlend", _config != null ? _config.fogUnexploredBlend : 0.7f);
        Shader.SetGlobalFloat("_FogCoverOcean", _config != null && _config.fogCoverOcean ? 1f : 0f);
        Shader.SetGlobalFloat("_FogCoverRiver", _config != null ? _config.fogCoverRiver : 0f);
        Shader.SetGlobalVector("_FogScrollSpeed", new Vector4(0.02f, 0.01f, 0f, 0f));

        CreateFogMask(minX, minZ, sizeX, sizeZ);
        Shader.SetGlobalTexture("_FogMaskTex", _fogMaskTex);

        if (fogMaterial == null)
            Debug.LogWarning("MapPresentationBootstrap: fogMaterial 为 null，已使用白纹理和默认雾色。");
        else if (textureMissing)
            Debug.LogWarning("MapPresentationBootstrap: fogMaterial 的 _MainTex 为空，已回退到白纹理。");
    }

    private void CreateFogMask(float minX, float minZ, float sizeX, float sizeZ)
    {
        float texel = Mathf.Max(0.25f, _config != null ? _config.fogMaskTexelSize : 2f);
        int width = Mathf.Clamp(Mathf.CeilToInt(sizeX / texel), 4, 1024);
        int height = Mathf.Clamp(Mathf.CeilToInt(sizeZ / texel), 4, 1024);
        if (_fogMaskTex != null) Destroy(_fogMaskTex);

        _fogMaskTex = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
        {
            name = "FogMaskTex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        _fogMaskTex.SetPixels32(new Color32[width * height]);
        _fogMaskTex.Apply(false);
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null || layer < 0) return;
        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private void OnDestroy()
    {
        if (_fogMaskTex != null) Destroy(_fogMaskTex);
    }
}
