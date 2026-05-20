using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class MapRenderer : MonoBehaviour
{

    [Inject] private IMapDataService _mapDataService;
    [Inject] private IEnvironmentModelsProvider environmentModelsProvider;
    [Inject] private MapGenerationConfigSO _config;
    [Inject] private IMeshGenerator _meshGenerator;
    [Inject] private MapGenerator mapGenerator;
    [Inject] private MapVisualEventSO _mapVisualEvent;

    // 渲染地图视觉部分
    public void MapRender()
    {
        // 1. 获取所有六边形坐标
        Vector3[] hexVertices = _mapDataService.GetHexVertices();

        // 2. 生成各种Mesh
        //主地图Mesh创建
        MainMapMeshCreat(hexVertices);
        //河流Mesh创建
        RiverMeshCreat(hexVertices);
        //湖或海Mesh创建
        LakeOrSeaMeshCreat(hexVertices);
        //网格Mesh创建
        GridMeshCreat(hexVertices);

        // 更新 IMapDataService 的运行时数据（verticesList、mesh、gridGameObject）
        // 这些在上面的创建步骤中被赋值到 mapGenerator
        if (_mapDataService != null && mapGenerator != null)
        {
            _mapDataService.UpdateRuntimeData(mapGenerator.verticesList, mapGenerator.mesh, mapGenerator.gridGameObject);
        }

        // 3. 实例化地貌和资源模型
        InstantiateLandForms(hexVertices);
        InstantiateResources(hexVertices);

        // 4. 迷雾 - 使用事件系统触发由各 FogManager 实例处理的初始化
        _mapVisualEvent.FogInit();

    }

    //主地图Mesh创建
    private void MainMapMeshCreat(Vector3[] hexVertices)
    {
        //声明变量
        List<Vector3> verticesList = new List<Vector3>();
        //UV 
        List<Vector2> uvList = new List<Vector2>();
        //高地绘制顺序
        List<int> highDrawOrderList = new List<int>();
        //平地绘制顺序
        List<int> flatDrawOrderList = new List<int>();
        //海底绘制顺序
        List<int> seafloorDrawOrderList = new List<int>();
        //绘制顺序
        //子绘制顺列表
        List<List<int>> subList = new List<List<int>>() { highDrawOrderList, flatDrawOrderList, seafloorDrawOrderList };

        //过渡区域绘制顺序
        //矩形
        //子网格绘制顺序
        List<List<int>> transitionRectDrawOrderList = new List<List<int>>();
        //自己的材质
        List<Material> materialAs = new List<Material>();
        //邻居的材质
        List<Material> materialBs = new List<Material>();

        //三角
        //子网格绘制顺序
        List<List<int>> transitionTriDrawOrderList = new List<List<int>>();
        //顶点A的材质 - 顺时针排序
        List<Material> materialAsTri = new List<Material>();
        //顶点B的材质
        List<Material> materialBsTri = new List<Material>();
        //顶点C的材质
        List<Material> materialCsTri = new List<Material>();

        //过渡区域绘制顺序（好像没用？）
        List<int> transitionDrawOrderList = new List<int>();

        //赋值变量
        //实心区域

        if(hexVertices == null)
        {
            Debug.Log("六边形坐标数组为null！");
            return;
        }

        if (hexVertices.Length == 0)
        {
            Debug.Log("没有获取到六边形坐标！");
            return;
        }

        for (int j = 0; j < hexVertices.Length; j++)
        {
            //根据六边形坐标获取hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            //绘制顺序偏移
            int IndexOffset = verticesList.Count;

            //顶点坐标vertices
            verticesList.AddRange(_meshGenerator.GetSolidAreaVertices(ref hexCellData));

            //UV
            uvList.AddRange(_meshGenerator.GetSolidAreaVerticesUV(ref hexCellData));

            //顶点绘制顺序
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
        //矩形区域
        for (int j = 0; j < hexVertices.Length; j++)
        {
            //根据六边形坐标获取hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            //绘制顺序偏移
            int IndexOffset = verticesList.Count;
            Enums.HexDirection[] hexDirections = new Enums.HexDirection[3] { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE };
            for (int i = 0; i < hexDirections.Length; i++)
            {
                IndexOffset = verticesList.Count;
                if (_mapDataService.GetNeighbor(hexCellData, hexDirections[i]) == null) continue;
                bool isSlope = true, isRiver = false;
                MainMeshRectFunction(hexCellData, hexDirections[i], out isSlope, out isRiver);
                List<int> ints = new List<int>();//暂时的中间变量
                if (isSlope)
                {
                    //顶点坐标vertices
                    verticesList.AddRange(_meshGenerator.GetRectVertices(ref hexCellData, hexDirections[i], _mapDataService));
                    //UV
                    uvList.AddRange(_meshGenerator.GetRectUV(ref hexCellData, hexDirections[i], _mapDataService));
                    //绘制顺序
                    //ints.Clear();
                    if (isRiver) { OtherMeshDrawOrderElementAddRule(ref hexCellData, _meshGenerator.GetRectSlopeRiverDrawOrder(ref hexCellData, hexDirections[i], _mapDataService), ref ints, IndexOffset); }
                    else { OtherMeshDrawOrderElementAddRule(ref hexCellData, _meshGenerator.GetRectDrawOrder(ref hexCellData, hexDirections[i], _mapDataService), ref ints, IndexOffset); }
                }
                else
                {
                    //顶点坐标vertices
                    verticesList.AddRange(_meshGenerator.GetRectStepVertices(ref hexCellData, hexDirections[i], _mapDataService));
                    //UV
                    uvList.AddRange(_meshGenerator.GetRectStepUV(ref hexCellData, hexDirections[i], _mapDataService));
                    //绘制顺序
                    if (isRiver) { OtherMeshDrawOrderElementAddRule(ref hexCellData, _meshGenerator.GetRectStepRiverDrawOrder(ref hexCellData, hexDirections[i], _mapDataService), ref ints, IndexOffset); }
                    else { OtherMeshDrawOrderElementAddRule(ref hexCellData, _meshGenerator.GetRectStepDrawOrder(ref hexCellData, hexDirections[i], _mapDataService), ref ints, IndexOffset); }
                }

                materialAs.Add(HexController.SetHexMaterial(hexCellData, _config.mapMaterial));
                materialBs.Add(HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, hexDirections[i]), _config.mapMaterial));

                transitionDrawOrderList.AddRange(ints);
                transitionRectDrawOrderList.Add(ints);
            }

        }
        //三角区域
        for (int j = 0; j < hexVertices.Length; j++)
        {
            //根据六边形坐标获取hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            //绘制顺序偏移
            int IndexOffset = verticesList.Count;

            Enums.HexDirection[][] h = new Enums.HexDirection[2][]
            {
                new[] { Enums.HexDirection.NE, Enums.HexDirection.E },
                new[] { Enums.HexDirection.E, Enums.HexDirection.SE }
            };

            //每个地块管两个三角过渡区域（NE_E、E_SE）           
            for (int i = 0; i < 2; i++)
            {
                if (_mapDataService.GetNeighbor(hexCellData, h[i][0]) == null || _mapDataService.GetNeighbor(hexCellData, h[i][1]) == null) continue;
                IndexOffset = verticesList.Count;
                List<int> ints = new List<int>();//暂时的中间变量
                //绘制方法
                Enums.TriType triType = MainMeshTriFunction(hexCellData, h[i][0], h[i][1]);
                //顶点坐标vertices
                verticesList.AddRange(GetTriVerticesFunction(triType, ref hexCellData, h[i][0], h[i][1]));
                //UV
                uvList.AddRange(GetTriUVFunction(triType, ref hexCellData, h[i][0], h[i][1]));
                //绘制顺序1           
                //ints.Clear();
                OtherMeshDrawOrderElementAddRule(ref hexCellData, GetTriDrawOrderFunction(triType, ref hexCellData, h[i][0], h[i][1]), ref ints, IndexOffset);

                materialAsTri.Add(HexController.SetHexMaterial(hexCellData, _config.mapMaterial));
                materialBsTri.Add(HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, h[i][0]), _config.mapMaterial));
                materialCsTri.Add(HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, h[i][1]), _config.mapMaterial));

                transitionDrawOrderList.AddRange(ints);
                transitionTriDrawOrderList.Add(ints);
            }
        }

        //调用CreatMesh()
        //三角形绘制顺序
        int[][] arrArawOrder = new int[3 + transitionRectDrawOrderList.Count + transitionRectDrawOrderList.Count][];
        //海底
        arrArawOrder[0] = subList[2].ToArray();
        //平地
        arrArawOrder[1] = subList[1].ToArray();
        //高地
        arrArawOrder[2] = subList[0].ToArray();
        //矩形过渡区域

        int transitionOffset = 3;
        for (int i = 0; i < transitionRectDrawOrderList.Count; i++)
        {
            arrArawOrder[transitionOffset + i] = transitionRectDrawOrderList[i].ToArray();
        }
        //三角过渡区域
        transitionOffset = transitionRectDrawOrderList.Count + 3;
        for (int i = 0; i < transitionTriDrawOrderList.Count; i++)
        {
            arrArawOrder[transitionOffset + i] = transitionTriDrawOrderList[i].ToArray();
        }

        // 创建主地图 Mesh，并保存运行时数据到 mapGenerator
        Mesh mainMesh = MapController.CreatMesh(
                   verticesList.ToArray(),
                   uvList.ToArray(),
                   arrArawOrder,
                   _mapDataService.MapGameObject,
                   _config.mapMaterial,
                   materialAs.ToArray(),
                   materialBs.ToArray(),

                   materialBsTri.ToArray(),
                   materialAsTri.ToArray(),
                   materialCsTri.ToArray(),
                   _config.blendMask,
                   _config.blendContrast,
                   _config.blendSmooth,
                   _config.globalSmoothness
                  );

        // 将生成的顶点列表与 Mesh 传回 MapGenerator（供后续使用）
        if (mapGenerator != null)
        {
            mapGenerator.verticesList = verticesList;
            mapGenerator.mesh = mainMesh;
        }
    }

    //河流Mesh创建
    private void RiverMeshCreat(Vector3[] hexVertices)
    {
        //声明变量
        GameObject RiverWater = new GameObject("RiverWater");
        //顶点数组
        List<Vector3> verticesRiverWater = new List<Vector3>();
        //UV 
        List<Vector2> uvRiverWater = new List<Vector2>();
        //三角形绘制顺序
        List<int> drawOrderRiverWater = new List<int>();

        //变量赋值
        //实心区域        
        for (int j = 0; j < hexVertices.Length; j++)
        {
            List<int> ints = new List<int>();//暂时的中间变量
            //根据六边形坐标获取hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            //绘制顺序偏移
            int IndexOffset = verticesRiverWater.Count;

            if (RiverMeshSolidAreaDrawOrderFunction(hexCellData) == null) continue;

            //顶点坐标
            verticesRiverWater.AddRange(_meshGenerator.GetRiverVertices(ref hexCellData));
            //顶点绘制顺序
            List<int> l = new List<int>();
            l = RiverMeshSolidAreaDrawOrderFunction(hexCellData);
            ints.Clear();
            OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
            drawOrderRiverWater.AddRange(ints);
            //UV
            uvRiverWater.AddRange(_meshGenerator.GetRiverUV(ref hexCellData, l));
        }
        //下游过渡区域
        for (int j = 0; j < hexVertices.Length; j++)
        {
            List<int> ints = new List<int>();//暂时的中间变量
            //根据六边形坐标获取hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            //绘制顺序偏移
            int IndexOffset = verticesRiverWater.Count;

            //该地块下游过渡区域
            //顶点坐标 - 河水不分坡或阶梯
            verticesRiverWater.AddRange(_meshGenerator.GetOutgoingRiverVertices(ref hexCellData, _mapDataService));

            //顶点绘制顺序
            List<int> l = new List<int>();
            l.AddRange(RiverMeshDownstreamDrawOrderFunction(ref hexCellData));
            OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
            drawOrderRiverWater.AddRange(ints);
            //UV
            uvRiverWater.AddRange(_meshGenerator.GetOutgoingRiverSlopUV(ref hexCellData));
        }

        //调用CreatMesh()
        if (drawOrderRiverWater.Count % 3 == 0 && drawOrderRiverWater.Count != 0)
        {

            MapController.CreatMesh(verticesRiverWater.ToArray(), uvRiverWater.ToArray(), drawOrderRiverWater.ToArray(), RiverWater, _config.riverMaterial);
        }
    }

    //湖或海Mesh创建
    private void LakeOrSeaMeshCreat(Vector3[] hexVertices)
    {
        GameObject LakeOrSea = new GameObject("LakeOrSea");
        //顶点数组
        List<Vector3> verticesLakeOrSea = new List<Vector3>();
        //UV 
        List<Vector2> uvLakeOrSea = new List<Vector2>();
        //湖或海绘制顺序
        List<int> drawOrderLakeOrSea = new List<int>();
        //海岸绘制顺序
        List<int> drawOrderCoast = new List<int>();

        //先确定哪些是“湖或海”地块
        for (int j = 0; j < hexVertices.Length; j++)
        {
            //根据六边形坐标获取hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            //若不是“湖或海”即跳过
            if (!isLakeOrSea(hexCellData)) { continue; }
            hexCellData.HexType = Enums.HexType.LakeOrSea;
            hexCellData.isCoast = true;
        }

        //实心区域
        for (int j = 0; j < hexVertices.Length; j++)
        {
            List<int> ints = new List<int>();//暂时的中间变量
            //根据六边形坐标获取hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            int IndexOffset = verticesLakeOrSea.Count;
            //若不是“湖或海”即跳过
            if (hexCellData.HexType != Enums.HexType.LakeOrSea) { continue; }
            //寻找海岸地格
            bool isCoast = (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE) != null) && (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NE).HexType != Enums.HexType.LakeOrSea) ||
                           (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E) != null) && (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.E).HexType != Enums.HexType.LakeOrSea) ||
                           (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE) != null) && (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SE).HexType != Enums.HexType.LakeOrSea) ||
                           (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SW) != null) && (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.SW).HexType != Enums.HexType.LakeOrSea) ||
                           (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.W) != null) && (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.W).HexType != Enums.HexType.LakeOrSea) ||
                           (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NW) != null) && (_mapDataService.GetNeighbor(hexCellData, Enums.HexDirection.NW).HexType != Enums.HexType.LakeOrSea);

            //顶点坐标
            verticesLakeOrSea.AddRange(_meshGenerator.GetlakeOrSeaVertices(ref hexCellData));
            //UV
            uvLakeOrSea.AddRange(_meshGenerator.GetlakeOrSeaUV(ref hexCellData));

            //顶点绘制顺序
            List<int> l = new List<int>();
            l.AddRange(LakeOrSeaMeshSolidAreaDrawOrderFunction(ref hexCellData));
            OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
            drawOrderLakeOrSea.AddRange(ints);
        }
        //矩形过渡区域
        for (int j = 0; j < hexVertices.Length; j++)
        {
            List<int> ints = new List<int>();//暂时的中间变量
            //根据六边形坐标获取hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            int IndexOffset = verticesLakeOrSea.Count;
            //若不是“湖或海”即跳过
            if (hexCellData.HexType != Enums.HexType.LakeOrSea) { continue; }

            Enums.HexDirection[] hexDirections = new Enums.HexDirection[3] { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE };

            for (int i = 0; i < hexDirections.Length; i++)
            {
                if (_mapDataService.GetNeighbor(hexCellData, hexDirections[i]) != null && _mapDataService.GetNeighbor(hexCellData, hexDirections[i]).lakeOrSeaVertices.Count != 0)
                {
                    //顶点坐标
                    IndexOffset = verticesLakeOrSea.Count;
                    verticesLakeOrSea.AddRange(_meshGenerator.GetlakeOrSeaRectVertices(ref hexCellData, hexDirections[i], _mapDataService));
                    //UV
                    uvLakeOrSea.AddRange(_meshGenerator.GetlakeOrSeaRectUV(ref hexCellData, hexDirections[i], _mapDataService));
                    //绘制顺序          
                    List<int> l = new List<int>();
                    l.AddRange(LakeOrSeaMeshRectDrawOrderFunction(hexCellData, hexDirections[i]));
                    ints.Clear();
                    OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
                    drawOrderLakeOrSea.AddRange(ints);
                }
            }
        }
        //三角过渡区域 
        for (int j = 0; j < hexVertices.Length; j++)
        {
            List<int> ints = new List<int>();//暂时的中间变量
            //根据六边形坐标获取hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            int IndexOffset = verticesLakeOrSea.Count;
            //若不是“湖或海”即跳过
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
                    //顶点坐标
                    IndexOffset = verticesLakeOrSea.Count;
                    verticesLakeOrSea.AddRange(_meshGenerator.GetlakeOrSeaTriVertices(ref hexCellData, h[i][0], h[i][1], _mapDataService));
                    //UV
                    uvLakeOrSea.AddRange(_meshGenerator.GetlakeOrSeaTriUV(ref hexCellData, h[i][0], h[i][1], _mapDataService));

                    //三角绘制顺序
                    List<int> l = new List<int>();
                    l.AddRange(LakeOrSeaMeshTriDrawOrderFunction(hexCellData, h[i][0], h[i][1]));
                    ints.Clear();
                    OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
                    drawOrderLakeOrSea.AddRange(ints);
                }
            }

        }
        //海岸网格 - 矩形
        for (int j = 0; j < hexVertices.Length; j++)
        {
            List<int> ints = new List<int>();//暂时的中间变量
            //根据六边形坐标获取hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            int IndexOffset = verticesLakeOrSea.Count;
            //若不是“湖或海”即跳过
            if (hexCellData.HexType != Enums.HexType.LakeOrSea) { continue; }

            //海岸的方向
            List<Enums.HexDirection> coastDirections = new List<Enums.HexDirection>();
            Enums.HexDirection[] hexDirections = { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE, Enums.HexDirection.SW, Enums.HexDirection.W, Enums.HexDirection.NW };
            foreach (Enums.HexDirection h in hexDirections)
            {
                if (_mapDataService.GetNeighbor(hexCellData, h) != null && _mapDataService.GetNeighbor(hexCellData, h).HexType != Enums.HexType.LakeOrSea)
                {
                    coastDirections.Add(h);
                }
            }

            ///矩形
            //顶点坐标
            List<Vector3> v = new List<Vector3>();
            foreach (Enums.HexDirection h in coastDirections)
            {
                v.AddRange(_meshGenerator.GetOneDirectionCoastRectVertices(ref hexCellData, h, _mapDataService));
            }
            verticesLakeOrSea.AddRange(v);
            //UV
            uvLakeOrSea.AddRange(_meshGenerator.GetCoastRectUV(ref hexCellData, v.ToArray()));
            //三角绘制顺序    
            List<int> l = new List<int>();
            l.AddRange(CoastMeshRectDrawOrderFunction(hexCellData, v.ToArray()));
            ints.Clear();
            OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
            drawOrderCoast.AddRange(ints);
        }
        //海岸网格 - 三角
        for (int j = 0; j < hexVertices.Length; j++)
        {
            List<int> ints = new List<int>();//暂时的中间变量
            //根据六边形坐标获取hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            int IndexOffset = verticesLakeOrSea.Count;
            //若不是“湖或海”即跳过
            if (hexCellData.HexType != Enums.HexType.LakeOrSea) { continue; }

            //海岸的方向
            List<Enums.HexDirection> coastDirections = new List<Enums.HexDirection>();
            Enums.HexDirection[] hexDirections = { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE, Enums.HexDirection.SW, Enums.HexDirection.W, Enums.HexDirection.NW };
            foreach (Enums.HexDirection h in hexDirections)
            {
                if (_mapDataService.GetNeighbor(hexCellData, h) != null && _mapDataService.GetNeighbor(hexCellData, h).HexType != Enums.HexType.LakeOrSea)
                {
                    coastDirections.Add(h);
                }
            }

            //三角
            List<Vector3> v = new List<Vector3>();
            foreach (Enums.HexDirection h in coastDirections)
            {
                v.AddRange(_meshGenerator.GetOneDirectionCoastTriVertices(ref hexCellData, h, _mapDataService));
            }
            verticesLakeOrSea.AddRange(v);
            //UV
            uvLakeOrSea.AddRange(_meshGenerator.GetCoastTriUV(ref hexCellData, v.ToArray()));
            //三角绘制顺序
            List<int> l = new List<int>();
            l.AddRange(CoastMeshTriDrawOrderFunction(hexCellData, v.ToArray()));
            ints.Clear();
            OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
            drawOrderCoast.AddRange(ints);
        }


        int[][] arrArawOrderLakeOrSea = new int[2][];
        //海岸网格
        arrArawOrderLakeOrSea[0] = drawOrderCoast.ToArray();
        //湖或海网格
        arrArawOrderLakeOrSea[1] = drawOrderLakeOrSea.ToArray();
        MapController.CreatMesh(verticesLakeOrSea.ToArray(), uvLakeOrSea.ToArray(), arrArawOrderLakeOrSea, LakeOrSea, _config.lakeOrSeaMaterial);

    }

    //网格Mesh创建
    private void GridMeshCreat(Vector3[] hexVertices)
    {
        //网格线
        GameObject GridLine = new GameObject("GridLine");
        //顶点数组
        List<Vector3> verticesGridLine = new List<Vector3>();
        //UV 
        List<Vector2> uvGridLine = new List<Vector2>();
        //绘制顺序
        List<List<int>> drawOrderGridLine = new List<List<int>>();

        for (int j = 0; j < hexVertices.Length; j++)
        {
            List<int> ints = new List<int>();//暂时的中间变量
            //根据六边形坐标获取hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            int IndexOffset = verticesGridLine.Count;
            //若是“湖或海”即跳过
            if (isLakeOrSea(hexCellData)) { continue; }

            //顶点坐标            
            verticesGridLine.AddRange(_meshGenerator.GetGridVertices(ref hexCellData));
            //UV
            uvGridLine.AddRange(_meshGenerator.GetGridUV(ref hexCellData));
            //顶点绘制顺序
            List<int> l = new List<int>();
            l.AddRange(_meshGenerator.GetGridDrawOrder(ref hexCellData));
            ints.Clear();
            OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
            drawOrderGridLine.Add(ints);
        }

        Shader gridLineShader = Shader.Find("Custom/GridLine") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Hidden/InternalErrorShader");
        //为每个地块网格线单独创建一个GameObject
        for (int j = 0, i = 0; j < hexVertices.Length; j++)
        {
            //根据六边形坐标获取hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            //若是“湖或海”即跳过
            if (isLakeOrSea(hexCellData)) { continue; }

            GameObject go = new GameObject($"SubGridLine_{j}");
            MapController.CreatMesh(verticesGridLine.ToArray(), uvGridLine.ToArray(), drawOrderGridLine[i++].ToArray(), go, new Material(gridLineShader));
            hexCellData.GridMesh = go;
            go.SetActive(false);
            go.transform.parent = GridLine.transform;

        }
        mapGenerator.gridGameObject = GridLine;
    }

    //判断某个地块是否为湖或海
    private bool isLakeOrSea(HexCellData hexCellData)
    {
        //若高度为0，则为湖或海
        return !(hexCellData.Height > 0);
    }

    //主地图Mesh实心区域绘制顺序选择逻辑
    private int MainMeshSolidAreaDrawOrderFunction(HexCellData hexCellData, out List<Enums.HexDirection> direction)
    {
        int drawOrder;
        direction = new List<Enums.HexDirection>();

        if (hexCellData.HexType == Enums.HexType.RiverSource)
        {
            //滥觞 + 方向
            drawOrder = 2;
            direction.Add(hexCellData.RiverOutgoingDirection);
        }
        else if (hexCellData.HexType == Enums.HexType.RiverMidstream)
        {
            //中游 + 进入 + 出去方向
            drawOrder = 3;
            direction.Add(hexCellData.RiverIncomingDirection);
            direction.Add(hexCellData.RiverOutgoingDirection);
        }
        else if (hexCellData.HexType == Enums.HexType.RiverEnd)
        {
            //终点 + 方向
            drawOrder = 2;
            direction.Add(hexCellData.RiverIncomingDirection);
        }
        else
        {
            //无河流地块
            drawOrder = 1;
        }

        return drawOrder;
    }

    //主地图Mesh矩形过渡区域绘制顺序选择逻辑
    private void MainMeshRectFunction(HexCellData hexCellData, Enums.HexDirection direction, out bool isSlope, out bool isRiver)
    {
        isRiver = false;
        isSlope = true;
        //判断过渡区域的绘制方法
        Enums.RectType[] rectTypes = new Enums.RectType[] { };
        Enums.TriType[] triTypes = new Enums.TriType[] { };
        TerrainGenerator.IsType(hexCellData, out rectTypes, out triTypes, _mapDataService);

        //有无对应邻居
        if (_mapDataService.GetNeighbor(hexCellData, direction) == null) { return; }

        //有无河
        if ((hexCellData.RiverIncomingDirection == direction || hexCellData.RiverOutgoingDirection == direction) &&
            (hexCellData.hasRiver && _mapDataService.GetNeighbor(hexCellData, direction).hasRiver))
        { isRiver = true; }

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

    //主地图Mesh三角过渡区域逻辑
    private Enums.TriType MainMeshTriFunction(HexCellData hexCellData, Enums.HexDirection directionA, Enums.HexDirection directionB)
    {
        //判断过渡区域的绘制方法
        Enums.RectType[] rectTypes = new Enums.RectType[] { };
        Enums.TriType[] triTypes = new Enums.TriType[] { };
        TerrainGenerator.IsType(hexCellData, out rectTypes, out triTypes, _mapDataService);
        int index = 0;

        if (directionA == Enums.HexDirection.NE && directionB == Enums.HexDirection.E) { index = 0; }
        else if (directionA == Enums.HexDirection.E && directionB == Enums.HexDirection.SE) { index = 1; }

        //有无对应邻居
        if (_mapDataService.GetNeighbor(hexCellData, directionA) == null || _mapDataService.GetNeighbor(hexCellData, directionB) == null) { return Enums.TriType.zero; }

        return triTypes[index];
    }
    //顶点
    private List<Vector3> GetTriVerticesFunction(Enums.TriType triType, ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1)
    {
        List<Vector3> triVertices = new List<Vector3>();
        switch (triType)
        {
            case Enums.TriType.one:
                return _meshGenerator.GetTriVertices(ref hexCellData, direction0, direction1, _mapDataService);
            case Enums.TriType.two:
                Debug.Log("暂无此方法");
                return triVertices;
            case Enums.TriType.three:
                return _meshGenerator.GetTriStep3Vertices(ref hexCellData, direction0, direction1, _mapDataService);
            case Enums.TriType.four:
                return _meshGenerator.GetTriStep4Vertices(ref hexCellData, direction0, direction1, _mapDataService);
            default:
                Debug.Log("输入的TriType出错");
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
                Debug.Log("暂无此方法");
                return triUV;
            case Enums.TriType.three:
                return _meshGenerator.GetTriStep3UV(ref hexCellData, direction0, direction1);
            case Enums.TriType.four:
                return _meshGenerator.GetTriStep4UV(ref hexCellData, direction0, direction1);
            default:
                Debug.Log("输入的TriType出错");
                return triUV;
        }
    }
    //绘制顺序
    private List<int> GetTriDrawOrderFunction(Enums.TriType triType, ref HexCellData hexCellData, Enums.HexDirection direction0, Enums.HexDirection direction1)
    {
        List<int> triUV = new List<int>();
        switch (triType)
        {
            case Enums.TriType.one:
                return _meshGenerator.GetTriDrawOrder(ref hexCellData, direction0, direction1, _mapDataService);
            case Enums.TriType.two:
                Debug.Log("暂无此方法");
                return triUV;
            case Enums.TriType.three:
                return _meshGenerator.GetTriStep3DrawOrder(ref hexCellData, direction0, direction1);
            case Enums.TriType.four:
                return _meshGenerator.GetTriStep4DrawOrder(ref hexCellData, direction0, direction1, _mapDataService);
            default:
                Debug.Log("输入的TriType出错");
                return triUV;
        }
    }

    //河水Mesh实心区域绘制顺序选择逻辑
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
                //Debug.Log("出错，理应不可到达此处");
                return null;
        }
    }

    //河水Mesh下游过渡区域绘制顺序选择逻辑
    private int[] RiverMeshDownstreamDrawOrderFunction(ref HexCellData hexCellData)
    {
        return _meshGenerator.GetOutgoingRiverSlopDrawOrder(ref hexCellData);
    }

    //湖或海Mesh实心区域绘制顺序选择逻辑
    private int[] LakeOrSeaMeshSolidAreaDrawOrderFunction(ref HexCellData hexCellData)
    {
        return _meshGenerator.GetlakeOrSeaDrawOrder(ref hexCellData);
    }

    //湖或海Mesh矩形过渡区域绘制顺序选择逻辑
    private List<int> LakeOrSeaMeshRectDrawOrderFunction(HexCellData hexCellData, Enums.HexDirection direction)
    {
        if (_mapDataService.GetNeighbor(hexCellData, direction) != null && _mapDataService.GetNeighbor(hexCellData, direction).lakeOrSeaVertices.Count != 0)
        {
            return _meshGenerator.GetlakeOrSeaRectDrawOrder(ref hexCellData, direction, _mapDataService);
        }
        else
        {
            Debug.Log("出错，正常情况不应来到这里");
            return null;
        }
    }

    //湖或海Mesh三角过渡区域绘制顺序选择逻辑
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
            Debug.Log("出错了，正常情况不可能到这");
            return null;
        }
    }

    //海岸Mesh矩形过渡区域绘制顺序选择逻辑
    private List<int> CoastMeshRectDrawOrderFunction(HexCellData hexCellData, Vector3[] v)
    {
        return _meshGenerator.GetCoastRectDrawOrder(ref hexCellData, v);
    }

    //海岸Mesh三角过渡区域绘制顺序选择逻辑
    private List<int> CoastMeshTriDrawOrderFunction(HexCellData hexCellData, Vector3[] v)
    {
        return _meshGenerator.GetCoastTriDrawOrder(ref hexCellData, v);
    }


    //绘制顺序的添加规则 - 1.主地图网格基础地块规则、2.正常添加规则
    private void MainMeshDrawOrderElementAddRule(ref HexCellData hexCellData, List<int> drawOrder, ref List<List<int>> subList, int IndexOffset)
    {
        foreach (int i in drawOrder)
        {
            //不能直接加入，需要预处理
            switch (hexCellData.Height)
            {
                case 0:
                    subList[0].Add(i + IndexOffset);
                    break;
                case 1:
                    subList[1].Add(i + IndexOffset);
                    break;
                case 2:
                    subList[2].Add(i + IndexOffset);
                    break;
            }
        }
    }

    //绘制顺序的添加规则 - 2.正常添加规则
    private void OtherMeshDrawOrderElementAddRule(ref HexCellData hexCellData, List<int> drawOrder, ref List<int> ints, int IndexOffset)
    {
        foreach (int i in drawOrder)
        {
            //不能直接加入，需要预处理
            ints.Add(i + IndexOffset);
        }
    }

    private void InstantiateLandForms(Vector3[] hexVertices)
    {
        GameObject landForm = new GameObject("LandForm");

        for (int j = 0; j < hexVertices.Length; j++)
        {
            //根据六边形坐标获取hexCell
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
            //根据六边形坐标获取hexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            if ((int)hexCellData.resourceType == 4) { continue; }
            hexCellData.resourceModel = Instantiate(environmentModelsProvider.GetResourcePrefab((int)hexCellData.resourceType));
            hexCellData.resourceModel.transform.position = hexCellData.RealCenterWorldCoordinate + new Vector3(0, 0, 0);
            hexCellData.resourceModel.AddComponent<ModelController>();
            hexCellData.resourceModel.transform.SetParent(Resource.transform);
        }

    }
}