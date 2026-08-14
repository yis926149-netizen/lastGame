using UnityEngine;
using Zenject;

// The scene keeps this component and its .meta GUID so CostLabelPrefab remains serialized.
// WholeMap rendering was removed; this class now only coordinates backend-independent map visuals.
public sealed class MapPresentationBootstrap : MonoBehaviour, IMapPresentationBootstrap
{
    /// <summary>探索费用标签预制体：需在 Inspector 中指定（子物体需有 Text 组件）。</summary>
    public GameObject CostLabelPrefab;

    [Inject] private IMapDataService _mapDataService;
    [Inject] private MapGenerationConfigSO _config;
    [Inject] private ChunkMapRenderer _chunkMapRenderer;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private GoldWallet _goldWallet;
    [Inject(Id = "TargetUICanvas")] private Canvas _targetUICanvas;
    [Inject] private IExplorationService _explorationService;
    [Inject] private IExplorationCostProvider _costProvider;
    [Inject(Optional = true)] private ILogisticsService _logisticsService;

    private Texture2D _fogMaskTex;
    private GameObject _landFormRoot;
    private GameObject _resourceRoot;
    private FogEnvironmentSelectiveEffect _environmentFogEffect;
    private CostLabelRenderer _costLabelRenderer;
    private bool _isMapVisualSubscribed;
    private bool _isLogisticsSubscribed;

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
    }

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
        if (_landFormRoot != null) Destroy(_landFormRoot);
        _landFormRoot = new GameObject("LandForm");
        SetLayerRecursively(_landFormRoot, LayerMask.NameToLayer("FogAffectedEnvironment"));

        foreach (Vector3 coordinate in hexVertices)
        {
            HexCellData cell = _mapDataService.GetCell(coordinate);
            MapLandFormSO landForm = cell?.landForm;
            if (landForm == null || landForm.modelPrefab == null) continue;

            cell.landFormModel = Instantiate(landForm.modelPrefab, cell.RealCenterWorldCoordinate, Quaternion.identity, _landFormRoot.transform);
            if (cell.landFormModel.GetComponent<ModelController>() == null)
                cell.landFormModel.AddComponent<ModelController>();
            SetLayerRecursively(cell.landFormModel, _landFormRoot.layer);
        }
    }

    private void InstantiateResources(Vector3[] hexVertices)
    {
        if (_resourceRoot != null) Destroy(_resourceRoot);
        _resourceRoot = new GameObject("Resource");
        SetLayerRecursively(_resourceRoot, LayerMask.NameToLayer("FogAffectedEnvironment"));

        foreach (Vector3 coordinate in hexVertices)
        {
            HexCellData cell = _mapDataService.GetCell(coordinate);
            MapResourceSO resource = cell?.resource;
            if (resource == null || resource.modelPrefab == null) continue;

            cell.resourceModel = Instantiate(resource.modelPrefab, cell.RealCenterWorldCoordinate, Quaternion.identity, _resourceRoot.transform);
            if (cell.resourceModel.GetComponent<ModelController>() == null)
                cell.resourceModel.AddComponent<ModelController>();
            SetLayerRecursively(cell.resourceModel, _resourceRoot.layer);
        }
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
        Shader.SetGlobalFloat("_FogUnexploredDesaturate", 0.5f);
        Shader.SetGlobalFloat("_FogUnexploredBlend", 0.7f);
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
