using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class MapRenderer : MonoBehaviour
{
    private readonly Dictionary<(int owner, Enums.HexDirection direction), RectangleTransitionMeshData> _genericRectangleMeshes
        = new Dictionary<(int owner, Enums.HexDirection direction), RectangleTransitionMeshData>();

    [Inject] private IMapDataService _mapDataService;
    [Inject] private IEnvironmentModelsProvider environmentModelsProvider;
    [Inject] private MapGenerationConfigSO _config;
    [Inject] private IMeshGenerator _meshGenerator;
    [Inject] private MapGenerator mapGenerator;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private FieldOfViewService _fieldOfView;
    [Inject] private IUnitRepository _unitRepository;

    private Mesh _terrainMesh;
    private Mesh _waterMesh;
    private Mesh _riverMesh;
    private List<HexCellData> _cellsInGenerateOrder;
    private bool _isSubscribed;

    private Color[] _cachedTerrainColors;
    private Color[] _cachedWaterColors;
    private Color[] _cachedRiverColors;
    private Dictionary<GameObject, Renderer[]> _enemyRendererCache;
    private Dictionary<GameObject, Canvas[]> _enemyCanvasCache;
    private List<int> _fogMaskStampedGIndices;

    // 方案B：世界对齐的探索遮罩贴图（像素锯齿边界用）。R 通道 0/1，双线性采样得到边界渐变带。
    private Texture2D _fogMaskTex;
    private Color32[] _fogMaskData;
    private Vector2 _fogMaskOrigin;   // 世界 (minX, minZ)，与 _FogMapOrigin 一致
    private Vector2 _fogMaskSize;     // 世界 (sizeX, sizeZ)，与 _FogMapSize 一致
    private int _fogMaskW, _fogMaskH;
    private readonly HashSet<HexCellData> _fogMaskStamped = new HashSet<HexCellData>();

    // ��Ⱦ��ͼ�Ӿ�����
    public void MapRender()
    {
        // 1. ��ȡ��������������
        Vector3[] hexVertices = _mapDataService.GetHexVertices();

        // 2. 设置迷雾全局 Shader 属性
        SetupFogGlobalShaderProperties(hexVertices);

        // 3. ���ɸ���Mesh
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

        // 3. ʵ������ò����Դģ��
        InstantiateLandForms(hexVertices);
        InstantiateResources(hexVertices);

        // 4. ���� - ʹ���¼�ϵͳ�����ɸ� FogManager ʵ�������ĳ�ʼ��
        _mapVisualEvent.FogInit();

    }

    //����ͼMesh����
    private void MainMapMeshCreat(Vector3[] hexVertices)
    {
        _genericRectangleMeshes.Clear();
        int cellCount = hexVertices.Length;
        List<Vector3> verticesList = new List<Vector3>(cellCount * 44);
        List<Vector2> uvList = new List<Vector2>(cellCount * 44);
        List<Color> allColors = new List<Color>(cellCount * 44);
        _cellsInGenerateOrder = new List<HexCellData>(cellCount);

        //�ߵػ���˳��
        List<int> highDrawOrderList = new List<int>();
        //ƽ�ػ���˳��
        List<int> flatDrawOrderList = new List<int>();
        //���׻���˳��
        List<int> seafloorDrawOrderList = new List<int>();
        //����˳��
        //�ӻ���˳�б�
        List<List<int>> subList = new List<List<int>>() { highDrawOrderList, flatDrawOrderList, seafloorDrawOrderList };

        //�����������˳��
        //����
        //���������˳��
        List<List<int>> transitionRectDrawOrderList = new List<List<int>>();
        //�Լ��Ĳ���
        List<Material> materialAs = new List<Material>();
        //�ھӵĲ���
        List<Material> materialBs = new List<Material>();

        //����
        //���������˳��
        List<List<int>> transitionTriDrawOrderList = new List<List<int>>();
        //����A�Ĳ��� - ˳ʱ������
        List<Material> materialAsTri = new List<Material>();
        //����B�Ĳ���
        List<Material> materialBsTri = new List<Material>();
        //����C�Ĳ���
        List<Material> materialCsTri = new List<Material>();

        //�����������˳�򣨺���û�ã���
        List<int> transitionDrawOrderList = new List<int>();

        //��ֵ����
        //ʵ������

        if(hexVertices == null)
        {
            Debug.Log("��������������Ϊnull��");
            return;
        }

        if (hexVertices.Length == 0)
        {
            Debug.Log("û�л�ȡ�����������꣡");
            return;
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

            //生成vertices
            verticesList.AddRange(_meshGenerator.GetSolidAreaVertices(ref hexCellData));
            //添加顶点色
            Color cellColor = FogVertexColor(hexCellData);
            for (int c = 0; c < 44; c++)
                allColors.Add(cellColor);

            //UV
            uvList.AddRange(_meshGenerator.GetSolidAreaVerticesUV(ref hexCellData));

            //�������˳��
            List<Enums.HexDirection> d;
            int index = MainMeshSolidAreaDrawOrderFunction(hexCellData, out d);
            List<int> ints = new List<int>();
            switch (index)
            {
                case 1:
                    ints = _meshGenerator.GetSolidAreaVerticesDrawOrder1(ref hexCellData);
                    break;
                case 2:
                    ints = _meshGenerator.GetSolidAreaVerticesDrawOrder2(ref hexCellData, d[0]);
                    break;
                case 3:
                    ints = _meshGenerator.GetSolidAreaVerticesDrawOrder3(ref hexCellData, d[0], d[1]);
                    break;
            }

            MainMeshDrawOrderElementAddRule(ref hexCellData, ints, ref subList, IndexOffset);
        }
        // 矩形过渡
        for (int j = 0; j < hexVertices.Length; j++)
        {
            //根据六边形顶点坐标取出hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            Color cellColor = FogVertexColor(hexCellData);
            //绘制顺序偏移
            int IndexOffset = verticesList.Count;
            Enums.HexDirection[] hexDirections = new Enums.HexDirection[3] { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE };
            for (int i = 0; i < hexDirections.Length; i++)
            {
                IndexOffset = verticesList.Count;
                if (_mapDataService.GetNeighbor(hexCellData, hexDirections[i]) == null) continue;
                bool isSlope = true, isRiver = false;
                MainMeshRectFunction(hexCellData, hexDirections[i], out isSlope, out isRiver);
                List<int> ints = new List<int>();

                // 始终生成通用矩形剖面并缓存：三角过渡（GetGenericTriangleMesh）会引用相邻矩形的
                // 表面边界剖面，河道边的表面边界与通用矩形一致，缺失会导致三角过渡查表抛异常。
                RectangleTransitionMeshData rectangle = GetGenericRectangleMesh(ref hexCellData, hexDirections[i]);

                if (isRiver)
                {
                    // 河道边：渲染带河道（侧壁+河床底面）的过渡几何，不使用通用矩形的渲染输出。
                    if (isSlope)
                    {
                        int preCount = verticesList.Count;
                        verticesList.AddRange(_meshGenerator.GetRectVertices(ref hexCellData, hexDirections[i], _mapDataService));
                        uvList.AddRange(_meshGenerator.GetRectUV(ref hexCellData, hexDirections[i], _mapDataService));
                        int addedCount = verticesList.Count - preCount;
                        for (int c = 0; c < addedCount; c++) allColors.Add(cellColor);
                        hexCellData.MeshTransitionVertexRanges.Add((preCount, addedCount));
                        OtherMeshDrawOrderElementAddRule(ref hexCellData, _meshGenerator.GetRectSlopeRiverDrawOrder(ref hexCellData, hexDirections[i], _mapDataService), ref ints, IndexOffset);
                    }
                    else
                    {
                        int preCount = verticesList.Count;
                        verticesList.AddRange(_meshGenerator.GetRectStepVertices(ref hexCellData, hexDirections[i], _mapDataService));
                        uvList.AddRange(_meshGenerator.GetRectStepUV(ref hexCellData, hexDirections[i], _mapDataService));
                        int addedCount = verticesList.Count - preCount;
                        for (int c = 0; c < addedCount; c++) allColors.Add(cellColor);
                        hexCellData.MeshTransitionVertexRanges.Add((preCount, addedCount));
                        OtherMeshDrawOrderElementAddRule(ref hexCellData, _meshGenerator.GetRectStepRiverDrawOrder(ref hexCellData, hexDirections[i], _mapDataService), ref ints, IndexOffset);
                    }
                }
                else
                {
                    int preCount = verticesList.Count;
                    RectangleTransitionMeshData flatRectangle = RectangleTransitionMesh.ToFlatShaded(rectangle);
                    verticesList.AddRange(flatRectangle.Vertices);
                    uvList.AddRange(flatRectangle.UVs);
                    int addedCount = verticesList.Count - preCount;
                    for (int c = 0; c < addedCount; c++) allColors.Add(cellColor);
                    hexCellData.MeshTransitionVertexRanges.Add((preCount, addedCount));
                    OtherMeshDrawOrderElementAddRule(ref hexCellData, flatRectangle.Indices, ref ints, IndexOffset);
                }

                materialAs.Add(HexController.SetHexMaterial(hexCellData, _config.mapMaterial));
                materialBs.Add(HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, hexDirections[i]), _config.mapMaterial));

                transitionDrawOrderList.AddRange(ints);
                transitionRectDrawOrderList.Add(ints);
            }

        }
        // 三角过渡
        for (int j = 0; j < hexVertices.Length; j++)
        {
            //根据六边形顶点坐标取出hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            Color cellColor = FogVertexColor(hexCellData);
            //绘制顺序偏移
            int IndexOffset = verticesList.Count;

            Enums.HexDirection[][] h = new Enums.HexDirection[2][]
            {
                new[] { Enums.HexDirection.NE, Enums.HexDirection.E },
                new[] { Enums.HexDirection.E, Enums.HexDirection.SE }
            };

            //每个地块有两个三角过渡区域NE_E和E_SE           
            for (int i = 0; i < 2; i++)
            {
                if (_mapDataService.GetNeighbor(hexCellData, h[i][0]) == null || _mapDataService.GetNeighbor(hexCellData, h[i][1]) == null) continue;
                IndexOffset = verticesList.Count;
                List<int> ints = new List<int>();
                int preCount = verticesList.Count;
                TriangleTransitionMeshData triangle = GetGenericTriangleMesh(ref hexCellData, h[i][0], h[i][1]);
                verticesList.AddRange(triangle.Vertices);
                uvList.AddRange(triangle.UVs);
                int addedCount = verticesList.Count - preCount;
                for (int c = 0; c < addedCount; c++) allColors.Add(cellColor);
                hexCellData.MeshTransitionVertexRanges.Add((preCount, addedCount));
                OtherMeshDrawOrderElementAddRule(ref hexCellData, triangle.Indices, ref ints, IndexOffset);

                materialAsTri.Add(HexController.SetHexMaterial(hexCellData, _config.mapMaterial));
                materialBsTri.Add(HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, h[i][0]), _config.mapMaterial));
                materialCsTri.Add(HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, h[i][1]), _config.mapMaterial));

                transitionDrawOrderList.AddRange(ints);
                transitionTriDrawOrderList.Add(ints);
            }
        }
        //����CreatMesh()
        //�����λ���˳��
        int[][] arrArawOrder = new int[3 + transitionRectDrawOrderList.Count + transitionTriDrawOrderList.Count][];
        //����
        arrArawOrder[0] = subList[2].ToArray();
        //ƽ��
        arrArawOrder[1] = subList[1].ToArray();
        //�ߵ�
        arrArawOrder[2] = subList[0].ToArray();
        //���ι�������

        int transitionOffset = 3;
        for (int i = 0; i < transitionRectDrawOrderList.Count; i++)
        {
            arrArawOrder[transitionOffset + i] = transitionRectDrawOrderList[i].ToArray();
        }
        //���ǹ�������
        transitionOffset = transitionRectDrawOrderList.Count + 3;
        for (int i = 0; i < transitionTriDrawOrderList.Count; i++)
        {
            arrArawOrder[transitionOffset + i] = transitionTriDrawOrderList[i].ToArray();
        }

        // 创建主体地图 Mesh，并将临时数据存储到 mapGenerator
        Color[] terrainColorArray = allColors.ToArray();
        _cachedTerrainColors = terrainColorArray;
        Mesh mainMesh = MapController.CreatMesh(
                   verticesList.ToArray(),
                   uvList.ToArray(),
                   terrainColorArray,
                   arrArawOrder,
                   _mapDataService.MapGameObject,
                   _config.mapMaterial,
                   materialAs.ToArray(),
                   materialBs.ToArray(),

                   materialAsTri.ToArray(),
                   materialBsTri.ToArray(),
                   materialCsTri.ToArray(),
                   _config.blendMask,
                   _config.blendContrast,
                   _config.blendSmooth,
                   _config.globalSmoothness
                  );

        // 将生成的顶点列表和 Mesh 传给 MapGenerator（供全局使用）
        if (mapGenerator != null)
        {
            mapGenerator.verticesList = verticesList;
            mapGenerator.mesh = mainMesh;
        }
        _terrainMesh = mainMesh;
    }

    //����Mesh����
    private void RiverMeshCreat(Vector3[] hexVertices)
    {
        int cellCount = hexVertices.Length;
        GameObject RiverWater = new GameObject("RiverWater");
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
            verticesRiverWater.AddRange(_meshGenerator.GetRiverVertices(ref hexCellData));
            //�������˳��
            List<int> l = new List<int>();
            l = RiverMeshSolidAreaDrawOrderFunction(hexCellData);
            ints.Clear();
            OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
            drawOrderRiverWater.AddRange(ints);
            //UV
            uvRiverWater.AddRange(_meshGenerator.GetRiverUV(ref hexCellData, l));
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
            verticesRiverWater.AddRange(_meshGenerator.GetOutgoingRiverVertices(ref hexCellData, _mapDataService));

            //�������˳��
            List<int> l = new List<int>();
            l.AddRange(RiverMeshDownstreamDrawOrderFunction(ref hexCellData));
            OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
            drawOrderRiverWater.AddRange(ints);
            //UV
            uvRiverWater.AddRange(_meshGenerator.GetOutgoingRiverSlopUV(ref hexCellData));
            RecordRiver(hexCellData, IndexOffset);
        }

        //����CreatMesh()
        if (drawOrderRiverWater.Count % 3 == 0 && drawOrderRiverWater.Count != 0)
        {

            Color[] riverColorArray = riverColors.ToArray();
            _cachedRiverColors = riverColorArray;
            _riverMesh = MapController.CreatMesh(verticesRiverWater.ToArray(), uvRiverWater.ToArray(), drawOrderRiverWater.ToArray(), RiverWater, _config.riverMaterial, riverColorArray);
        }
    }

    //����Mesh����
    private void LakeOrSeaMeshCreat(Vector3[] hexVertices)
    {
        int cellCount = hexVertices.Length;
        GameObject LakeOrSea = new GameObject("LakeOrSea");
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
            verticesLakeOrSea.AddRange(_meshGenerator.GetlakeOrSeaVertices(ref hexCellData));
            //UV
            uvLakeOrSea.AddRange(_meshGenerator.GetlakeOrSeaUV(ref hexCellData));
            RecordWater(hexCellData, IndexOffset);

            //�������˳��
            List<int> l = new List<int>();
            l.AddRange(LakeOrSeaMeshSolidAreaDrawOrderFunction(ref hexCellData));
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
                if (_mapDataService.GetNeighbor(hexCellData, hexDirections[i]) != null && _mapDataService.GetNeighbor(hexCellData, hexDirections[i]).lakeOrSeaVertices.Count != 0)
                {
                    //��������
                    IndexOffset = verticesLakeOrSea.Count;
                    verticesLakeOrSea.AddRange(_meshGenerator.GetlakeOrSeaRectVertices(ref hexCellData, hexDirections[i], _mapDataService));
                    //UV
                    uvLakeOrSea.AddRange(_meshGenerator.GetlakeOrSeaRectUV(ref hexCellData, hexDirections[i], _mapDataService));
                    RecordWater(hexCellData, IndexOffset);
                    //����˳��          
                    List<int> l = new List<int>();
                    l.AddRange(LakeOrSeaMeshRectDrawOrderFunction(hexCellData, hexDirections[i]));
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
                if (_mapDataService.GetNeighbor(hexCellData, h[i][0]) != null &&
                    _mapDataService.GetNeighbor(hexCellData, h[i][1]) != null &&
                    _mapDataService.GetNeighbor(hexCellData, h[i][0]).lakeOrSeaVertices.Count != 0 &&
                    _mapDataService.GetNeighbor(hexCellData, h[i][1]).lakeOrSeaVertices.Count != 0)
                {
                    //��������
                    IndexOffset = verticesLakeOrSea.Count;
                    verticesLakeOrSea.AddRange(_meshGenerator.GetlakeOrSeaTriVertices(ref hexCellData, h[i][0], h[i][1], _mapDataService));
                    //UV
                    uvLakeOrSea.AddRange(_meshGenerator.GetlakeOrSeaTriUV(ref hexCellData, h[i][0], h[i][1], _mapDataService));
                    RecordWater(hexCellData, IndexOffset);

                    //���ǻ���˳��
                    List<int> l = new List<int>();
                    l.AddRange(LakeOrSeaMeshTriDrawOrderFunction(hexCellData, h[i][0], h[i][1]));
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
            List<Vector3> v = new List<Vector3>();
            foreach (Enums.HexDirection h in coastDirections)
            {
                v.AddRange(_meshGenerator.GetOneDirectionCoastRectVertices(ref hexCellData, h, _mapDataService));
            }
            verticesLakeOrSea.AddRange(v);
            //UV
            uvLakeOrSea.AddRange(_meshGenerator.GetCoastRectUV(ref hexCellData, v.ToArray()));
            RecordWater(hexCellData, IndexOffset);
            //���ǻ���˳��    
            List<int> l = new List<int>();
            l.AddRange(CoastMeshRectDrawOrderFunction(hexCellData, v.ToArray()));
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
            List<Vector3> v = new List<Vector3>();
            foreach (Enums.HexDirection h in coastDirections)
            {
                v.AddRange(_meshGenerator.GetOneDirectionCoastTriVertices(ref hexCellData, h, _mapDataService));
            }
            verticesLakeOrSea.AddRange(v);
            //UV
            uvLakeOrSea.AddRange(_meshGenerator.GetCoastTriUV(ref hexCellData, v.ToArray()));
            RecordWater(hexCellData, IndexOffset);
            //���ǻ���˳��
            List<int> l = new List<int>();
            l.AddRange(CoastMeshTriDrawOrderFunction(hexCellData, v.ToArray()));
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
        _waterMesh = MapController.CreatMesh(verticesLakeOrSea.ToArray(), uvLakeOrSea.ToArray(), arrArawOrderLakeOrSea, LakeOrSea, _config.lakeOrSeaMaterial, waterColorArray);

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
            verticesGridLine.AddRange(_meshGenerator.GetGridVertices(ref hexCellData));
            //UV
            uvGridLine.AddRange(_meshGenerator.GetGridUV(ref hexCellData));
            //�������˳��
            List<int> l = new List<int>();
            l.AddRange(_meshGenerator.GetGridDrawOrder(ref hexCellData));
            ints.Clear();
            OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
            drawOrderGridLine.Add(ints);
        }

        Shader gridLineShader = Shader.Find("Custom/GridLine") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Hidden/InternalErrorShader");
        //Ϊÿ���ؿ������ߵ�������һ��GameObject
        for (int j = 0, i = 0; j < hexVertices.Length; j++)
        {
            //���������������ȡhexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            //���ǡ����򺣡�������
            if (isLakeOrSea(hexCellData)) { continue; }

            GameObject go = new GameObject($"SubGridLine_{j}");
            MapController.CreatMesh(verticesGridLine.ToArray(), uvGridLine.ToArray(), drawOrderGridLine[i++].ToArray(), go, new Material(gridLineShader));
            hexCellData.GridMesh = go;
            go.SetActive(false);
            go.transform.parent = GridLine.transform;

        }
        mapGenerator.gridGameObject = GridLine;
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
        ref HexCellData hexCellData,
        Enums.HexDirection directionA,
        Enums.HexDirection directionB)
    {
        if (directionA == Enums.HexDirection.NE && directionB == Enums.HexDirection.E)
        {
            HexCellData neighborNE = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE);
            // 渲染前展开为逐面独立顶点（flat shading），避免 center-fan 中心枢纽顶点被平均出发暗的法线。
            return TriangleTransitionMesh.ToFlatShaded(RectangleDrivenTriangleMesh.BuildNEE(
                GetRectangleMesh(hexCellData, Enums.HexDirection.NE),
                GetRectangleMesh(neighborNE, Enums.HexDirection.SE),
                GetRectangleMesh(hexCellData, Enums.HexDirection.E)));
        }
        if (directionA == Enums.HexDirection.E && directionB == Enums.HexDirection.SE)
        {
            HexCellData neighborSE = _mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE);
            return TriangleTransitionMesh.ToFlatShaded(RectangleDrivenTriangleMesh.BuildESE(
                GetRectangleMesh(hexCellData, Enums.HexDirection.E),
                GetRectangleMesh(neighborSE, Enums.HexDirection.NE),
                GetRectangleMesh(hexCellData, Enums.HexDirection.SE)));
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
        ref HexCellData hexCellData,
        Enums.HexDirection direction)
    {
        HexCellData neighbor = _mapDataService.GetNeighbor(hexCellData, direction);
        var starts = new List<Vector3>(4);
        var ends = new List<Vector3>(4);

        switch (direction)
        {
            case Enums.HexDirection.NE:
                starts.Add(hexCellData.SolidAreaVertices[1]);
                starts.Add(hexCellData.SolidAreaVertices[7]);
                starts.Add(hexCellData.SolidAreaVertices[8]);
                starts.Add(hexCellData.SolidAreaVertices[2]);
                ends.Add(neighbor.SolidAreaVertices[5]);
                ends.Add(neighbor.SolidAreaVertices[14]);
                ends.Add(neighbor.SolidAreaVertices[13]);
                ends.Add(neighbor.SolidAreaVertices[4]);
                break;
            case Enums.HexDirection.E:
                starts.Add(hexCellData.SolidAreaVertices[2]);
                starts.Add(hexCellData.SolidAreaVertices[9]);
                starts.Add(hexCellData.SolidAreaVertices[10]);
                starts.Add(hexCellData.SolidAreaVertices[3]);
                ends.Add(neighbor.SolidAreaVertices[6]);
                ends.Add(neighbor.SolidAreaVertices[16]);
                ends.Add(neighbor.SolidAreaVertices[15]);
                ends.Add(neighbor.SolidAreaVertices[5]);
                break;
            case Enums.HexDirection.SE:
                starts.Add(hexCellData.SolidAreaVertices[3]);
                starts.Add(hexCellData.SolidAreaVertices[11]);
                starts.Add(hexCellData.SolidAreaVertices[12]);
                starts.Add(hexCellData.SolidAreaVertices[4]);
                ends.Add(neighbor.SolidAreaVertices[1]);
                ends.Add(neighbor.SolidAreaVertices[18]);
                ends.Add(neighbor.SolidAreaVertices[17]);
                ends.Add(neighbor.SolidAreaVertices[6]);
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

    //����
    private List<Vector3> GetTriVerticesFunction(Enums.TriType triType, ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1)
    {
        List<Vector3> triVertices = new List<Vector3>();
        switch (triType)
        {
            case Enums.TriType.one:
                return _meshGenerator.GetTriVertices(ref hexCellData, direction0, direction1, _mapDataService);
            case Enums.TriType.two:
                Debug.Log("���޴˷���");
                return triVertices;
            case Enums.TriType.three:
                return _meshGenerator.GetTriStep3Vertices(ref hexCellData, direction0, direction1, _mapDataService);
            case Enums.TriType.four:
                return _meshGenerator.GetTriStep4Vertices(ref hexCellData, direction0, direction1, _mapDataService);
            default:
                Debug.Log("�����TriType����");
                return triVertices;
        }
    }
    //UV
    private List<Vector2> GetTriUVFunction(Enums.TriType triType, ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1)
    {
        List<Vector2> triUV = new List<Vector2>();
        switch (triType)
        {
            case Enums.TriType.one:
                return _meshGenerator.GetTriUV(ref hexCellData, direction0, direction1, _mapDataService);
            case Enums.TriType.two:
                Debug.Log("���޴˷���");
                return triUV;
            case Enums.TriType.three:
                return _meshGenerator.GetTriStep3UV(ref hexCellData, direction0, direction1);
            case Enums.TriType.four:
                return _meshGenerator.GetTriStep4UV(ref hexCellData, direction0, direction1);
            default:
                Debug.Log("�����TriType����");
                return triUV;
        }
    }
    //����˳��
    private List<int> GetTriDrawOrderFunction(Enums.TriType triType, ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1)
    {
        List<int> triUV = new List<int>();
        switch (triType)
        {
            case Enums.TriType.one:
                return _meshGenerator.GetTriDrawOrder(ref hexCellData, direction0, direction1, _mapDataService);
            case Enums.TriType.two:
                Debug.Log("���޴˷���");
                return triUV;
            case Enums.TriType.three:
                return _meshGenerator.GetTriStep3DrawOrder(ref hexCellData, direction0, direction1);
            case Enums.TriType.four:
                return _meshGenerator.GetTriStep4DrawOrder(ref hexCellData, direction0, direction1, _mapDataService);
            default:
                Debug.Log("�����TriType����");
                return triUV;
        }
    }

    //��ˮMeshʵ���������˳��ѡ���߼�
    private List<int> RiverMeshSolidAreaDrawOrderFunction(HexCellData hexCellData)
    {
        switch (hexCellData.HexType)
        {
            case Enums.HexType.RiverSource:
                return _meshGenerator.GetRiverWater2DrawOrder(hexCellData.RiverOutgoingDirection);
            case Enums.HexType.RiverMidstream:
                return _meshGenerator.GetRiverWater3DrawOrder(ref hexCellData);
            case Enums.HexType.RiverEnd:
                return _meshGenerator.GetRiverWater2DrawOrder(hexCellData.RiverIncomingDirection);
            default:
                //Debug.Log("��������Ӧ���ɵ���˴�");
                return null;
        }
    }

    //��ˮMesh���ι����������˳��ѡ���߼�
    private int[] RiverMeshDownstreamDrawOrderFunction(ref HexCellData hexCellData)
    {
        return _meshGenerator.GetOutgoingRiverSlopDrawOrder(ref hexCellData);
    }

    //����Meshʵ���������˳��ѡ���߼�
    private int[] LakeOrSeaMeshSolidAreaDrawOrderFunction(ref HexCellData hexCellData)
    {
        return _meshGenerator.GetlakeOrSeaDrawOrder(ref hexCellData);
    }

    //����Mesh���ι����������˳��ѡ���߼�
    private List<int> LakeOrSeaMeshRectDrawOrderFunction(HexCellData hexCellData, Enums.HexDirection direction)
    {
        if (_mapDataService.GetNeighbor(hexCellData, direction) != null && _mapDataService.GetNeighbor(hexCellData, direction).lakeOrSeaVertices.Count != 0)
        {
            return _meshGenerator.GetlakeOrSeaRectDrawOrder(ref hexCellData, direction, _mapDataService);
        }
        else
        {
            Debug.Log("���������������Ӧ��������");
            return null;
        }
    }

    //����Mesh���ǹ����������˳��ѡ���߼�
    private List<int> LakeOrSeaMeshTriDrawOrderFunction(HexCellData hexCellData, Enums.HexDirection directionA, Enums.HexDirection directionB)
    {
        if (_mapDataService.GetNeighbor(hexCellData, directionA) != null &&
            _mapDataService.GetNeighbor(hexCellData, directionB) != null &&
            _mapDataService.GetNeighbor(hexCellData, directionA).lakeOrSeaVertices.Count != 0 &&
            _mapDataService.GetNeighbor(hexCellData, directionB).lakeOrSeaVertices.Count != 0)
        {
            return _meshGenerator.GetlakeOrSeaTriDrawOrder(ref hexCellData, directionA, directionB, _mapDataService);
        }
        else
        {
            Debug.Log("�����ˣ�������������ܵ���");
            return null;
        }
    }

    //����Mesh���ι����������˳��ѡ���߼�
    private List<int> CoastMeshRectDrawOrderFunction(HexCellData hexCellData, Vector3[] v)
    {
        return _meshGenerator.GetCoastRectDrawOrder(ref hexCellData, v);
    }

    //����Mesh���ǹ����������˳��ѡ���߼�
    private List<int> CoastMeshTriDrawOrderFunction(HexCellData hexCellData, Vector3[] v)
    {
        return _meshGenerator.GetCoastTriDrawOrder(ref hexCellData, v);
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
        GameObject landForm = new GameObject("LandForm");

        for (int j = 0; j < hexVertices.Length; j++)
        {
            //���������������ȡhexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            if((int)hexCellData.landFormType == 4) { continue; }
            hexCellData.landFormModel = Instantiate(environmentModelsProvider.GetLandFormPrefab((int)hexCellData.landFormType));
            hexCellData.landFormModel.transform.position = hexCellData.RealCenterWorldCoordinate + new Vector3(0, 0, 0);
            hexCellData.landFormModel.AddComponent<ModelController>();
            hexCellData.landFormModel.transform.SetParent(landForm.transform);
        }
           
    }
    private void InstantiateResources(Vector3[] hexVertices)
    {
        GameObject Resource = new GameObject("Resource");
        for (int j = 0; j < hexVertices.Length; j++)
        {
            //���������������ȡhexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            if ((int)hexCellData.resourceType >= 4) { continue; }
            hexCellData.resourceModel = Instantiate(environmentModelsProvider.GetResourcePrefab((int)hexCellData.resourceType));
            hexCellData.resourceModel.transform.position = hexCellData.RealCenterWorldCoordinate + new Vector3(0, 0, 0);
            hexCellData.resourceModel.AddComponent<ModelController>();
            hexCellData.resourceModel.transform.SetParent(Resource.transform);
        }

    }

    private void Awake()
    {
        Subscribe();
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        if (!_isSubscribed || _mapVisualEvent == null) return;
        _mapVisualEvent.OnMapVisualChanged.RemoveListener(OnMapVisualChanged);

        if (_unitRepository != null)
        {
            _unitRepository.OnEnemyUnitAdded -= OnEnemyUnitAdded;
            _unitRepository.OnEnemyUnitRemoved -= OnEnemyUnitRemoved;
        }

        _isSubscribed = false;
    }

    private void Subscribe()
    {
        if (_isSubscribed || _mapVisualEvent == null) return;
        _mapVisualEvent.OnMapVisualChanged.AddListener(OnMapVisualChanged);

        if (_unitRepository != null)
        {
            _unitRepository.OnEnemyUnitAdded += OnEnemyUnitAdded;
            _unitRepository.OnEnemyUnitRemoved += OnEnemyUnitRemoved;
        }

        _isSubscribed = true;
    }

    private void OnMapVisualChanged()
    {
        // 先重算视野（IsVisible），再刷视觉——保证"先算可见性、再画"。
        _fieldOfView?.Recompute();
        UpdateExplorationVisuals();
        UpdateFogMask();
        SyncCellObjectVisibility();
    }

    private void OnEnemyUnitAdded(int aiIndex, GameObject unitGO, CharacterData data)
    {
        if (unitGO == null) return;
        if (_enemyRendererCache == null)
            _enemyRendererCache = new Dictionary<GameObject, Renderer[]>();
        if (_enemyCanvasCache == null)
            _enemyCanvasCache = new Dictionary<GameObject, Canvas[]>();
        _enemyRendererCache[unitGO] = unitGO.GetComponentsInChildren<Renderer>(true);
        _enemyCanvasCache[unitGO] = unitGO.GetComponentsInChildren<Canvas>(true);
    }

    private void OnEnemyUnitRemoved(GameObject unitGO)
    {
        if (unitGO == null) return;
        _enemyRendererCache?.Remove(unitGO);
        _enemyCanvasCache?.Remove(unitGO);
    }

    /// <summary>
    /// 集中式物体可见性同步（三态记忆迷雾，归属×状态规则）：
    ///  - 中立地物（地貌/资源）：按所在格 IsExplored 显隐（记忆区仍显示）。
    ///  - 己方单位/建筑：永远可见，跳过。
    ///  - 敌方建筑/城市（静态）：按所在格 IsExplored 显隐（记忆区显示当前真实状态，无快照）。
    ///  - 敌方单位（动态）：按所在格 IsVisible 只关渲染（Renderer.enabled），
    ///    保留移动/AI/回合逻辑，避免迷雾里的敌人冻结。
    /// </summary>
    private void SyncCellObjectVisibility()
    {
        var cells = _mapDataService?.GetAllCells();
        if (cells == null) return;

        foreach (var cell in cells)
        {
            if (cell == null) continue;

            // 中立地物：地貌与资源模型
            if (cell.landFormModel != null && cell.landFormModel.activeSelf != cell.IsExplored)
                cell.landFormModel.SetActive(cell.IsExplored);
            if (cell.resourceModel != null && cell.resourceModel.activeSelf != cell.IsExplored)
                cell.resourceModel.SetActive(cell.IsExplored);

            // 建筑（含城市）：己方永远可见；敌方按 IsExplored 显隐
            GameObject building = cell.BulidingTypeOnHex_Building.Value;
            if (building != null)
            {
                bool isEnemyBuilding = cell.Player_City_Index.Key > 0
                    || building.CompareTag("EnemyBuilding");
                bool show = !isEnemyBuilding || cell.IsExplored;
                if (building.activeSelf != show)
                    building.SetActive(show);
            }
        }

        // 敌方单位：始终保持 GameObject 激活（保留移动/AI/回合逻辑——UnitMovementController
        // 的协程/Update 需物体激活才能运行，否则迷雾里的敌人无法移动、AI 回合会卡住），
        // 只用 Renderer/Canvas.enabled 控制可见性。按所在格 IsVisible 显隐（记忆区隐藏）。
        if (_unitRepository != null)
        {
            foreach (var group in _unitRepository.AllEnemyUnitGroups)
            {
                foreach (var kv in group)
                {
                    GameObject unitGO = kv.Key;
                    if (unitGO == null) continue;
                    if (!unitGO.activeSelf) unitGO.SetActive(true);

                    HexCellData cell = _mapDataService.GetCellByWorldPosition(unitGO.transform.position);
                    bool visible = cell != null && cell.IsExplored;

                    Renderer[] cachedRenderers = null;
                    Canvas[] cachedCanvases = null;
                    bool hasRendererCache = _enemyRendererCache != null && _enemyRendererCache.TryGetValue(unitGO, out cachedRenderers);
                    bool hasCanvasCache = _enemyCanvasCache != null && _enemyCanvasCache.TryGetValue(unitGO, out cachedCanvases);

                    if (hasRendererCache)
                    {
                        foreach (var r in cachedRenderers)
                            if (r != null) r.enabled = visible;
                    }
                    else
                    {
                        foreach (var r in unitGO.GetComponentsInChildren<Renderer>(true))
                            r.enabled = visible;
                    }

                    if (hasCanvasCache)
                    {
                        foreach (var c in cachedCanvases)
                            if (c != null) c.enabled = visible;
                    }
                    else
                    {
                        foreach (var c in unitGO.GetComponentsInChildren<Canvas>(true))
                            c.enabled = visible;
                    }
                }
            }
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

        // 方案B：按同一包围盒新建探索遮罩并绑定为全局纹理（内容由 UpdateFogMask 增量盖章）。
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

    // 三态记忆迷雾顶点色编码：.r=IsExplored(0/1)、.g=IsVisible(0/1)。
    // Shader 据此区分 未探索(r=0) / 记忆区(r=1,g=0,压暗) / 可见(r=1,g=1,全亮)。
    private static Color FogVertexColor(HexCellData cell)
    {
        float r = cell.IsExplored ? 1f : 0f;
        float g = cell.IsVisible ? 1f : 0f;
        return new Color(r, g, 0f, 1f);
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
        _fogMaskStamped.Clear();
    }

    // 把一格的六边形足迹盖章进遮罩的指定通道（toGreen=false→R 已探索；true→G 当前视野）。
    // 用【精确正六边形】填充（尖顶朝±z、平边在±x，内切半径=InnerRadius）：六边形能完美平铺，
    // 因此已探索区无缝、无外扩，基础边界正好落在六边形棱上（最贴边）。只改目标通道字节，不动其它通道。
    private void StampCellToFogMask(HexCellData cell, bool toGreen, List<int> collectIndices = null)
    {
        if (_fogMaskData == null) return;
        Vector3 c = cell.CenterWorldCoordinate;
        float o = _config != null ? _config.OuterRadius : 3f;    // 外接半径（顶点距离，仅用于 AABB）
        float ir = _config != null ? _config.InnerRadius : 2.598f; // 内切半径 apothem（三条边法向上的判定阈值）
        const float H = 0.8660254f; // sin60/cos30

        int px0 = Mathf.Clamp(Mathf.FloorToInt((c.x - o - _fogMaskOrigin.x) / _fogMaskSize.x * _fogMaskW), 0, _fogMaskW - 1);
        int px1 = Mathf.Clamp(Mathf.CeilToInt ((c.x + o - _fogMaskOrigin.x) / _fogMaskSize.x * _fogMaskW), 0, _fogMaskW - 1);
        int py0 = Mathf.Clamp(Mathf.FloorToInt((c.z - o - _fogMaskOrigin.y) / _fogMaskSize.y * _fogMaskH), 0, _fogMaskH - 1);
        int py1 = Mathf.Clamp(Mathf.CeilToInt ((c.z + o - _fogMaskOrigin.y) / _fogMaskSize.y * _fogMaskH), 0, _fogMaskH - 1);

        for (int py = py0; py <= py1; py++)
        {
            float wz = _fogMaskOrigin.y + (py + 0.5f) / _fogMaskH * _fogMaskSize.y;
            for (int px = px0; px <= px1; px++)
            {
                float wx = _fogMaskOrigin.x + (px + 0.5f) / _fogMaskW * _fogMaskSize.x;
                float dx = wx - c.x, dz = wz - c.z;
                // 点在正六边形内 ⇔ 在三条边法向(0°, 60°, 120°)上的投影都 ≤ 内切半径 ir
                if (Mathf.Abs(dx) <= ir &&
                    Mathf.Abs(0.5f * dx + H * dz) <= ir &&
                    Mathf.Abs(-0.5f * dx + H * dz) <= ir)
                {
                    int idx = py * _fogMaskW + px;
                    Color32 col = _fogMaskData[idx];
                    if (toGreen) { col.g = 255; collectIndices?.Add(idx); }
                    else col.r = 255;
                    _fogMaskData[idx] = col;
                }
            }
        }
    }

    // 更新探索遮罩：R=已探索（单调，增量盖章）、G=当前视野（动态，每回合清空后重盖）。
    private void UpdateFogMask()
    {
        if (_fogMaskTex == null || _fogMaskData == null || _cellsInGenerateOrder == null) return;

        // R 通道（已探索，单调只增）：只盖新探索的格子，盖过的跳过。
        foreach (var cell in _cellsInGenerateOrder)
        {
            if (cell == null || !cell.IsExplored) continue;
            if (!_fogMaskStamped.Add(cell)) continue; // Add 返回 false = 已盖过
            StampCellToFogMask(cell, false);
        }

        // G 通道：增量更新——先清空上一轮的 G 像素，再盖本轮可见格。
        if (_fogMaskStampedGIndices != null)
        {
            foreach (int idx in _fogMaskStampedGIndices)
            {
                Color32 col = _fogMaskData[idx];
                col.g = 0;
                _fogMaskData[idx] = col;
            }
            _fogMaskStampedGIndices.Clear();
        }
        else
        {
            _fogMaskStampedGIndices = new List<int>(1024);
        }

        foreach (var cell in _cellsInGenerateOrder)
        {
            if (cell != null && cell.IsVisible)
                StampCellToFogMask(cell, true, _fogMaskStampedGIndices);
        }

        // R 可能新增、G 每回合都变 → 整图上传（纹理很小，成本可忽略）。
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
}
