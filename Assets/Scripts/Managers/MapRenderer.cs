using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Zenject;

public class MapRenderer : MonoBehaviour, IMapRenderBackend
{
    private readonly Dictionary<(int owner, Enums.HexDirection direction), RectangleTransitionMeshData> _genericRectangleMeshes
        = new Dictionary<(int owner, Enums.HexDirection direction), RectangleTransitionMeshData>();

    /// <summary>探索费用标签预制体：需在 Inspector 中指定（子物体需有 Text 组件）</summary>
    public GameObject CostLabelPrefab;

    [Inject] private IMapDataService _mapDataService;
    [Inject] private MapGenerationConfigSO _config;
    [Inject] private IMeshGenerator _meshGenerator;
    [Inject] private MapGenerator mapGenerator;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private GoldWallet _goldWallet;
    [Inject(Id = "TargetUICanvas")] private Canvas _targetUICanvas;
    [Inject] private IExplorationService _explorationService;
    [Inject(Optional = true)] private ILogisticsService _logisticsService;
    // 【动态地图-阶段二】统一可见性解析（永久 || 临时 VisibilityLease），迷雾目标/血条共用
    [Inject(Optional = true)] private IMapVisibilityResolver _visibilityResolver;

    // 【动态地图-阶段三】Chunked 渲染后端（MapRender 内分派网格构建）
    [Inject(Optional = true)] private ChunkMapRenderer _chunkMapRenderer;

    private Mesh _terrainMesh;
    private Mesh _waterMesh;
    private Mesh _riverMesh;
    private List<HexCellData> _cellsInGenerateOrder;
    private bool _isSubscribed;
    private bool _isLogisticsSubscribed;
    private GameObject _landFormRoot;
    private GameObject _resourceRoot;
    private FogEnvironmentSelectiveEffect _environmentFogEffect;

    // 【动态地图-阶段二】运行时重建复用宿主与材质缓存（§六-2）：
    // 承载 GO 持久复用（mesh.Clear() 后重建），材质按"基础材质组合键"缓存共享，杜绝重复重建泄漏。
    private GameObject _riverHost;
    private GameObject _lakeHost;
    private GameObject _gridHost;
    private readonly Dictionary<(Material, Material), Material> _rectMaterialCache = new Dictionary<(Material, Material), Material>();
    private readonly Dictionary<(Material, Material, Material), Material> _triMaterialCache = new Dictionary<(Material, Material, Material), Material>();
    private Material _terrainBaseMaterial0;
    private Material _terrainBaseMaterial1;
    private Material _terrainBaseMaterial2;

    // 【动态地图-阶段一】无状态网格构建：只读视图 + 全图实心/湖海/矩形过渡顶点注册表。
    // 生成器不再把渲染缓存写回 HexCellData；各构建循环把格级产物收集到这些字典供跨格依赖读取。
    private IReadOnlyMapView _view;
    private readonly Dictionary<int, Vector3[]> _solidVertices = new Dictionary<int, Vector3[]>();
    private readonly Dictionary<int, Vector3[]> _lakeOrSeaVertices = new Dictionary<int, Vector3[]>();
    private readonly Dictionary<(int order, Enums.HexDirection dir), List<Vector3>> _rectVerticesByCell = new Dictionary<(int, Enums.HexDirection), List<Vector3>>();

    private Color[] _cachedTerrainColors;
    private Color[] _cachedWaterColors;
    private Color[] _cachedRiverColors;

    /// <summary>【动态地图-阶段三】WholeMap 后端不支持脏 Chunk 重建（由 ChunkMapRenderer 提供）。</summary>
    public bool SupportsChunkedRebuild => false;

    /// <summary>【动态地图-阶段四】WholeMap 后端不支持 Shader 顶点动画（由 ChunkMapRenderer 提供）。</summary>
    public bool SupportsAnimatedTransition => false;

    /// <summary>【动态地图-阶段三】WholeMap 后端不提供 Chunk staging（Chunked 后端专用）。</summary>
    public PreparedChunkGeometry PrepareChunkGeometry(System.Collections.Generic.IReadOnlyCollection<HexCellData> changedCells)
    {
        throw new System.NotSupportedException("MapRenderer(WholeMap) 不支持 PrepareChunkGeometry；请配置 MapRenderMode.Chunked 使用 ChunkMapRenderer。");
    }

    /// <summary>【动态地图-阶段三】WholeMap 后端不消费 Chunk staging。</summary>
    public void CommitChunkGeometry(PreparedChunkGeometry geometry)
    {
        throw new System.NotSupportedException("MapRenderer(WholeMap) 不支持 CommitChunkGeometry；请配置 MapRenderMode.Chunked 使用 ChunkMapRenderer。");
    }

    /// <summary>【动态地图-阶段四】WholeMap 后端不提供动画几何构建（Chunked 后端专用）。</summary>
    public PreparedChunkGeometry PrepareAnimatedChunkGeometry(
        System.Collections.Generic.IReadOnlyCollection<HexCellData> changedCells,
        System.Collections.Generic.IReadOnlyDictionary<int, float> oldHeights,
        System.Collections.Generic.IReadOnlyDictionary<int, float> staggerDelays)
    {
        throw new System.NotSupportedException("MapRenderer(WholeMap) 不支持动画几何构建；请配置 MapRenderMode.Chunked 使用 ChunkMapRenderer。");
    }

    /// <summary>【动态地图-阶段四】WholeMap 后端不消费动画 staging。</summary>
    public void CommitAnimatedChunkGeometry(PreparedChunkGeometry geometry)
    {
        throw new System.NotSupportedException("MapRenderer(WholeMap) 不支持动画 staging 提交；请配置 MapRenderMode.Chunked 使用 ChunkMapRenderer。");
    }

    /// <summary>【动态地图-阶段四】WholeMap 后端不支持逐 Chunk 动画进度驱动。</summary>
    public void SetChunkAnimationProgress(ChunkIndex index, float progress)
    {
        throw new System.NotSupportedException("MapRenderer(WholeMap) 不支持动画进度驱动；请配置 MapRenderMode.Chunked 使用 ChunkMapRenderer。");
    }

    /// <summary>【动态地图-阶段四】WholeMap 后端不支持动画收尾。</summary>
    public void FinalizeChunkAnimation(ChunkIndex index)
    {
        throw new System.NotSupportedException("MapRenderer(WholeMap) 不支持动画收尾；请配置 MapRenderMode.Chunked 使用 ChunkMapRenderer。");
    }

    /// <summary>【动态地图-阶段五】WholeMap 后端不提供脏 Chunk 索引（分帧提交专用）。</summary>
    public System.Collections.Generic.IReadOnlyList<ChunkIndex> ComputeDirtyChunkIndices(
        System.Collections.Generic.IReadOnlyCollection<HexCellData> changedCells)
    {
        throw new System.NotSupportedException("MapRenderer(WholeMap) 不支持分帧提交（ComputeDirtyChunkIndices）；请配置 MapRenderMode.Chunked 使用 ChunkMapRenderer。");
    }

    /// <summary>【动态地图-阶段五】WholeMap 后端不提供按 Chunk 切片构建。</summary>
    public PreparedChunkGeometry PrepareChunkGeometrySlice(
        System.Collections.Generic.IReadOnlyList<ChunkIndex> chunkIndices)
    {
        throw new System.NotSupportedException("MapRenderer(WholeMap) 不支持分帧提交（PrepareChunkGeometrySlice）；请配置 MapRenderMode.Chunked 使用 ChunkMapRenderer。");
    }

    // 方案B：世界对齐的探索遮罩贴图（像素锯齿边界用）。R 通道 0/1，双线性采样得到边界渐变带。
    private Texture2D _fogMaskTex;
    private Color32[] _fogMaskData;
    private Vector2 _fogMaskOrigin;   // 世界 (minX, minZ)，与 _FogMapOrigin 一致
    private Vector2 _fogMaskSize;     // 世界 (sizeX, sizeZ)，与 _FogMapSize 一致
    private int _fogMaskW, _fogMaskH;

    // 【迷雾过渡】驱动 FogAlpha 逐帧过渡，实现逐渐消散/重聚。
    private readonly FogTransitionManager _fogTransition = new FogTransitionManager();
    // 限频刷新：过渡进行中每隔固定时间才重建纹理/顶点色（人眼几乎无感，省性能）。
    private float _fogRefreshTimer;
    private const float FogRefreshInterval = 1f / 20f; // 20fps 刷新一次视觉
    // 首次（开局）用快照立即到位，之后才走过渡动画。
    private bool _fogInitialized;


    // ��Ⱦ��ͼ�Ӿ�����
    public void MapRender()
    {
        // 1. ��ȡ��������������
        Vector3[] hexVertices = _mapDataService.GetHexVertices();

        // 1.5 无状态构建视图与格级注册表（动态地图-阶段一）
        _view = new MapDataReadOnlyView(_mapDataService);
        _solidVertices.Clear();
        _lakeOrSeaVertices.Clear();
        _rectVerticesByCell.Clear();

        // 2. 设置迷雾全局 Shader 属性
        SetupFogGlobalShaderProperties(hexVertices);

        // 3. 【动态地图-阶段三】渲染后端分派：Chunked = ChunkMapRenderer 分块构建网格（含地形/河流/湖海）
        bool useChunked = _config != null &&
                          _config.enableExperimentalChunkRenderer &&
                          _config.mapRenderMode == Enums.MapRenderMode.Chunked &&
                          _chunkMapRenderer != null;
        if (_config != null && _config.mapRenderMode == Enums.MapRenderMode.Chunked && !useChunked)
        {
            Debug.LogWarning("[MapRenderer] Chunked 实验后端未通过安全开关，已强制回退 WholeMap。");
        }
        if (useChunked)
        {
            Debug.Log($"[MapRenderer] 地图构建：Chunked（实验后端，mapRenderMode=Chunked + enableExperimentalChunkRenderer=true）");
            _chunkMapRenderer.ChunkMapRender(hexVertices);
        }
        else
        {
            Debug.Log($"[MapRenderer] 地图构建：WholeMap（mapRenderMode={(_config != null ? _config.mapRenderMode.ToString() : "null")}，useChunked={useChunked}）");
            //����ͼMesh����
            MainMapMeshCreat(hexVertices);
            //����Mesh����
            RiverMeshCreat(hexVertices);
            //����Mesh����
            LakeOrSeaMeshCreat(hexVertices);
            //����Mesh����
            GridMeshCreat(hexVertices);

            // ���� IMapDataService ������ʱ���ݣ�verticesList��mesh��gridGameObject��
            // ��Щ������Ĵ��������б���ֵ�� mapGenerator
            if (_mapDataService != null && mapGenerator != null)
            {
                _mapDataService.UpdateRuntimeData(mapGenerator.verticesList, mapGenerator.mesh, mapGenerator.gridGameObject);
            }
        }

        // 4. ʵ������ò����Դģ��
        InstantiateLandForms(hexVertices);
        InstantiateResources(hexVertices);
        SetupEnvironmentFogEffect();

        // 5. ���� - ʹ���¼�ϵͳ�����ɸ� FogManager ʵ�������ĳ�ʼ��
        _mapVisualEvent.FogInit();

        // 6. 探索费用标签渲染器
        if (CostLabelPrefab != null)
        {
            var labelGo = new GameObject("CostLabelRenderer");
            labelGo.transform.SetParent(transform);
            var labelRenderer = labelGo.AddComponent<CostLabelRenderer>();
            labelRenderer.Initialize(_mapDataService, _goldWallet, CostLabelPrefab, _targetUICanvas, _explorationService, _mapVisualEvent, _logisticsService);
        }
    }

    // ����无状态构建上下文：把全图实心/湖海/矩形过渡注册表打包给生成器方法（动态地图-阶段一）。
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

    //����ͼMesh����
    private void MainMapMeshCreat(Vector3[] hexVertices)
    {
        TerrainGeometry geometry = BuildTerrainGeometry(hexVertices);
        ApplyTerrainGeometry(geometry, create: true);
    }

    /// <summary>【动态地图-阶段二】构建地形网格数据（纯数据，不触碰渲染组件；首次与运行时重建共用）。</summary>
    private TerrainGeometry BuildTerrainGeometry(Vector3[] hexVertices)
    {
        _genericRectangleMeshes.Clear();
        int cellCount = hexVertices.Length;
        List<Vector3> verticesList = new List<Vector3>(cellCount * 44);
        List<Vector2> uvList = new List<Vector2>(cellCount * 44);
        List<Color> allColors = new List<Color>(cellCount * 44);
        _cellsInGenerateOrder = new List<HexCellData>(cellCount);
        var rectangleVertexRanges = new List<(int start, int count)>();
        var triangleVertexRanges = new List<(int start, int count)>();

        //�ߵػ���˳��
        List<int> highDrawOrderList = new List<int>();
        //ƽ�ػ���˳��
        List<int> flatDrawOrderList = new List<int>();
        //���׻���˳��
        List<int> seafloorDrawOrderList = new List<int>();
        //����˳��
        //�ӻ���˳�б�
        List<List<int>> subList = new List<List<int>>() { highDrawOrderList, flatDrawOrderList, seafloorDrawOrderList };

        //��ֵ����
        //ʵ������

        if(hexVertices == null)
        {
            Debug.Log("��������������Ϊnull��");
            return null;
        }

        if (hexVertices.Length == 0)
        {
            Debug.Log("û�л�ȡ�����������꣡");
            return null;
        }

        for (int j = 0; j < hexVertices.Length; j++)
        {
            //根据六边形顶点坐标取出hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            //记录按生成顺序的地块列表
            _cellsInGenerateOrder.Add(hexCellData);
            //清空过渡区域顶点范围
            hexCellData.MeshTransitionVertexRanges.Clear();
            //记录实心区域首顶点索引
            hexCellData.MeshSolidAreaVertexStartIndex = verticesList.Count;
            //绘制顺序偏移
            int IndexOffset = verticesList.Count;

            //生成vertices（无状态：不再写回 HexCellData 渲染缓存）
            SolidAreaMeshData solid = _meshGenerator.BuildSolidArea(hexCellData, _view);
            _solidVertices[hexCellData.GenerateOrder] = solid.Vertices;
            // 逻辑中心同步（原 GetSolidAreaVertices 内部赋值点，保持行为一致）
            hexCellData.RealCenterWorldCoordinate = solid.Center;
            verticesList.AddRange(solid.Vertices);
            //添加顶点色
            Color cellColor = FogVertexColor(hexCellData);
            for (int c = 0; c < 44; c++)
                allColors.Add(cellColor);

            //UV
            uvList.AddRange(_meshGenerator.BuildSolidAreaUV(hexCellData));

            //�������˳��
            List<Enums.HexDirection> d;
            int index = MainMeshSolidAreaDrawOrderFunction(hexCellData, out d);
            List<int> ints = new List<int>();
            switch (index)
            {
                case 1:
                    ints = _meshGenerator.BuildSolidAreaDrawOrder1(hexCellData);
                    break;
                case 2:
                    ints = _meshGenerator.BuildSolidAreaDrawOrder2(hexCellData, d[0]);
                    break;
                case 3:
                    ints = _meshGenerator.BuildSolidAreaDrawOrder3(hexCellData, d[0], d[1]);
                    break;
            }

            MainMeshDrawOrderElementAddRule(ref hexCellData, ints, ref subList, IndexOffset);
        }
        // 矩形过渡
        var rectGroups = new Dictionary<(Material, Material), List<int>>();
        for (int j = 0; j < hexVertices.Length; j++)
        {
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            Color cellColor = FogVertexColor(hexCellData);
            int IndexOffset = verticesList.Count;
            Enums.HexDirection[] hexDirections = new Enums.HexDirection[3] { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE };
            for (int i = 0; i < hexDirections.Length; i++)
            {
                IndexOffset = verticesList.Count;
                if (_mapDataService.GetNeighbor(hexCellData, hexDirections[i]) == null) continue;
                bool isSlope = true, isRiver = false;
                MainMeshRectFunction(hexCellData, hexDirections[i], out isSlope, out isRiver);
                List<int> ints = new List<int>();
                CellBuildContext ctx = MakeBuildContext(hexCellData);

                RectangleTransitionMeshData rectangle = GetGenericRectangleMesh(ctx, hexDirections[i]);

                if (isRiver)
                {
                    if (isSlope)
                    {
                        int preCount = verticesList.Count;
                        List<Vector3> rectVerts = _meshGenerator.BuildRectVertices(ctx, hexDirections[i]);
                        verticesList.AddRange(rectVerts);
                        uvList.AddRange(_meshGenerator.BuildRectUV(ctx, hexDirections[i]));
                        int addedCount = verticesList.Count - preCount;
                        for (int c = 0; c < addedCount; c++) allColors.Add(cellColor);
                        hexCellData.MeshTransitionVertexRanges.Add((preCount, addedCount));
                        OtherMeshDrawOrderElementAddRule(ref hexCellData, _meshGenerator.BuildRectSlopeRiverDrawOrder(ctx, hexDirections[i]), ref ints, IndexOffset);
                        _rectVerticesByCell[(hexCellData.GenerateOrder, hexDirections[i])] = rectVerts;
                    }
                    else
                    {
                        int preCount = verticesList.Count;
                        List<Vector3> rectVerts = _meshGenerator.BuildRectStepVertices(ctx, hexDirections[i]);
                        verticesList.AddRange(rectVerts);
                        uvList.AddRange(_meshGenerator.BuildRectStepUV(ctx, rectVerts));
                        int addedCount = verticesList.Count - preCount;
                        for (int c = 0; c < addedCount; c++) allColors.Add(cellColor);
                        hexCellData.MeshTransitionVertexRanges.Add((preCount, addedCount));
                        OtherMeshDrawOrderElementAddRule(ref hexCellData, _meshGenerator.BuildRectStepRiverDrawOrder(ctx, rectVerts), ref ints, IndexOffset);
                        _rectVerticesByCell[(hexCellData.GenerateOrder, hexDirections[i])] = rectVerts;
                    }
                }
                else
                {
                    // 非河流矩形不产生旧式矩形顶点组：记录空列表，保持与旧行为一致（TriStep3/4 依赖）
                    _rectVerticesByCell[(hexCellData.GenerateOrder, hexDirections[i])] = new List<Vector3>();
                    int preCount = verticesList.Count;
                    RectangleTransitionMeshData usedRect = RectFlat(_config.shadingStyle)
                        ? RectangleTransitionMesh.ToFlatShaded(rectangle)
                        : rectangle;
                    verticesList.AddRange(usedRect.Vertices);
                    uvList.AddRange(usedRect.UVs);
                    int addedCount = verticesList.Count - preCount;
                    for (int c = 0; c < addedCount; c++) allColors.Add(cellColor);
                    hexCellData.MeshTransitionVertexRanges.Add((preCount, addedCount));
                    OtherMeshDrawOrderElementAddRule(ref hexCellData, usedRect.Indices, ref ints, IndexOffset);
                }

                int rectangleVertexCount = verticesList.Count - IndexOffset;
                if (rectangleVertexCount > 0)
                    rectangleVertexRanges.Add((IndexOffset, rectangleVertexCount));

                Material matA = HexController.SetHexMaterial(hexCellData, _config.mapMaterial);
                Material matB = HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, hexDirections[i]), _config.mapMaterial);
                var key = (matA, matB);
                if (!rectGroups.ContainsKey(key))
                    rectGroups[key] = new List<int>();
                rectGroups[key].AddRange(ints);
            }
        }
        var mergedRectIndices = new List<List<int>>(rectGroups.Values);
        var mergedMaterialAs = rectGroups.Keys.Select(k => k.Item1).ToList();
        var mergedMaterialBs = rectGroups.Keys.Select(k => k.Item2).ToList();

        // 三角过渡
        var triGroups = new Dictionary<(Material, Material, Material), List<int>>();
        for (int j = 0; j < hexVertices.Length; j++)
        {
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            Color cellColor = FogVertexColor(hexCellData);
            int IndexOffset = verticesList.Count;

            Enums.HexDirection[][] h = new Enums.HexDirection[2][]
            {
                new[] { Enums.HexDirection.NE, Enums.HexDirection.E },
                new[] { Enums.HexDirection.E, Enums.HexDirection.SE }
            };

            for (int i = 0; i < 2; i++)
            {
                if (_mapDataService.GetNeighbor(hexCellData, h[i][0]) == null || _mapDataService.GetNeighbor(hexCellData, h[i][1]) == null) continue;
                IndexOffset = verticesList.Count;
                List<int> ints = new List<int>();
                int preCount = verticesList.Count;
                CellBuildContext ctx = MakeBuildContext(hexCellData);
                TriangleTransitionMeshData triangle = GetGenericTriangleMesh(ctx, h[i][0], h[i][1]);
                verticesList.AddRange(triangle.Vertices);
                uvList.AddRange(triangle.UVs);
                int addedCount = verticesList.Count - preCount;
                for (int c = 0; c < addedCount; c++) allColors.Add(cellColor);
                hexCellData.MeshTransitionVertexRanges.Add((preCount, addedCount));
                triangleVertexRanges.Add((preCount, addedCount));
                OtherMeshDrawOrderElementAddRule(ref hexCellData, triangle.Indices, ref ints, IndexOffset);

                Material matA = HexController.SetHexMaterial(hexCellData, _config.mapMaterial);
                Material matB = HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, h[i][0]), _config.mapMaterial);
                Material matC = HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, h[i][1]), _config.mapMaterial);
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

        // arrArawOrder：实心区 3 个 + 矩形过渡 N 个 + 三角过渡 M 个
        int[][] arrArawOrder = new int[3 + mergedRectIndices.Count + mergedTriIndices.Count][];
        arrArawOrder[0] = subList[2].ToArray();
        arrArawOrder[1] = subList[1].ToArray();
        arrArawOrder[2] = subList[0].ToArray();

        int offset = 3;
        for (int i = 0; i < mergedRectIndices.Count; i++)
            arrArawOrder[offset++] = mergedRectIndices[i].ToArray();
        for (int i = 0; i < mergedTriIndices.Count; i++)
            arrArawOrder[offset++] = mergedTriIndices[i].ToArray();

        Color[] terrainColorArray = allColors.ToArray();
        _cachedTerrainColors = terrainColorArray;

        // 几何自检（诊断，定位"错点/缺面"问题）：顶点数/首格中心/NaN 统计/过渡组数
        if (verticesList.Count > 0)
        {
            int nanCount = 0;
            for (int i = 0; i < verticesList.Count; i++)
            {
                if (float.IsNaN(verticesList[i].x) || float.IsNaN(verticesList[i].y) || float.IsNaN(verticesList[i].z) ||
                    float.IsInfinity(verticesList[i].x) || float.IsInfinity(verticesList[i].y) || float.IsInfinity(verticesList[i].z))
                {
                    nanCount++;
                    if (nanCount <= 3)
                        Debug.LogWarning($"[MapRenderer] 几何自检：顶点[{i}] 非法 = {verticesList[i]}");
                }
            }
            Debug.Log($"[MapRenderer] 几何自检：格子数={cellCount} 顶点={verticesList.Count} submesh={arrArawOrder.Length} " +
                      $"首格中心={verticesList[0]} 矩形过渡={mergedRectIndices.Count}组 三角过渡={mergedTriIndices.Count}组 NaN/Inf={nanCount}");
        }

        return new TerrainGeometry
        {
            Vertices = verticesList.ToArray(),
            UVs = uvList.ToArray(),
            Colors = terrainColorArray,
            SubMeshIndices = arrArawOrder,
            BaseMaterials = _config.mapMaterial,
            RectAs = mergedMaterialAs.ToArray(),
            RectBs = mergedMaterialBs.ToArray(),
            TriAs = mergedMaterialAsTri.ToArray(),
            TriBs = mergedMaterialBsTri.ToArray(),
            TriCs = mergedMaterialCsTri.ToArray(),
            RectangleRanges = rectangleVertexRanges,
            TriangleRanges = triangleVertexRanges,
            VerticesList = verticesList
        };
    }

    /// <summary>【动态地图-阶段二】把地形网格数据应用到承载 Mesh（create=false 时复用既有 Mesh，Clear 后重建）。</summary>
    private void ApplyTerrainGeometry(TerrainGeometry geometry, bool create)
    {
        Mesh mainMesh;
        if (create || _terrainMesh == null)
        {
            mainMesh = CreateTerrainMesh(geometry, _mapDataService.MapGameObject);
        }
        else
        {
            mainMesh = RefillTerrainMesh(geometry, _terrainMesh);
        }

        // 将生成的顶点列表和 Mesh 传给 MapGenerator（供全局使用）
        if (mapGenerator != null)
        {
            mapGenerator.verticesList = geometry.VerticesList;
            mapGenerator.mesh = mainMesh;
        }
        PostProcessNormals(mainMesh, _config.shadingStyle, geometry.RectangleRanges, geometry.TriangleRanges);
        if (create)
            Debug.Log($"MapRenderer: terrain shading style = {_config.shadingStyle}");
        _terrainMesh = mainMesh;
    }

    private Mesh CreateTerrainMesh(TerrainGeometry geometry, GameObject host)
    {
        Mesh mesh = new Mesh();
        FillTerrainMeshData(mesh, geometry);

        MeshFilter meshFilter = host.GetComponent<MeshFilter>();
        if (meshFilter == null) meshFilter = host.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = host.GetComponent<MeshRenderer>();
        if (meshRenderer == null) meshRenderer = host.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = ResolveTerrainMaterials(geometry);
        meshFilter.sharedMesh = mesh;

        MeshCollider meshCollider = host.GetComponent<MeshCollider>();
        if (meshCollider == null) meshCollider = host.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = mesh;
        return mesh;
    }

    private Mesh RefillTerrainMesh(TerrainGeometry geometry, Mesh mesh)
    {
        FillTerrainMeshData(mesh, geometry);
        MeshRenderer meshRenderer = _mapDataService.MapGameObject.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
            meshRenderer.sharedMaterials = ResolveTerrainMaterials(geometry);
        return mesh;
    }

    private void FillTerrainMeshData(Mesh mesh, TerrainGeometry geometry)
    {
        mesh.Clear();
        mesh.indexFormat = geometry.Vertices.Length > ushort.MaxValue
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = geometry.Vertices;
        mesh.uv = geometry.UVs;
        mesh.colors = geometry.Colors;
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

    /// <summary>按"基础材质组合键"缓存共享运行时材质（基础 3 材质只建一次；矩形/三角过渡按组合缓存）。</summary>
    private Material[] ResolveTerrainMaterials(TerrainGeometry geometry)
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

        // 矩形过渡：与 MapController.CreatMesh 参数一致（A=Bs=neighbor 亮端，B=As=self 暗端）
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

        // 三角过渡：A=self, B=边1邻居, C=边2邻居
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

    //����Mesh����
    private void RiverMeshCreat(Vector3[] hexVertices)
    {
        RiverGeometry geometry = BuildRiverGeometry(hexVertices);
        ApplyRiverGeometry(geometry, create: true);
    }

    /// <summary>【动态地图-阶段二】构建河流网格数据（纯数据；无河流时返回 null）。</summary>
    private RiverGeometry BuildRiverGeometry(Vector3[] hexVertices)
    {
        int cellCount = hexVertices.Length;
        List<Vector3> verticesRiverWater = new List<Vector3>(cellCount * 44);
        List<Vector2> uvRiverWater = new List<Vector2>(cellCount * 44);
        List<Color> riverColors = new List<Color>(cellCount * 44);
        List<int> drawOrderRiverWater = new List<int>(cellCount * 100);

        // 本地函数：某地块刚向 verticesRiverWater 追加一段顶点后调用，补齐顶点色并记录范围。
        void RecordRiver(HexCellData cell, int preCount)
        {
            int added = verticesRiverWater.Count - preCount;
            if (added <= 0) return;
            Color col = FogVertexColor(cell);
            for (int c = 0; c < added; c++) riverColors.Add(col);
            cell.MeshRiverVertexRanges.Add((preCount, added));
        }

        //������ֵ
        //ʵ������        
        for (int j = 0; j < hexVertices.Length; j++)
        {
            List<int> ints = new List<int>();//��ʱ���м����
            //���������������ȡhexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            //����˳��ƫ��
            int IndexOffset = verticesRiverWater.Count;

            if (RiverMeshSolidAreaDrawOrderFunction(hexCellData) == null) continue;

            //��������
            hexCellData.MeshRiverVertexRanges.Clear();
            CellBuildContext ctx = MakeBuildContext(hexCellData);
            Vector3[] riverVerts = _meshGenerator.BuildRiverVertices(ctx);
            verticesRiverWater.AddRange(riverVerts);
            //�������˳��
            List<int> l = new List<int>();
            l = RiverMeshSolidAreaDrawOrderFunction(hexCellData);
            ints.Clear();
            OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
            drawOrderRiverWater.AddRange(ints);
            //UV
            uvRiverWater.AddRange(_meshGenerator.BuildRiverUV(ctx, l, riverVerts.Length));
            RecordRiver(hexCellData, IndexOffset);
        }
        //���ι�������
        for (int j = 0; j < hexVertices.Length; j++)
        {
            List<int> ints = new List<int>();//��ʱ���м����
            //���������������ȡhexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            //����˳��ƫ��
            int IndexOffset = verticesRiverWater.Count;

            //�õؿ����ι�������
            //�������� - ��ˮ�����»����
            CellBuildContext ctx = MakeBuildContext(hexCellData);
            verticesRiverWater.AddRange(_meshGenerator.BuildOutgoingRiverVertices(ctx));

            //�������˳��
            List<int> l = new List<int>();
            l.AddRange(RiverMeshDownstreamDrawOrderFunction());
            OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
            drawOrderRiverWater.AddRange(ints);
            //UV
            uvRiverWater.AddRange(_meshGenerator.BuildOutgoingRiverSlopUV());
            RecordRiver(hexCellData, IndexOffset);
        }

        //����CreatMesh()
        if (drawOrderRiverWater.Count % 3 == 0 && drawOrderRiverWater.Count != 0)
        {

            Color[] riverColorArray = riverColors.ToArray();
            _cachedRiverColors = riverColorArray;
            return new RiverGeometry
            {
                Vertices = verticesRiverWater.ToArray(),
                UVs = uvRiverWater.ToArray(),
                Indices = drawOrderRiverWater.ToArray(),
                Colors = riverColorArray
            };
        }
        return null;
    }

    /// <summary>【动态地图-阶段二】应用河流网格：create=false 复用既有宿主 Mesh（Clear 后重建）；无河流时销毁旧宿主。</summary>
    private void ApplyRiverGeometry(RiverGeometry geometry, bool create)
    {
        if (geometry == null)
        {
            if (!create && _riverHost != null)
            {
                Object.Destroy(_riverHost);
                _riverHost = null;
            }
            _riverMesh = null;
            return;
        }

        if (create || _riverHost == null)
        {
            _riverHost = new GameObject("RiverWater");
            _riverMesh = MapController.CreatMesh(geometry.Vertices, geometry.UVs, geometry.Indices, _riverHost, _config.riverMaterial, geometry.Colors);
            return;
        }

        RefillRiverMesh(geometry, _riverMesh);
        MeshRenderer meshRenderer = _riverHost.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
            meshRenderer.sharedMaterials = _config.riverMaterial;
    }

    private void RefillRiverMesh(RiverGeometry geometry, Mesh mesh)
    {
        mesh.Clear();
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

    //����Mesh����
    private void LakeOrSeaMeshCreat(Vector3[] hexVertices)
    {
        WaterGeometry geometry = BuildWaterGeometry(hexVertices);
        ApplyWaterGeometry(geometry, create: true);
    }

    /// <summary>【动态地图-阶段二】构建湖海网格数据（纯数据；无水面时返回 null）。</summary>
    private WaterGeometry BuildWaterGeometry(Vector3[] hexVertices)
    {
        int cellCount = hexVertices.Length;
        List<Vector3> verticesLakeOrSea = new List<Vector3>(cellCount * 44);
        List<Vector2> uvLakeOrSea = new List<Vector2>(cellCount * 44);
        List<Color> lakeColors = new List<Color>(cellCount * 44);
        List<int> drawOrderLakeOrSea = new List<int>(cellCount * 100);
        List<int> drawOrderCoast = new List<int>(cellCount * 50);

        // 本地函数：某地块刚向 verticesLakeOrSea 追加了一段水面顶点后调用，
        // 补齐对应的顶点色并记录顶点范围（供运行时按探索状态更新）。
        // preCount 传入追加前的顶点数（各处一般已有 IndexOffset 记录了该值）。
        void RecordWater(HexCellData cell, int preCount)
        {
            int added = verticesLakeOrSea.Count - preCount;
            if (added <= 0) return;
            Color col = FogVertexColor(cell);
            for (int c = 0; c < added; c++) lakeColors.Add(col);
            cell.MeshWaterVertexRanges.Add((preCount, added));
        }

        // 确定哪些是"湖或海"地块并写入水格的水面高度
        for (int j = 0; j < hexVertices.Length; j++)
        {
            //遍历所有地块坐标取出hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            //如果不是"湖或海"就跳过
            if (!isLakeOrSea(hexCellData)) { continue; }
            hexCellData.HexType = Enums.HexType.LakeOrSea;
            hexCellData.isCoast = true;
            hexCellData.waterLevel = _config.seaLevel;
        }

        //ʵ������
        for (int j = 0; j < hexVertices.Length; j++)
        {
            List<int> ints = new List<int>();//��ʱ���м����
            //���������������ȡhexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            int IndexOffset = verticesLakeOrSea.Count;
            //�����ǡ����򺣡�������
            if (hexCellData.HexType != Enums.HexType.LakeOrSea) { continue; }
            //Ѱ�Һ����ظ�
            bool isCoast = (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null) && (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).HexType != Enums.HexType.LakeOrSea) ||
                           (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null) && (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).HexType != Enums.HexType.LakeOrSea) ||
                           (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null) && (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).HexType != Enums.HexType.LakeOrSea) ||
                           (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SW) != null) && (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SW).HexType != Enums.HexType.LakeOrSea) ||
                           (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.W) != null) && (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.W).HexType != Enums.HexType.LakeOrSea) ||
                           (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NW) != null) && (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NW).HexType != Enums.HexType.LakeOrSea);

            //��������
            hexCellData.MeshWaterVertexRanges.Clear();
            CellBuildContext ctx = MakeBuildContext(hexCellData);
            Vector3[] lakeVerts = _meshGenerator.BuildLakeOrSeaVertices(ctx);
            _lakeOrSeaVertices[hexCellData.GenerateOrder] = lakeVerts;
            verticesLakeOrSea.AddRange(lakeVerts);
            //UV
            uvLakeOrSea.AddRange(_meshGenerator.BuildLakeOrSeaUV());
            RecordWater(hexCellData, IndexOffset);

            //�������˳��
            List<int> l = new List<int>();
            l.AddRange(LakeOrSeaMeshSolidAreaDrawOrderFunction());
            OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
            drawOrderLakeOrSea.AddRange(ints);
        }
        //���ι�������
        for (int j = 0; j < hexVertices.Length; j++)
        {
            List<int> ints = new List<int>();//��ʱ���м����
            //���������������ȡhexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            int IndexOffset = verticesLakeOrSea.Count;
            //�����ǡ����򺣡�������
            if (hexCellData.HexType != Enums.HexType.LakeOrSea) { continue; }

            Enums.HexDirection[] hexDirections = new Enums.HexDirection[3] { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE };

            for (int i = 0; i < hexDirections.Length; i++)
            {
                HexCellData neighbor = _mapDataService.GetNeighbor(hexCellData, hexDirections[i]);
                if (neighbor != null && _lakeOrSeaVertices.ContainsKey(neighbor.GenerateOrder))
                {
                    //��������
                    IndexOffset = verticesLakeOrSea.Count;
                    CellBuildContext ctx = MakeBuildContext(hexCellData);
                    verticesLakeOrSea.AddRange(_meshGenerator.BuildLakeOrSeaRectVertices(ctx, hexDirections[i]));
                    //UV
                    uvLakeOrSea.AddRange(_meshGenerator.BuildLakeOrSeaRectUV(hexDirections[i]));
                    RecordWater(hexCellData, IndexOffset);
                    //����˳��          
                    List<int> l = new List<int>();
                    l.AddRange(LakeOrSeaMeshRectDrawOrderFunction(hexDirections[i]));
                    ints.Clear();
                    OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
                    drawOrderLakeOrSea.AddRange(ints);
                }
            }
        }
        //���ǹ������� 
        for (int j = 0; j < hexVertices.Length; j++)
        {
            List<int> ints = new List<int>();//��ʱ���м����
            //���������������ȡhexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            int IndexOffset = verticesLakeOrSea.Count;
            //�����ǡ����򺣡�������
            if (hexCellData.HexType != Enums.HexType.LakeOrSea) { continue; }

            Enums.HexDirection[][] h = new Enums.HexDirection[2][]
            {
                new[] { Enums.HexDirection.NE, Enums.HexDirection.E },
                new[] { Enums.HexDirection.E, Enums.HexDirection.SE }
            };

            for (int i = 0; i < h.Length; i++)
            {
                HexCellData neighborA = _mapDataService.GetNeighbor(hexCellData, h[i][0]);
                HexCellData neighborB = _mapDataService.GetNeighbor(hexCellData, h[i][1]);
                if (neighborA != null &&
                    neighborB != null &&
                    _lakeOrSeaVertices.ContainsKey(neighborA.GenerateOrder) &&
                    _lakeOrSeaVertices.ContainsKey(neighborB.GenerateOrder))
                {
                    //��������
                    IndexOffset = verticesLakeOrSea.Count;
                    CellBuildContext ctx = MakeBuildContext(hexCellData);
                    verticesLakeOrSea.AddRange(_meshGenerator.BuildLakeOrSeaTriVertices(ctx, h[i][0], h[i][1]));
                    //UV
                    uvLakeOrSea.AddRange(_meshGenerator.BuildLakeOrSeaTriUV(h[i][0], h[i][1]));
                    RecordWater(hexCellData, IndexOffset);

                    //���ǻ���˳��
                    List<int> l = new List<int>();
                    l.AddRange(LakeOrSeaMeshTriDrawOrderFunction(h[i][0], h[i][1]));
                    ints.Clear();
                    OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
                    drawOrderLakeOrSea.AddRange(ints);
                }
            }

        }
        //�������� - ����
        for (int j = 0; j < hexVertices.Length; j++)
        {
            List<int> ints = new List<int>();//��ʱ���м����
            //���������������ȡhexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            int IndexOffset = verticesLakeOrSea.Count;
            //�����ǡ����򺣡�������
            if (hexCellData.HexType != Enums.HexType.LakeOrSea) { continue; }

            //�����ķ���
            List<Enums.HexDirection> coastDirections = new List<Enums.HexDirection>();
            Enums.HexDirection[] hexDirections = { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE, Enums.HexDirection.SW, Enums.HexDirection.W, Enums.HexDirection.NW };
            foreach (Enums.HexDirection h in hexDirections)
            {
                if (_mapDataService.GetNeighbor(hexCellData, h) != null && _mapDataService.GetNeighbor(hexCellData, h).HexType != Enums.HexType.LakeOrSea)
                {
                    coastDirections.Add(h);
                }
            }

            ///����
            //��������
            CellBuildContext ctx = MakeBuildContext(hexCellData);
            List<Vector3> v = new List<Vector3>();
            foreach (Enums.HexDirection h in coastDirections)
            {
                v.AddRange(_meshGenerator.BuildCoastRectVertices(ctx, h));
            }
            verticesLakeOrSea.AddRange(v);
            //UV
            uvLakeOrSea.AddRange(_meshGenerator.BuildCoastRectUV(v.ToArray()));
            RecordWater(hexCellData, IndexOffset);
            //���ǻ���˳��    
            List<int> l = new List<int>();
            l.AddRange(CoastMeshRectDrawOrderFunction(v.ToArray()));
            ints.Clear();
            OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
            drawOrderCoast.AddRange(ints);
        }
        //�������� - ����
        for (int j = 0; j < hexVertices.Length; j++)
        {
            List<int> ints = new List<int>();//��ʱ���м����
            //���������������ȡhexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            int IndexOffset = verticesLakeOrSea.Count;
            //�����ǡ����򺣡�������
            if (hexCellData.HexType != Enums.HexType.LakeOrSea) { continue; }

            //�����ķ���
            List<Enums.HexDirection> coastDirections = new List<Enums.HexDirection>();
            Enums.HexDirection[] hexDirections = { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE, Enums.HexDirection.SW, Enums.HexDirection.W, Enums.HexDirection.NW };
            foreach (Enums.HexDirection h in hexDirections)
            {
                if (_mapDataService.GetNeighbor(hexCellData, h) != null && _mapDataService.GetNeighbor(hexCellData, h).HexType != Enums.HexType.LakeOrSea)
                {
                    coastDirections.Add(h);
                }
            }

            //����
            CellBuildContext ctx = MakeBuildContext(hexCellData);
            List<Vector3> v = new List<Vector3>();
            foreach (Enums.HexDirection h in coastDirections)
            {
                v.AddRange(_meshGenerator.BuildCoastTriVertices(ctx, h));
            }
            verticesLakeOrSea.AddRange(v);
            //UV
            uvLakeOrSea.AddRange(_meshGenerator.BuildCoastTriUV(v.ToArray()));
            RecordWater(hexCellData, IndexOffset);
            //���ǻ���˳��
            List<int> l = new List<int>();
            l.AddRange(CoastMeshTriDrawOrderFunction(v.ToArray()));
            ints.Clear();
            OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
            drawOrderCoast.AddRange(ints);
        }


        int[][] arrArawOrderLakeOrSea = new int[2][];
        //��������
        arrArawOrderLakeOrSea[0] = drawOrderCoast.ToArray();
        //��������
        arrArawOrderLakeOrSea[1] = drawOrderLakeOrSea.ToArray();
        Color[] waterColorArray = lakeColors.ToArray();
        _cachedWaterColors = waterColorArray;
        return new WaterGeometry
        {
            Vertices = verticesLakeOrSea.ToArray(),
            UVs = uvLakeOrSea.ToArray(),
            Indices = arrArawOrderLakeOrSea,
            Colors = waterColorArray
        };
    }

    /// <summary>【动态地图-阶段二】应用湖海网格：create=false 复用既有宿主 Mesh；无水面时销毁旧宿主。</summary>
    private void ApplyWaterGeometry(WaterGeometry geometry, bool create)
    {
        if (geometry == null)
        {
            if (!create && _lakeHost != null)
            {
                Object.Destroy(_lakeHost);
                _lakeHost = null;
            }
            _waterMesh = null;
            return;
        }

        if (create || _lakeHost == null)
        {
            _lakeHost = new GameObject("LakeOrSea");
            _waterMesh = MapController.CreatMesh(geometry.Vertices, geometry.UVs, geometry.Indices, _lakeHost, _config.lakeOrSeaMaterial, geometry.Colors);
            return;
        }

        RefillWaterMesh(geometry, _waterMesh);
        MeshRenderer meshRenderer = _lakeHost.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
            meshRenderer.sharedMaterials = _config.lakeOrSeaMaterial;
    }

    private void RefillWaterMesh(WaterGeometry geometry, Mesh mesh)
    {
        mesh.Clear();
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

    //����Mesh����
    private void GridMeshCreat(Vector3[] hexVertices)
    {
        //������
        GameObject GridLine = new GameObject("GridLine");
        //��������
        List<Vector3> verticesGridLine = new List<Vector3>();
        //UV 
        List<Vector2> uvGridLine = new List<Vector2>();
        //����˳��
        List<List<int>> drawOrderGridLine = new List<List<int>>();

        for (int j = 0; j < hexVertices.Length; j++)
        {
            List<int> ints = new List<int>();//��ʱ���м����
            //���������������ȡhexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            int IndexOffset = verticesGridLine.Count;
            //���ǡ����򺣡�������
            if (isLakeOrSea(hexCellData)) { continue; }

            //��������            
            CellBuildContext ctx = MakeBuildContext(hexCellData);
            verticesGridLine.AddRange(_meshGenerator.BuildGridVertices(ctx));
            //UV
            uvGridLine.AddRange(_meshGenerator.BuildGridUV());
            //�������˳��
            List<int> l = new List<int>();
            l.AddRange(_meshGenerator.BuildGridDrawOrder());
            ints.Clear();
            OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
            drawOrderGridLine.Add(ints);
        }

        Shader gridLineShader = Shader.Find("Custom/GridLine") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Hidden/InternalErrorShader");
        int vertexOffset = 0;
        for (int j = 0, i = 0; j < hexVertices.Length; j++)
        {
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            if (isLakeOrSea(hexCellData)) { continue; }

            var localVerts = verticesGridLine.GetRange(vertexOffset, 12).ToArray();
            var localUVs = uvGridLine.GetRange(vertexOffset, 12).ToArray();
            var absoluteIndices = drawOrderGridLine[i];
            var localIndices = new int[absoluteIndices.Count];
            for (int k = 0; k < absoluteIndices.Count; k++)
                localIndices[k] = absoluteIndices[k] - vertexOffset;

            GameObject go = new GameObject($"SubGridLine_{j}");
            MapController.CreatMesh(localVerts, localUVs, localIndices, go, new Material(gridLineShader), addCollider: false);
            hexCellData.GridMesh = go;
            go.SetActive(false);
            go.transform.parent = GridLine.transform;

            vertexOffset += 12;
            i++;
        }
        _gridHost = GridLine;
        mapGenerator.gridGameObject = GridLine;
    }

    /// <summary>
    /// 【动态地图-阶段二】重建单个地块的网格线（高度变化后跟随；水→陆地补建；陆地→水销毁）。
    /// 网格线默认隐藏，运行时高亮（PlayerInputHandler/UIController）自行 SetActive/着色。
    /// </summary>
    private void RebuildGridCell(HexCellData hexCellData)
    {
        if (hexCellData == null) return;

        if (isLakeOrSea(hexCellData))
        {
            if (hexCellData.GridMesh != null)
            {
                Object.Destroy(hexCellData.GridMesh);
                hexCellData.GridMesh = null;
            }
            return;
        }

        CellBuildContext ctx = MakeBuildContext(hexCellData);
        Vector3[] verts = _meshGenerator.BuildGridVertices(ctx).ToArray();
        Vector2[] uvs = _meshGenerator.BuildGridUV().ToArray();
        int[] indices = _meshGenerator.BuildGridDrawOrder().ToArray();

        if (hexCellData.GridMesh == null)
        {
            Shader gridLineShader = Shader.Find("Custom/GridLine") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Hidden/InternalErrorShader");
            GameObject go = new GameObject($"SubGridLine_{hexCellData.GenerateOrder}");
            MapController.CreatMesh(verts, uvs, indices, go, new Material(gridLineShader), addCollider: false);
            go.transform.parent = _gridHost != null ? _gridHost.transform : null;
            hexCellData.GridMesh = go;
            go.SetActive(false);
        }
        else
        {
            MeshFilter filter = hexCellData.GridMesh.GetComponent<MeshFilter>();
            if (filter == null) filter = hexCellData.GridMesh.AddComponent<MeshFilter>();
            Mesh mesh = filter.sharedMesh;
            if (mesh == null)
            {
                mesh = new Mesh();
                filter.sharedMesh = mesh;
            }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = indices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }

    //�ж�ĳ���ؿ��Ƿ�Ϊ����
    private bool isLakeOrSea(HexCellData hexCellData)
    {
        return WaterLevelConfig.IsWater(hexCellData);
    }

    //����ͼMeshʵ���������˳��ѡ���߼�
    private int MainMeshSolidAreaDrawOrderFunction(HexCellData hexCellData, out List<Enums.HexDirection> direction)
    {
        int drawOrder;
        direction = new List<Enums.HexDirection>();

        if (hexCellData.HexType == Enums.HexType.RiverSource)
        {
            //���� + ����
            drawOrder = 2;
            direction.Add(hexCellData.RiverOutgoingDirection);
        }
        else if (hexCellData.HexType == Enums.HexType.RiverMidstream)
        {
            //���� + ���� + ��ȥ����
            drawOrder = 3;
            direction.Add(hexCellData.RiverIncomingDirection);
            direction.Add(hexCellData.RiverOutgoingDirection);
        }
        else if (hexCellData.HexType == Enums.HexType.RiverEnd)
        {
            //�յ� + ����
            drawOrder = 2;
            direction.Add(hexCellData.RiverIncomingDirection);
        }
        else
        {
            //�޺����ؿ�
            drawOrder = 1;
        }

        return drawOrder;
    }

    //����ͼMesh���ι����������˳��ѡ���߼�
    //
    private void MainMeshRectFunction(HexCellData hexCellData, Enums.HexDirection direction, out bool isSlope, out bool isRiver)
    {
        isRiver = false;
        isSlope = true;

        if (_mapDataService.GetNeighbor(hexCellData, direction) == null) { return; }

        if ((hexCellData.RiverIncomingDirection == direction || hexCellData.RiverOutgoingDirection == direction) &&
            (hexCellData.hasRiver && _mapDataService.GetNeighbor(hexCellData, direction).hasRiver))
        { isRiver = true; }

        Enums.RectType[] rectTypes = new Enums.RectType[] { };
        Enums.TriType[] triTypes = new Enums.TriType[] { };
        TerrainGenerator.IsType(hexCellData, out rectTypes, out triTypes, _mapDataService);

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
    //����ͼMesh���ǹ��������߼�
    private Enums.TriType MainMeshTriFunction(HexCellData hexCellData, Enums.HexDirection directionA, Enums.HexDirection directionB)
    {
        //�жϹ�������Ļ��Ʒ���
        Enums.RectType[] rectTypes = new Enums.RectType[] { };
        Enums.TriType[] triTypes = new Enums.TriType[] { };
        TerrainGenerator.IsType(hexCellData, out rectTypes, out triTypes, _mapDataService);
        int index = 0;

        if (directionA == Enums.HexDirection.NE && directionB == Enums.HexDirection.E) { index = 0; }
        else if (directionA == Enums.HexDirection.E && directionB == Enums.HexDirection.SE) { index = 1; }

        //���޶�Ӧ�ھ�
        if (_mapDataService.GetNeighbor(hexCellData, directionA) == null || _mapDataService.GetNeighbor(hexCellData, directionB) == null) { return Enums.TriType.zero; }

        return triTypes[index];
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

    private RectangleTransitionMeshData GetRectangleMesh(
        HexCellData owner,
        Enums.HexDirection direction)
    {
        if (!_genericRectangleMeshes.TryGetValue((owner.GenerateOrder, direction), out RectangleTransitionMeshData rectangle))
        {
            throw new System.InvalidOperationException(
                $"Triangle transition requires rectangle profile {owner.GenerateOrder}:{direction}.");
        }
        return rectangle;
    }

    private RectangleTransitionMeshData GetGenericRectangleMesh(
        CellBuildContext ctx,
        Enums.HexDirection direction)
    {
        HexCellData hexCellData = ctx.Cell;
        HexCellData neighbor = _mapDataService.GetNeighbor(hexCellData, direction);
        Vector3[] solid = ctx.Solid;
        Vector3[] neighborSolid = ctx.GetNeighborSolid(direction);
        var starts = new List<Vector3>(4);
        var ends = new List<Vector3>(4);

        switch (direction)
        {
            case Enums.HexDirection.NE:
                starts.Add(solid[1]);
                starts.Add(solid[7]);
                starts.Add(solid[8]);
                starts.Add(solid[2]);
                ends.Add(neighborSolid[5]);
                ends.Add(neighborSolid[14]);
                ends.Add(neighborSolid[13]);
                ends.Add(neighborSolid[4]);
                break;
            case Enums.HexDirection.E:
                starts.Add(solid[2]);
                starts.Add(solid[9]);
                starts.Add(solid[10]);
                starts.Add(solid[3]);
                ends.Add(neighborSolid[6]);
                ends.Add(neighborSolid[16]);
                ends.Add(neighborSolid[15]);
                ends.Add(neighborSolid[5]);
                break;
            case Enums.HexDirection.SE:
                starts.Add(solid[3]);
                starts.Add(solid[11]);
                starts.Add(solid[12]);
                starts.Add(solid[4]);
                ends.Add(neighborSolid[1]);
                ends.Add(neighborSolid[18]);
                ends.Add(neighborSolid[17]);
                ends.Add(neighborSolid[6]);
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
            starts,
            ends,
            type,
            subdivision,
            perturbIntermediate);
        _genericRectangleMeshes[(hexCellData.GenerateOrder, direction)] = rectangle;
        return rectangle;
    }

    private bool UseGenericTransitions()
    {
        return _config.transitionGenerationMode == Enums.TransitionGenerationMode.GenericFan;
    }

    // 按高度差计算梯边细分数：每 stepHeight 高度一级台阶，使台阶落在相同整数高度、跨边对齐。
    // 因 CreateStepPoints 自带 +1 级（n+1 个台阶高度），要让台阶落整数高度需 n = 高度级数 - 1。
    private int GetSubdivision(float heightA, float heightB)
    {
        int levels = Mathf.RoundToInt(Mathf.Abs(heightA - heightB) / _config.stepHeight);
        return Mathf.Clamp(levels - 1, 0, _config.maxStepSubdivision);
    }

    //��ˮMeshʵ���������˳��ѡ���߼�
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
                //Debug.Log("��������Ӧ���ɵ���˴�");
                return null;
        }
    }

    //��ˮMesh���ι����������˳��ѡ���߼�
    private int[] RiverMeshDownstreamDrawOrderFunction()
    {
        return _meshGenerator.BuildOutgoingRiverSlopDrawOrder();
    }

    //����Meshʵ���������˳��ѡ���߼�
    private int[] LakeOrSeaMeshSolidAreaDrawOrderFunction()
    {
        return _meshGenerator.BuildLakeOrSeaDrawOrder();
    }

    //����Mesh���ι����������˳��ѡ���߼�
    private List<int> LakeOrSeaMeshRectDrawOrderFunction(Enums.HexDirection direction)
    {
        return _meshGenerator.BuildLakeOrSeaRectDrawOrder(direction);
    }

    //����Mesh���ǹ����������˳��ѡ���߼�
    private List<int> LakeOrSeaMeshTriDrawOrderFunction(Enums.HexDirection directionA, Enums.HexDirection directionB)
    {
        return _meshGenerator.BuildLakeOrSeaTriDrawOrder(directionA, directionB);
    }

    //����Mesh���ι����������˳��ѡ���߼�
    private List<int> CoastMeshRectDrawOrderFunction(Vector3[] v)
    {
        return _meshGenerator.BuildCoastRectDrawOrder(v);
    }

    //����Mesh���ǹ����������˳��ѡ���߼�
    private List<int> CoastMeshTriDrawOrderFunction(Vector3[] v)
    {
        return _meshGenerator.BuildCoastTriDrawOrder(v);
    }


    //����˳������ӹ��� - 1.����ͼ��������ؿ����2.�������ӹ���
    private void MainMeshDrawOrderElementAddRule(ref HexCellData hexCellData, List<int> drawOrder, ref List<List<int>> subList, int IndexOffset)
    {
        int bucket = WaterLevelConfig.ClassifyHeight(hexCellData.Height);
        List<int> target = bucket switch
        {
            0 => subList[0], // 水域
            1 => subList[1], // 低地
            _ => subList[2], // 高地
        };
        foreach (int i in drawOrder)
        {
            target.Add(i + IndexOffset);
        }
    }

    //����˳������ӹ��� - 2.�������ӹ���
    private void OtherMeshDrawOrderElementAddRule(ref HexCellData hexCellData, List<int> drawOrder, ref List<int> ints, int IndexOffset)
    {
        foreach (int i in drawOrder)
        {
            //����ֱ�Ӽ��룬��ҪԤ����
            ints.Add(i + IndexOffset);
        }
    }

    private void InstantiateLandForms(Vector3[] hexVertices)
    {
        _landFormRoot = new GameObject("LandForm");
        SetLayerRecursively(_landFormRoot, LayerMask.NameToLayer("FogAffectedEnvironment"));

        for (int j = 0; j < hexVertices.Length; j++)
        {
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            // 【地图地貌配置化】模型来自 MapLandFormSO.modelPrefab；无地貌或未配置模型则跳过
            MapLandFormSO landForm = hexCellData.landForm;
            if (landForm == null || landForm.modelPrefab == null) { continue; }
            hexCellData.landFormModel = Instantiate(landForm.modelPrefab);
            hexCellData.landFormModel.transform.position = hexCellData.RealCenterWorldCoordinate + new Vector3(0, 0, 0);
            hexCellData.landFormModel.AddComponent<ModelController>();
            hexCellData.landFormModel.transform.SetParent(_landFormRoot.transform);
            SetLayerRecursively(hexCellData.landFormModel, _landFormRoot.layer);
        }

        // 资源/地貌需要由 CommandBuffer 逐 Renderer 重绘对象遮罩。
        // Runtime StaticBatchingUtility.Combine 会改变 Renderer 的底层网格范围，
        // 替换材质重绘时可能把整批几何写入单个对象遮罩，造成全屏误雾化。
    }
    private void InstantiateResources(Vector3[] hexVertices)
    {
        _resourceRoot = new GameObject("Resource");
        SetLayerRecursively(_resourceRoot, LayerMask.NameToLayer("FogAffectedEnvironment"));
        for (int j = 0; j < hexVertices.Length; j++)
        {
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            // 【地图资源配置化】模型来自 MapResourceSO.modelPrefab；留空 = 不生成模型
            MapResourceSO resource = hexCellData.resource;
            if (resource == null || resource.modelPrefab == null) { continue; }
            hexCellData.resourceModel = Instantiate(resource.modelPrefab);
            hexCellData.resourceModel.transform.position = hexCellData.RealCenterWorldCoordinate + new Vector3(0, 0, 0);
            hexCellData.resourceModel.AddComponent<ModelController>();
            hexCellData.resourceModel.transform.SetParent(_resourceRoot.transform);
            SetLayerRecursively(hexCellData.resourceModel, _resourceRoot.layer);
        }

        // 同上：选择性雾化阶段不对资源做运行时静态合批。
    }

    private void SetupEnvironmentFogEffect()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogWarning("MapRenderer: 找不到 Main Camera，跳过资源/地貌选择性雾化效果。");
            return;
        }

        _environmentFogEffect = mainCamera.GetComponent<FogEnvironmentSelectiveEffect>();
        if (_environmentFogEffect == null)
            _environmentFogEffect = mainCamera.gameObject.AddComponent<FogEnvironmentSelectiveEffect>();

        // 【断供方案-阶段5】建筑根节点纳入选择性雾化（运行时按存在与否收集，为空自动跳过）
        GameObject[] buildingRoots =
        {
            GameObject.Find("PlayerBuilding"),
            GameObject.Find("EnemyBuilding")
        };
        // 【单位擦除层-方案A】单位根节点用于从雾化遮罩中擦除单位像素（单位永不雾化）
        GameObject[] unitRoots =
        {
            GameObject.Find("PlayerUnit"),
            GameObject.Find("EnemyUnit")
        };
        _environmentFogEffect.Initialize(_landFormRoot, _resourceRoot, buildingRoots, unitRoots);
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null || layer < 0) return;

        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

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
        // 【迷雾过渡】每帧驱动过渡管理器，推进 FogAlpha
        _fogTransition.Tick(Time.deltaTime);

        // 限频刷新视觉：只在有过渡且计时器到期时更新纹理/顶点色
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
        if (!_isLogisticsSubscribed && _logisticsService != null)
        {
            _logisticsService.LogisticsChanged += OnLogisticsChanged;
            _isLogisticsSubscribed = true;
        }
    }

    private void OnMapVisualChanged()
    {
        // 【迷雾过渡】不再瞬间重建，而是更新所有 cell 的过渡目标
        UpdateFogTransitionTargets();
        _environmentFogEffect?.RefreshRenderers();
    }

    private void OnLogisticsChanged()
    {
        // 【迷雾过渡】不再瞬间重建，而是更新所有 cell 的过渡目标
        UpdateFogTransitionTargets();
        _environmentFogEffect?.RefreshRenderers();
    }

    /// <summary>
    /// 遍历所有 cell，根据探索/可见状态设置过渡目标值，交给过渡管理器驱动。
    /// </summary>
    private void UpdateFogTransitionTargets()
    {
        if (_cellsInGenerateOrder == null) return;

        const int PlayerViewerFactionId = 0;

        foreach (var cell in _cellsInGenerateOrder)
        {
            if (cell == null) continue;

            // 计算目标可见性（与原 RebuildFogMask 逻辑一致；阶段二起统一查询 IMapVisibilityResolver，
            // 使 VisibilityLease 的临时点亮生效且不写探索位）
            bool isVisible;
            if (_visibilityResolver != null)
                isVisible = _visibilityResolver.IsVisibleToFaction(cell, PlayerViewerFactionId);
            else
                isVisible = (_logisticsService != null)
                    ? _logisticsService.IsVisibleToFaction(cell, PlayerViewerFactionId)
                    : cell.IsExplored;

            // 目标值：可见 → 1.0，不可见 → 0.0
            float targetAlpha = isVisible ? 1f : 0f;

            // 首次（开局）立即快照到目标值，避免开局主城范围缓慢浮现；
            // 之后的探索/失去可见性才走逐渐过渡。
            if (_fogInitialized)
                _fogTransition.RequestTransition(cell, targetAlpha);
            else
                _fogTransition.SnapTransition(cell, targetAlpha);
        }

        if (!_fogInitialized)
        {
            // 首帧快照后立即刷新一次视觉，确保开局画面正确
            _fogInitialized = true;
            UpdateExplorationVisuals();
            RebuildFogMask();
            _fogTransition.ClearDirty();
        }
    }

    private void SetupFogGlobalShaderProperties(Vector3[] hexVertices)
    {
        Material fogMat = _config != null ? _config.fogMaterial : null;

        // 迷雾贴图与色调：优先取现有迷雾材质的 _MainTex/_Color。
        // 关键兜底：若迷雾材质或其贴图缺失（本项目从 Tuanjie 迁移后部分资源 .meta 的 GUID
        // 损坏，运行时 fogMaterial 可能解析为 null），绝不能让全局 _FogTex 悬空——否则着色器
        // 采样到默认黑纹理，未探索地块被渲染成纯黑。此处回退到白纹理 + 米色，保证仍是迷雾外观。
        Texture fogTex = fogMat != null ? fogMat.GetTexture("_MainTex") : null;
        bool fogTexMissing = fogTex == null;
        if (fogTexMissing) fogTex = Texture2D.whiteTexture;

        // 方案A用整图唯一映射（UV∈[0,1]），贴图设 Clamp 避免边缘越界采样到对面；不重复平铺。
        if (!fogTexMissing) fogTex.wrapMode = TextureWrapMode.Clamp;

        Color fogColor = (fogMat != null && fogMat.HasProperty("_Color"))
            ? fogMat.GetColor("_Color")
            : new Color(0.735f, 0.663f, 0.590f, 1f);

        // 计算整张地图的世界 XZ 包围盒：遍历所有格子中心，向外扩一个 OuterRadius 覆盖边缘格子的顶点。
        // 一张迷雾贴图将被归一化铺满这个包围盒正好一次（见 FogBlend_vert）。
        float minX = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxZ = float.MinValue;
        if (hexVertices != null)
        {
            foreach (var hv in hexVertices)
            {
                HexCellData cell = _mapDataService.GetCell(hv);
                if (cell == null) continue;
                Vector3 c = cell.CenterWorldCoordinate;
                if (c.x < minX) minX = c.x;
                if (c.x > maxX) maxX = c.x;
                if (c.z < minZ) minZ = c.z;
                if (c.z > maxZ) maxZ = c.z;
            }
        }
        float pad = _config != null ? _config.OuterRadius : 3f;
        if (minX > maxX) { minX = 0; maxX = 1; minZ = 0; maxZ = 1; } // 无格子时兜底，避免除零
        minX -= pad; minZ -= pad; maxX += pad; maxZ += pad;
        float sizeX = Mathf.Max(0.0001f, maxX - minX);
        float sizeZ = Mathf.Max(0.0001f, maxZ - minZ);

        // fogTexAmount：贴图强度。1=完整显示图案（方案A默认），0=纯色。想让图案淡一点就调低。
        const float fogTexAmount = 1.0f;
        // fogMemoryDim：记忆区（探索过·当前无视野）亮度系数。0=全黑，1=不压暗。
        const float fogMemoryDim = 0.45f;

        Shader.SetGlobalTexture("_FogTex", fogTex);
        Shader.SetGlobalColor("_FogColor", fogColor);
        Shader.SetGlobalFloat("_FogEmission", 1.0f);
        Shader.SetGlobalFloat("_FogTexAmount", fogTexAmount);
        Shader.SetGlobalFloat("_FogMemoryDim", fogMemoryDim);
        Shader.SetGlobalColor("_FogMemoryColor", _config != null ? _config.fogMemoryColor : Color.white);
        Shader.SetGlobalVector("_FogMapOrigin", new Vector4(minX, minZ, 0, 0));
        Shader.SetGlobalVector("_FogMapSize", new Vector4(sizeX, sizeZ, 0, 0));
        Shader.SetGlobalFloat("_FogPixelSize", _config != null ? _config.fogPixelSize : 0f);
        Shader.SetGlobalFloat("_FogJaggedAmount", _config != null ? _config.fogJaggedAmount : 1.0f);
        Shader.SetGlobalFloat("_FogNoiseWavelength", _config != null ? _config.fogNoiseWavelength : 2.0f);
        Shader.SetGlobalFloat("_FogEdgeStyle", _config != null ? (float)(int)_config.fogEdgeStyle : 0f);
        Shader.SetGlobalFloat("_FogEdgeSoftness", _config != null ? _config.fogEdgeSoftness : 0.8f);
        Shader.SetGlobalFloat("_FogEdgeAnimSpeed", _config != null ? _config.fogEdgeAnimSpeed : 0.25f);

        // 【探索重构-方案三】未探索区视觉参数（去饱和+半透明雾）
        Shader.SetGlobalFloat("_FogUnexploredDesaturate", 0.5f);
        Shader.SetGlobalFloat("_FogUnexploredBlend", 0.7f);
        Shader.SetGlobalVector("_FogScrollSpeed", new Vector4(0.02f, 0.01f, 0f, 0f));

        // 方案B：按同一包围盒新建探索遮罩并绑定为全局纹理（内容由 RebuildFogMask 全量重建）。
        CreateFogMask(minX, minZ, sizeX, sizeZ);
        Shader.SetGlobalTexture("_FogMaskTex", _fogMaskTex);

        if (fogMat == null)
            Debug.LogWarning("MapRenderer: fogMaterial 为 null（检查 MapGenerationConfig 的 fogMaterial 引用是否因 .meta GUID 损坏而丢失）。已使用回退迷雾参数（白纹理 + 米色），未探索地块不会是纯黑。");
        else if (fogTexMissing)
            Debug.LogWarning("MapRenderer: fogMaterial 存在但其 _MainTex 为 null，已回退到白纹理。");
        else
            Debug.Log($"MapRenderer: 迷雾整图映射已设置 _FogTex={fogTex.name} origin=({minX:F1},{minZ:F1}) size=({sizeX:F1},{sizeZ:F1})");
    }

    private void UpdateExplorationVisuals()
    {
        if (_terrainMesh == null || _cellsInGenerateOrder == null) return;
        if (_cachedTerrainColors == null || _cachedTerrainColors.Length == 0) return;

        foreach (var cell in _cellsInGenerateOrder)
        {
            Color newColor = FogVertexColor(cell);

            if (cell.MeshSolidAreaVertexStartIndex >= 0)
            {
                int start = cell.MeshSolidAreaVertexStartIndex;
                for (int i = 0; i < 44 && start + i < _cachedTerrainColors.Length; i++)
                    _cachedTerrainColors[start + i] = newColor;
            }

            foreach (var range in cell.MeshTransitionVertexRanges)
            {
                for (int i = 0; i < range.count && range.start + i < _cachedTerrainColors.Length; i++)
                    _cachedTerrainColors[range.start + i] = newColor;
            }
        }

        _terrainMesh.colors = _cachedTerrainColors;

        UpdateWaterExplorationVisuals();
        UpdateRiverExplorationVisuals();
    }

    // 【探索重构-阶段6】【迷雾过渡】顶点色编码：.r=FogAlpha(0-1 连续值，过渡中)，.g 废弃（固定0）
    private static Color FogVertexColor(HexCellData cell)
    {
        float r = cell.FogAlpha; // 不再是二进制，而是连续过渡值
        return new Color(r, 0f, 0f, 1f);
    }

    // ===================== 方案B：世界探索遮罩贴图 =====================

    // 依地图世界包围盒新建（或重建）探索遮罩。texel 尺寸由 config.fogMaskTexelSize 决定，
    // 双线性采样时它就是锯齿渐变带的宽度。分辨率上限 1024 防超大地图爆内存。
    private void CreateFogMask(float minX, float minZ, float sizeX, float sizeZ)
    {
        float texel = Mathf.Max(0.25f, _config != null ? _config.fogMaskTexelSize : 2.0f);
        _fogMaskW = Mathf.Clamp(Mathf.CeilToInt(sizeX / texel), 4, 1024);
        _fogMaskH = Mathf.Clamp(Mathf.CeilToInt(sizeZ / texel), 4, 1024);
        _fogMaskOrigin = new Vector2(minX, minZ);
        _fogMaskSize = new Vector2(sizeX, sizeZ);

        if (_fogMaskTex != null) Destroy(_fogMaskTex); // 地图重生成时释放旧遮罩，避免纹理泄漏

        // linear=true：数据贴图不做 gamma，0/1 精确；Bilinear 产生边界渐变带；Clamp 防边缘越界。
        _fogMaskTex = new Texture2D(_fogMaskW, _fogMaskH, TextureFormat.RGBA32, false, true)
        {
            name = "FogMaskTex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        _fogMaskData = new Color32[_fogMaskW * _fogMaskH]; // 默认全 0 = 全未探索
        _fogMaskTex.SetPixels32(_fogMaskData);
        _fogMaskTex.Apply(false);
    }

    // 把一格的六边形足迹盖章进遮罩的 R 通道（探索状态由 FogAlpha 驱动）。
    private void StampCellToFogMask(HexCellData cell)
    {
        if (_fogMaskData == null) return;
        Vector3 c = cell.CenterWorldCoordinate;
        float o = _config != null ? _config.OuterRadius : 3f;
        float ir = _config != null ? _config.InnerRadius : 2.598f;
        const float H = 0.8660254f;

        int px0 = Mathf.Clamp(Mathf.FloorToInt((c.x - o - _fogMaskOrigin.x) / _fogMaskSize.x * _fogMaskW), 0, _fogMaskW - 1);
        int px1 = Mathf.Clamp(Mathf.CeilToInt ((c.x + o - _fogMaskOrigin.x) / _fogMaskSize.x * _fogMaskW), 0, _fogMaskW - 1);
        int py0 = Mathf.Clamp(Mathf.FloorToInt((c.z - o - _fogMaskOrigin.y) / _fogMaskSize.y * _fogMaskH), 0, _fogMaskH - 1);
        int py1 = Mathf.Clamp(Mathf.CeilToInt ((c.z + o - _fogMaskOrigin.y) / _fogMaskSize.y * _fogMaskH), 0, _fogMaskH - 1);

        // 【迷雾过渡】盖章强度由 FogAlpha 决定，0-255 连续值
        byte intensity = (byte)Mathf.RoundToInt(cell.FogAlpha * 255f);

        for (int py = py0; py <= py1; py++)
        {
            float wz = _fogMaskOrigin.y + (py + 0.5f) / _fogMaskH * _fogMaskSize.y;
            for (int px = px0; px <= px1; px++)
            {
                float wx = _fogMaskOrigin.x + (px + 0.5f) / _fogMaskW * _fogMaskSize.x;
                float dx = wx - c.x, dz = wz - c.z;
                if (Mathf.Abs(dx) <= ir &&
                    Mathf.Abs(0.5f * dx + H * dz) <= ir &&
                    Mathf.Abs(-0.5f * dx + H * dz) <= ir)
                {
                    int idx = py * _fogMaskW + px;
                    _fogMaskData[idx].r = intensity;
                }
            }
        }
    }

    private void RebuildFogMask()
    {
        if (_fogMaskTex == null || _fogMaskData == null || _cellsInGenerateOrder == null) return;

        // 【迷雾过渡】全清 0（每帧重建时的初始状态）
        for (int i = 0; i < _fogMaskData.Length; i++)
            _fogMaskData[i].r = 0;

        // 【迷雾过渡】盖章所有 cell，强度由 FogAlpha 控制（不再是二进制可见性判断）
        foreach (var cell in _cellsInGenerateOrder)
        {
            if (cell == null) continue;
            StampCellToFogMask(cell);
        }

        _fogMaskTex.SetPixels32(_fogMaskData);
        _fogMaskTex.Apply(false);
    }

    // 更新河流 Mesh 的顶点色（白=已探索显示河流，黑=未探索时河流透明露出地形迷雾）。
    private void UpdateRiverExplorationVisuals()
    {
        if (_riverMesh == null || _cellsInGenerateOrder == null) return;
        if (_cachedRiverColors == null || _cachedRiverColors.Length == 0) return;

        foreach (var cell in _cellsInGenerateOrder)
        {
            Color newColor = FogVertexColor(cell);
            foreach (var range in cell.MeshRiverVertexRanges)
            {
                for (int i = 0; i < range.count && range.start + i < _cachedRiverColors.Length; i++)
                    _cachedRiverColors[range.start + i] = newColor;
            }
        }

        _riverMesh.colors = _cachedRiverColors;
    }

    // 与地形一致地更新海洋/湖泊 Mesh 的顶点色（白=已探索显示水面，黑=未探索显示迷雾）。
    private void UpdateWaterExplorationVisuals()
    {
        if (_waterMesh == null || _cellsInGenerateOrder == null) return;
        if (_cachedWaterColors == null || _cachedWaterColors.Length == 0) return;

        foreach (var cell in _cellsInGenerateOrder)
        {
            Color newColor = FogVertexColor(cell);
            foreach (var range in cell.MeshWaterVertexRanges)
            {
                for (int i = 0; i < range.count && range.start + i < _cachedWaterColors.Length; i++)
                    _cachedWaterColors[range.start + i] = newColor;
            }
        }

        _waterMesh.colors = _cachedWaterColors;
    }

    private static bool RectFlat(Enums.ShadingStyle style)
    {
        return style == Enums.ShadingStyle.FlatAll || style == Enums.ShadingStyle.FlatRect_SmoothTri;
    }

    private static bool TriFlat(Enums.ShadingStyle style)
    {
        return style == Enums.ShadingStyle.FlatAll || style == Enums.ShadingStyle.SmoothRect_FlatTri;
    }

    private void PostProcessNormals(
        Mesh mesh,
        Enums.ShadingStyle style,
        List<(int start, int count)> rectangleRanges,
        List<(int start, int count)> triangleRanges)
    {
        if (style == Enums.ShadingStyle.FlatAll)
            return;

        Vector3[] normals = mesh.normals;
        Vector3[] smoothNormals = BuildPositionSmoothedNormals(mesh);

        switch (style)
        {
            case Enums.ShadingStyle.SmoothAll:
            case Enums.ShadingStyle.ForceUpNormals:
            case Enums.ShadingStyle.ExaggeratedNormals:
                normals = smoothNormals;
                break;
            case Enums.ShadingStyle.FlatRect_SmoothTri:
                ApplyNormalRanges(normals, smoothNormals, triangleRanges);
                break;
            case Enums.ShadingStyle.SmoothRect_FlatTri:
                ApplyNormalRanges(normals, smoothNormals, rectangleRanges);
                break;
        }

        if (style == Enums.ShadingStyle.ForceUpNormals ||
            style == Enums.ShadingStyle.ExaggeratedNormals)
        {
            ApplyStylizedTransitionNormals(normals, style);
        }

        mesh.normals = normals;
        MapController.RecalculateTangentsSafe(mesh);
    }

    private static Vector3[] BuildPositionSmoothedNormals(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        var sums = new Dictionary<Vector3Int, Vector3>();

        for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
        {
            int[] triangles = mesh.GetTriangles(subMesh);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                Vector3 faceNormal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                if (faceNormal.sqrMagnitude < 1e-10f) continue;

                AccumulateNormal(sums, PositionKey(vertices[a]), faceNormal);
                AccumulateNormal(sums, PositionKey(vertices[b]), faceNormal);
                AccumulateNormal(sums, PositionKey(vertices[c]), faceNormal);
            }
        }

        var result = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            sums.TryGetValue(PositionKey(vertices[i]), out Vector3 sum);
            result[i] = sum.sqrMagnitude > 1e-10f ? sum.normalized : Vector3.up;
        }
        return result;
    }

    private static Vector3Int PositionKey(Vector3 position)
    {
        const float precision = 10000f;
        return new Vector3Int(
            Mathf.RoundToInt(position.x * precision),
            Mathf.RoundToInt(position.y * precision),
            Mathf.RoundToInt(position.z * precision));
    }

    private static void AccumulateNormal(
        Dictionary<Vector3Int, Vector3> sums,
        Vector3Int key,
        Vector3 normal)
    {
        sums.TryGetValue(key, out Vector3 current);
        sums[key] = current + normal;
    }

    private static void ApplyNormalRanges(
        Vector3[] target,
        Vector3[] source,
        List<(int start, int count)> ranges)
    {
        foreach (var range in ranges)
        {
            int end = Mathf.Min(range.start + range.count, target.Length);
            for (int i = range.start; i < end; i++)
                target[i] = source[i];
        }
    }

    private void ApplyStylizedTransitionNormals(Vector3[] normals, Enums.ShadingStyle style)
    {
        foreach (HexCellData cell in _cellsInGenerateOrder)
        {
            foreach (var range in cell.MeshTransitionVertexRanges)
            {
                int end = Mathf.Min(range.start + range.count, normals.Length);
                for (int i = range.start; i < end; i++)
                {
                    if (style == Enums.ShadingStyle.ForceUpNormals)
                    {
                        normals[i] = Vector3.up;
                        continue;
                    }

                    Vector3 normal = normals[i];
                    normals[i] = new Vector3(normal.x * 2f, normal.y * 0.3f, normal.z * 2f).normalized;
                }
            }
        }
    }

    // ===================== 【动态地图-阶段二】运行时安全 WholeMap 后端 =====================
    // 阶段二仍用 WholeMap 后端重建（先让玩法跑通），但禁止运行时再次调用一次性 MapRender()：
    // Prepare 只生成数据（staging），Commit 交换/复用 Mesh 与材质缓存，RefreshCellObjects 只动变化格。

    /// <summary>运行时重建入口：基于当前（已写入目标数据的）HexCellData 生成全图几何 staging。</summary>
    public PreparedWholeMapGeometry PrepareWholeMapGeometry()
    {
        Vector3[] hexVertices = _mapDataService.GetHexVertices();
        _view = new MapDataReadOnlyView(_mapDataService);
        _solidVertices.Clear();
        _lakeOrSeaVertices.Clear();
        _rectVerticesByCell.Clear();

        return new PreparedWholeMapGeometry
        {
            Terrain = BuildTerrainGeometry(hexVertices),
            River = BuildRiverGeometry(hexVertices),
            Water = BuildWaterGeometry(hexVertices)
        };
    }

    /// <summary>把 staging 几何原子应用到渲染层：复用既有 Mesh（Clear 后重建）与材质缓存，无新建/泄漏。</summary>
    public void CommitWholeMapGeometry(PreparedWholeMapGeometry staging)
    {
        if (staging == null) return;

        ApplyTerrainGeometry(staging.Terrain, create: false);
        ApplyRiverGeometry(staging.River, create: false);
        ApplyWaterGeometry(staging.Water, create: false);

        if (_mapDataService != null && mapGenerator != null)
        {
            _mapDataService.UpdateRuntimeData(mapGenerator.verticesList, mapGenerator.mesh, mapGenerator.gridGameObject);
        }
    }

    /// <summary>
    /// 变化格对象刷新：移除已清空的地貌/资源模型（转交 RemovedVisualHandle）、
    /// 保留模型归位到新 RealCenterWorldCoordinate、重建网格线。
    /// </summary>
    public void RefreshCellObjects(IReadOnlyCollection<HexCellData> changedCells, RemovedVisualHandle removed)
    {
        if (changedCells == null) return;

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

            RebuildGridCell(cell);
        }
    }

    /// <summary>
    /// 立即刷新迷雾视觉（突破 20fps 限频）：更新目标 → 顶点色 → 遮罩。
    /// 用于竞技场突起/宝箱摧毁等需要瞬间亮灭的瞬间。
    /// </summary>
    public void ForceRefreshFogVisuals()
    {
        if (!_fogInitialized || _cellsInGenerateOrder == null) return;

        UpdateFogTransitionTargets();
        UpdateExplorationVisuals();
        RebuildFogMask();
        _fogTransition.ClearDirty();
        _fogRefreshTimer = 0f;
    }

    // ── 阶段二 WholeMap staging 数据结构 ────────────────────────────
    internal sealed class TerrainGeometry
    {
        public Vector3[] Vertices;
        public Vector2[] UVs;
        public Color[] Colors;
        public int[][] SubMeshIndices;
        public Material[] BaseMaterials;
        public Material[] RectAs;
        public Material[] RectBs;
        public Material[] TriAs;
        public Material[] TriBs;
        public Material[] TriCs;
        public List<(int start, int count)> RectangleRanges;
        public List<(int start, int count)> TriangleRanges;
        public List<Vector3> VerticesList;

        // 【动态地图-阶段四】每顶点动画通道（§20-10）：UV2.x=startVertexY、UV2.y=targetVertexY；
        // UV3.x=错峰延迟、UV3.y=participatesInTransition(0/1)。WholeMap 后端保持 null（无动画）。
        public Vector2[] UV2s;
        public Vector2[] UV3s;
    }

    internal sealed class RiverGeometry
    {
        public Vector3[] Vertices;
        public Vector2[] UVs;
        public int[] Indices;
        public Color[] Colors;
    }

    internal sealed class WaterGeometry
    {
        public Vector3[] Vertices;
        public Vector2[] UVs;
        public int[][] Indices;
        public Color[] Colors;
    }
}

/// <summary>【动态地图-阶段二/三】WholeMap 后端 staging 产物（Prepare → Commit 传递）。
/// 阶段三 Chunked 后端复用它携带 Chunk staging（见 ChunkMapRenderer）。</summary>
public sealed class PreparedWholeMapGeometry
{
    internal MapRenderer.TerrainGeometry Terrain;
    internal MapRenderer.RiverGeometry River;
    internal MapRenderer.WaterGeometry Water;

    /// <summary>阶段三 Chunked 后端：全量 Chunk staging（WholeMap 后端为 null）。</summary>
    internal PreparedChunkGeometry Chunked;
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class FogEnvironmentSelectiveEffect : MonoBehaviour
{
    private static readonly int ObjectMaskId = Shader.PropertyToID("_FogAffectedObjectMask");
    private static readonly int SceneColorId = Shader.PropertyToID("_FogSceneColorTex");
    private static readonly int UnitUIRectsId = Shader.PropertyToID("_UnitUIRects");
    private static readonly int UnitUICountId = Shader.PropertyToID("_UnitUICount");

    private const int MaxUnitUIRects = 32;

    private readonly List<Renderer> _renderers = new List<Renderer>();
    private readonly List<Renderer> _alwaysRenderers = new List<Renderer>();
    private readonly List<Renderer> _eraseRenderers = new List<Renderer>();
    private readonly List<Canvas> _eraseCanvases = new List<Canvas>();
    private readonly Vector4[] _uiRects = new Vector4[MaxUnitUIRects];
    private readonly Vector3[] _uiCorners = new Vector3[4];
    private Camera _camera;
    private GameObject _landFormRoot;
    private GameObject _resourceRoot;
    private GameObject[] _fogRoots = new GameObject[0];
    private GameObject[] _eraseRoots = new GameObject[0];
    private Material _maskMaterial;
    private Material _maskAlwaysMaterial;
    private Material _eraseMaterial;
    private Material _eraseUIMaterial;
    private Mesh _eraseUIQuad;
    private Material _validationMaterial;
    private RenderTexture _objectMask;
    private CommandBuffer _maskCommands;
    private int _maskWidth;
    private int _maskHeight;
    private bool _initialized;

    /// <summary>
    /// 【断供方案-阶段5】fogRoots：额外纳入雾化对象遮罩的根节点
    ///（建筑根 PlayerBuilding/EnemyBuilding——断供地块上的建筑随地面一起被迷雾覆盖）。
    /// 【地貌/资源常驻遮罩】landFormRoot/resourceRoot 使用不依赖相机深度的常驻遮罩
    ///（FogEnvironmentObjectMaskAlways）：贴地/半埋模型（金矿等）的像素会随相机角度
    /// 被深度测试裁出遮罩，导致"拉近时从迷雾中显露"；地貌/资源的雾化只取决于地块
    /// 探索状态，与相机视角无关。被建筑遮挡的像素由后绘制的建筑遮罩覆盖。
    /// 【单位擦除层-方案A】eraseRoots：从雾化遮罩中"擦除"的根节点
    ///（单位根 PlayerUnit/EnemyUnit）——单位是透明队列不在相机深度纹理中，
    /// 对象遮罩的深度裁剪看不到单位，雾化会连带盖住单位；擦除 pass 用单位自身
    /// 深度与场景深度比较，把可见单位像素从遮罩清除（决策 8 单位不雾化）。
    /// </summary>
    public void Initialize(
        GameObject landFormRoot,
        GameObject resourceRoot,
        GameObject[] fogRoots,
        GameObject[] eraseRoots)
    {
        _camera = GetComponent<Camera>();
        _camera.depthTextureMode |= DepthTextureMode.Depth;
        _landFormRoot = landFormRoot;
        _resourceRoot = resourceRoot;
        _fogRoots = fogRoots ?? new GameObject[0];
        _eraseRoots = eraseRoots ?? new GameObject[0];

        if (!CreateMaterials())
        {
            enabled = false;
            return;
        }

        _initialized = true;
        RefreshRenderers();
    }

    public void RefreshRenderers()
    {
        if (!_initialized) return;

        _renderers.Clear();
        _alwaysRenderers.Clear();
        _eraseRenderers.Clear();
        _eraseCanvases.Clear();
        AddRenderers(_landFormRoot, _alwaysRenderers);
        AddRenderers(_resourceRoot, _alwaysRenderers);
        foreach (GameObject root in _fogRoots)
            AddRenderers(root, _renderers);
        foreach (GameObject root in _eraseRoots)
        {
            AddRenderers(root, _eraseRenderers);
            // 单位 UI（世界空间 Canvas）同步收集，用于屏幕矩形擦除
            if (root != null)
                _eraseCanvases.AddRange(root.GetComponentsInChildren<Canvas>(true));
        }
        EnsureMaskResources();
        RebuildMaskCommands();
    }

    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
        _camera.depthTextureMode |= DepthTextureMode.Depth;
        if (_initialized)
            RefreshRenderers();
    }

    private void OnPreCull()
    {
        if (!_initialized) return;
        EnsureMaskResources();
        UpdateEraseUIRects();
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (!_initialized || _validationMaterial == null || _objectMask == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        _validationMaterial.SetTexture(ObjectMaskId, _objectMask);
        // 不依赖 Graphics.Blit 对隐式 _MainTex 的绑定；该 Shader include 多套全局纹理后，
        // 部分平台/编辑器路径下 _MainTex 会采到默认灰纹理。
        _validationMaterial.SetTexture(SceneColorId, source);
        Graphics.Blit(source, destination, _validationMaterial);
    }

    private bool CreateMaterials()
    {
        if (_maskMaterial == null)
        {
            Shader maskShader = Shader.Find("Hidden/FogEnvironmentObjectMask");
            if (maskShader == null)
            {
                Debug.LogError("FogEnvironmentSelectiveEffect: 找不到 Hidden/FogEnvironmentObjectMask Shader。");
                return false;
            }
            _maskMaterial = new Material(maskShader) { hideFlags = HideFlags.HideAndDontSave };
        }

        if (_maskAlwaysMaterial == null)
        {
            Shader maskAlwaysShader = Shader.Find("Hidden/FogEnvironmentObjectMaskAlways");
            if (maskAlwaysShader == null)
            {
                Debug.LogError("FogEnvironmentSelectiveEffect: 找不到 Hidden/FogEnvironmentObjectMaskAlways Shader。");
                return false;
            }
            _maskAlwaysMaterial = new Material(maskAlwaysShader) { hideFlags = HideFlags.HideAndDontSave };
        }

        if (_validationMaterial == null)
        {
            Shader effectShader = Shader.Find("Hidden/FogEnvironmentSelective");
            if (effectShader == null)
            {
                Debug.LogError("FogEnvironmentSelectiveEffect: 找不到 Hidden/FogEnvironmentSelective Shader。");
                return false;
            }
            _validationMaterial = new Material(effectShader) { hideFlags = HideFlags.HideAndDontSave };
        }

        if (_eraseMaterial == null)
        {
            Shader eraseShader = Shader.Find("Hidden/FogEnvironmentUnitErase");
            if (eraseShader == null)
            {
                Debug.LogError("FogEnvironmentSelectiveEffect: 找不到 Hidden/FogEnvironmentUnitErase Shader。");
                return false;
            }
            _eraseMaterial = new Material(eraseShader) { hideFlags = HideFlags.HideAndDontSave };
        }

        if (_eraseUIMaterial == null)
        {
            Shader eraseUIShader = Shader.Find("Hidden/FogEnvironmentUnitUIErase");
            if (eraseUIShader == null)
            {
                Debug.LogError("FogEnvironmentSelectiveEffect: 找不到 Hidden/FogEnvironmentUnitUIErase Shader。");
                return false;
            }
            _eraseUIMaterial = new Material(eraseUIShader) { hideFlags = HideFlags.HideAndDontSave };
        }

        return true;
    }

    /// <summary>
    /// 【单位 UI 擦除】每帧把单位世界空间 Canvas 的屏幕矩形投影到遮罩坐标系，
    /// 写入擦除材质（CommandBuffer 执行时读取最新值）——UI 像素从雾化遮罩中清除，
    /// 与单位模型擦除同理（世界空间 UI 不写深度，遮罩深度裁剪看不到它）。
    /// 提示浮标不在此列：浮标已改用 MarkerOverlayCamera 叠加相机渲染，
    /// 矩形擦除会连带清除浮标周围地面/金矿模型的雾，故不采用。
    /// </summary>
    private void UpdateEraseUIRects()
    {
        if (_eraseUIMaterial == null || _camera == null) return;

        Rect pixelRect = _camera.pixelRect;
        int count = 0;

        for (int i = 0; i < _eraseCanvases.Count && count < MaxUnitUIRects; i++)
            AddEraseRect(_eraseCanvases[i], pixelRect, ref count);

        _eraseUIMaterial.SetVectorArray(UnitUIRectsId, _uiRects);
        _eraseUIMaterial.SetInt(UnitUICountId, count);
    }

    private void AddEraseRect(Canvas canvas, Rect pixelRect, ref int count)
    {
        if (canvas == null || !canvas.gameObject.activeInHierarchy) return;

        RectTransform rect = canvas.GetComponent<RectTransform>();
        if (rect == null) return;

        rect.GetWorldCorners(_uiCorners);

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        for (int c = 0; c < 4; c++)
        {
            Vector3 screen = _camera.WorldToScreenPoint(_uiCorners[c]);
            float u = (screen.x - pixelRect.xMin) / Mathf.Max(1f, pixelRect.width);
            float v = (screen.y - pixelRect.yMin) / Mathf.Max(1f, pixelRect.height);
            if (u < min.x) min.x = u;
            if (v < min.y) min.y = v;
            if (u > max.x) max.x = u;
            if (v > max.y) max.y = v;
        }

        // 完全离屏的 UI 无需擦除
        if (max.x <= 0f || max.y <= 0f || min.x >= 1f || min.y >= 1f) return;

        // 1 像素 padding，防边缘残留雾化
        float pad = 1f / Mathf.Max(1f, Mathf.Max(pixelRect.width, pixelRect.height));
        _uiRects[count++] = new Vector4(min.x - pad, min.y - pad, max.x + pad, max.y + pad);
    }

    private void EnsureMaskResources()
    {
        int width = Mathf.Max(1, _camera.pixelWidth);
        int height = Mathf.Max(1, _camera.pixelHeight);
        if (_objectMask != null && width == _maskWidth && height == _maskHeight) return;

        ReleaseMaskTexture();
        _maskWidth = width;
        _maskHeight = height;

        // RG 存模型片元的地图 UV，B 存有效标记；不能再用单通道 R8。
        RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
            ? RenderTextureFormat.ARGBHalf
            : RenderTextureFormat.ARGB32;
        _objectMask = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
        {
            name = "FogAffectedObjectMask",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false
        };
        _objectMask.Create();
        RebuildMaskCommands();
    }

    private void RebuildMaskCommands()
    {
        if (!_initialized || _objectMask == null || _maskMaterial == null || _maskAlwaysMaterial == null) return;

        RemoveMaskCommands();
        _maskCommands = new CommandBuffer { name = "Fog Environment Object Mask" };
        _maskCommands.SetRenderTarget(_objectMask);
        _maskCommands.ClearRenderTarget(false, true, Color.black);

        // 【地貌/资源常驻遮罩】先绘制（不依赖相机深度）：雾化只取决于地块探索状态，
        // 贴地/半埋模型不会因相机角度变化被裁出遮罩（"拉近从迷雾中显露"的根因）。
        foreach (Renderer renderer in _alwaysRenderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;

            int subMeshCount = GetSubMeshCount(renderer);
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                _maskCommands.DrawRenderer(renderer, _maskAlwaysMaterial, subMesh, 0);
        }

        // 建筑遮罩后绘制：深度裁剪保留（断供雾化语义），建筑像素覆盖前方地貌/资源遮罩。
        foreach (Renderer renderer in _renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;

            int subMeshCount = GetSubMeshCount(renderer);
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                _maskCommands.DrawRenderer(renderer, _maskMaterial, subMesh, 0);
        }

        // 【单位擦除层-方案A】先雾化对象、后擦除单位：单位覆盖的像素从遮罩清零，
        // 雾化不会连带盖住单位（CommandBuffer 每帧按当前变换重绘，移动的单位实时生效）。
        foreach (Renderer renderer in _eraseRenderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;

            int subMeshCount = GetSubMeshCount(renderer);
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                _maskCommands.DrawRenderer(renderer, _eraseMaterial, subMesh, 0);
        }

        // 【单位 UI 擦除】全屏 quad 按屏幕矩形把单位世界空间 UI（血条/图标）像素清零——
        // UI 像素处被雾化对象标记 B=1 时，后处理会连 UI 一起雾化，必须单独擦除。
        if (_eraseUIMaterial != null)
        {
            if (_eraseUIQuad == null)
                _eraseUIQuad = CreateFullScreenQuad();
            _maskCommands.DrawMesh(_eraseUIQuad, Matrix4x4.identity, _eraseUIMaterial, 0, 0);
        }

        // SetRenderTarget 会持续影响后续相机步骤，必须在图像效果前恢复颜色目标；
        // 否则 OnRenderImage 的 source 可能来自单通道对象遮罩而非场景颜色。
        _maskCommands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        _camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, _maskCommands);
    }

    private void AddRenderers(GameObject root, List<Renderer> target)
    {
        if (root == null) return;

        // 环境预制体可能附带 ParticleSystemRenderer、TrailRenderer 等特效。
        // 这些渲染器使用纯几何替换 Shader 重绘时可能生成覆盖全屏的错误遮罩，
        // 选择性雾化只标记实际模型表面。
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                target.Add(renderer);
        }
    }

    private static int GetSubMeshCount(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            return skinned.sharedMesh.subMeshCount;

        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        return filter != null && filter.sharedMesh != null ? filter.sharedMesh.subMeshCount : 1;
    }

    // 全屏 quad（clip 空间 -1..1），用于单位 UI 屏幕矩形擦除
    private static Mesh CreateFullScreenQuad()
    {
        var mesh = new Mesh { name = "FogUnitUIEraseQuad", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = new[]
        {
            new Vector3(-1f, -1f, 0f),
            new Vector3(1f, -1f, 0f),
            new Vector3(1f, 1f, 0f),
            new Vector3(-1f, 1f, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        return mesh;
    }

    private void OnDisable()
    {
        RemoveMaskCommands();
        ReleaseMaskTexture();
    }

    private void OnDestroy()
    {
        if (_maskMaterial != null) Destroy(_maskMaterial);
        if (_maskAlwaysMaterial != null) Destroy(_maskAlwaysMaterial);
        if (_eraseMaterial != null) Destroy(_eraseMaterial);
        if (_eraseUIMaterial != null) Destroy(_eraseUIMaterial);
        if (_eraseUIQuad != null) Destroy(_eraseUIQuad);
        if (_validationMaterial != null) Destroy(_validationMaterial);
    }

    private void RemoveMaskCommands()
    {
        if (_maskCommands == null) return;
        if (_camera != null)
            _camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _maskCommands);
        _maskCommands.Release();
        _maskCommands = null;
    }

    private void ReleaseMaskTexture()
    {
        if (_objectMask == null) return;
        _objectMask.Release();
        Destroy(_objectMask);
        _objectMask = null;
    }
}
