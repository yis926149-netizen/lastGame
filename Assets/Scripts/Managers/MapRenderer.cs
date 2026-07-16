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

    // ��Ⱦ��ͼ�Ӿ�����
    public void MapRender()
    {
        // 1. ��ȡ��������������
        Vector3[] hexVertices = _mapDataService.GetHexVertices();

        // 2. ���ɸ���Mesh
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
        //��������
        List<Vector3> verticesList = new List<Vector3>();
        //UV 
        List<Vector2> uvList = new List<Vector2>();
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
            //���������������ȡhexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            //����˳��ƫ��
            int IndexOffset = verticesList.Count;

            //��������vertices
            verticesList.AddRange(_meshGenerator.GetSolidAreaVertices(ref hexCellData));

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
        //��������
        for (int j = 0; j < hexVertices.Length; j++)
        {
            //���������������ȡhexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            //����˳��ƫ��
            int IndexOffset = verticesList.Count;
            Enums.HexDirection[] hexDirections = new Enums.HexDirection[3] { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE };
            for (int i = 0; i < hexDirections.Length; i++)
            {
                IndexOffset = verticesList.Count;
                if (_mapDataService.GetNeighbor(hexCellData, hexDirections[i]) == null) continue;
                bool isSlope = true, isRiver = false;
                MainMeshRectFunction(hexCellData, hexDirections[i], out isSlope, out isRiver);
                List<int> ints = new List<int>();//��ʱ���м����
                if (isSlope)
                {
                    //��������vertices
                    verticesList.AddRange(_meshGenerator.GetRectVertices(ref hexCellData, hexDirections[i], _mapDataService));
                    //UV
                    uvList.AddRange(_meshGenerator.GetRectUV(ref hexCellData, hexDirections[i], _mapDataService));
                    //����˳��
                    //ints.Clear();
                    if (isRiver) { OtherMeshDrawOrderElementAddRule(ref hexCellData, _meshGenerator.GetRectSlopeRiverDrawOrder(ref hexCellData, hexDirections[i], _mapDataService), ref ints, IndexOffset); }
                    else { OtherMeshDrawOrderElementAddRule(ref hexCellData, _meshGenerator.GetRectDrawOrder(ref hexCellData, hexDirections[i], _mapDataService), ref ints, IndexOffset); }
                }
                else
                {
                    //��������vertices
                    verticesList.AddRange(_meshGenerator.GetRectStepVertices(ref hexCellData, hexDirections[i], _mapDataService));
                    //UV
                    uvList.AddRange(_meshGenerator.GetRectStepUV(ref hexCellData, hexDirections[i], _mapDataService));
                    //����˳��
                    if (isRiver) { OtherMeshDrawOrderElementAddRule(ref hexCellData, _meshGenerator.GetRectStepRiverDrawOrder(ref hexCellData, hexDirections[i], _mapDataService), ref ints, IndexOffset); }
                    else { OtherMeshDrawOrderElementAddRule(ref hexCellData, _meshGenerator.GetRectStepDrawOrder(ref hexCellData, hexDirections[i], _mapDataService), ref ints, IndexOffset); }
                }

                materialAs.Add(HexController.SetHexMaterial(hexCellData, _config.mapMaterial));
                materialBs.Add(HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, hexDirections[i]), _config.mapMaterial));

                transitionDrawOrderList.AddRange(ints);
                transitionRectDrawOrderList.Add(ints);
            }

        }
        //��������
        for (int j = 0; j < hexVertices.Length; j++)
        {
            //���������������ȡhexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            //����˳��ƫ��
            int IndexOffset = verticesList.Count;

            Enums.HexDirection[][] h = new Enums.HexDirection[2][]
            {
                new[] { Enums.HexDirection.NE, Enums.HexDirection.E },
                new[] { Enums.HexDirection.E, Enums.HexDirection.SE }
            };

            //ÿ���ؿ���������ǹ�������NE_E��E_SE��           
            for (int i = 0; i < 2; i++)
            {
                if (_mapDataService.GetNeighbor(hexCellData, h[i][0]) == null || _mapDataService.GetNeighbor(hexCellData, h[i][1]) == null) continue;
                IndexOffset = verticesList.Count;
                List<int> ints = new List<int>();//��ʱ���м����
                //���Ʒ���
                Enums.TriType triType = MainMeshTriFunction(hexCellData, h[i][0], h[i][1]);
                //��������vertices
                verticesList.AddRange(GetTriVerticesFunction(triType, ref hexCellData, h[i][0], h[i][1]));
                //UV
                uvList.AddRange(GetTriUVFunction(triType, ref hexCellData, h[i][0], h[i][1]));
                //����˳��1           
                //ints.Clear();
                OtherMeshDrawOrderElementAddRule(ref hexCellData, GetTriDrawOrderFunction(triType, ref hexCellData, h[i][0], h[i][1]), ref ints, IndexOffset);

                materialAsTri.Add(HexController.SetHexMaterial(hexCellData, _config.mapMaterial));
                materialBsTri.Add(HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, h[i][0]), _config.mapMaterial));
                materialCsTri.Add(HexController.SetHexMaterial(_mapDataService.GetNeighbor(hexCellData, h[i][1]), _config.mapMaterial));

                transitionDrawOrderList.AddRange(ints);
                transitionTriDrawOrderList.Add(ints);
            }
        }

        //����CreatMesh()
        //�����λ���˳��
        int[][] arrArawOrder = new int[3 + transitionRectDrawOrderList.Count + transitionRectDrawOrderList.Count][];
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

        // ��������ͼ Mesh������������ʱ���ݵ� mapGenerator
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

        // �����ɵĶ����б��� Mesh ���� MapGenerator��������ʹ�ã�
        if (mapGenerator != null)
        {
            mapGenerator.verticesList = verticesList;
            mapGenerator.mesh = mainMesh;
        }
    }

    //����Mesh����
    private void RiverMeshCreat(Vector3[] hexVertices)
    {
        //��������
        GameObject RiverWater = new GameObject("RiverWater");
        //��������
        List<Vector3> verticesRiverWater = new List<Vector3>();
        //UV 
        List<Vector2> uvRiverWater = new List<Vector2>();
        //�����λ���˳��
        List<int> drawOrderRiverWater = new List<int>();

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
            verticesRiverWater.AddRange(_meshGenerator.GetRiverVertices(ref hexCellData));
            //�������˳��
            List<int> l = new List<int>();
            l = RiverMeshSolidAreaDrawOrderFunction(hexCellData);
            ints.Clear();
            OtherMeshDrawOrderElementAddRule(ref hexCellData, l, ref ints, IndexOffset);
            drawOrderRiverWater.AddRange(ints);
            //UV
            uvRiverWater.AddRange(_meshGenerator.GetRiverUV(ref hexCellData, l));
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
        }

        //����CreatMesh()
        if (drawOrderRiverWater.Count % 3 == 0 && drawOrderRiverWater.Count != 0)
        {

            MapController.CreatMesh(verticesRiverWater.ToArray(), uvRiverWater.ToArray(), drawOrderRiverWater.ToArray(), RiverWater, _config.riverMaterial);
        }
    }

    //����Mesh����
    private void LakeOrSeaMeshCreat(Vector3[] hexVertices)
    {
        GameObject LakeOrSea = new GameObject("LakeOrSea");
        //��������
        List<Vector3> verticesLakeOrSea = new List<Vector3>();
        //UV 
        List<Vector2> uvLakeOrSea = new List<Vector2>();
        //���򺣻���˳��
        List<int> drawOrderLakeOrSea = new List<int>();
        //��������˳��
        List<int> drawOrderCoast = new List<int>();

        //��ȷ����Щ�ǡ����򺣡��ؿ�
        for (int j = 0; j < hexVertices.Length; j++)
        {
            //���������������ȡhexCell
            HexCellData hexCellData = _mapDataService.GetCell(hexVertices[j]);
            //�����ǡ����򺣡�������
            if (!isLakeOrSea(hexCellData)) { continue; }
            hexCellData.HexType = Enums.HexType.LakeOrSea;
            hexCellData.isCoast = true;
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
            verticesLakeOrSea.AddRange(_meshGenerator.GetlakeOrSeaVertices(ref hexCellData));
            //UV
            uvLakeOrSea.AddRange(_meshGenerator.GetlakeOrSeaUV(ref hexCellData));

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
        MapController.CreatMesh(verticesLakeOrSea.ToArray(), uvLakeOrSea.ToArray(), arrArawOrderLakeOrSea, LakeOrSea, _config.lakeOrSeaMaterial);

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
        //���߶�Ϊ0����Ϊ����
        return !(hexCellData.Height > 0);
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
    private void MainMeshRectFunction(HexCellData hexCellData, Enums.HexDirection direction, out bool isSlope, out bool isRiver)
    {
        isRiver = false;
        isSlope = true;
        //�жϹ�������Ļ��Ʒ���
        Enums.RectType[] rectTypes = new Enums.RectType[] { };
        Enums.TriType[] triTypes = new Enums.TriType[] { };
        TerrainGenerator.IsType(hexCellData, out rectTypes, out triTypes, _mapDataService);

        //���޶�Ӧ�ھ�
        if (_mapDataService.GetNeighbor(hexCellData, direction) == null) { return; }

        //���޺�
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
        foreach (int i in drawOrder)
        {
            //����ֱ�Ӽ��룬��ҪԤ����
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
}