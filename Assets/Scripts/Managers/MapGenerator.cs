using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class MapGenerator : MonoBehaviour
{
    //ע��
    [Inject] private IUIConfigProvider uiConfigProvider;
    [Inject] private IMapDataService _mapDataService;
    [Inject] private MapGenerationConfigSO _config; 
    // 【地图资源配置化】资源数据库（生成权重表）
    [Inject] private MapResourceDatabaseSO _resourceDatabase;
    // 【地图地貌配置化】地貌数据库（生成权重表）
    [Inject] private MapLandFormDatabaseSO _landFormDatabase;

    //�ο�
    public GameObject NextCardPlaceholder;

    //��������������λ��
    public Vector3 SpawnHexCenterPoint = new Vector3();

    //ȫ���ؿ�����ĵ���������
    [HideInInspector]
    public List<Vector3> centerWorldCoordinates = new List<Vector3>();
    //�ؿ����ĵ����������_�ؿ������������_�ֵ�
    [HideInInspector]
    public Dictionary<Vector3, Vector3> CenterWorldC_HexC = new Dictionary<Vector3, Vector3>();
    //�ؿ������������_�ؿ��HexCell_�ֵ�
    [HideInInspector]
    public Dictionary<Vector3, HexCellData> HexC_HexCellData = new Dictionary<Vector3, HexCellData>();
    //�ؿ����ɵ�˳��_�ؿ��HexCell_�ֵ�
    [HideInInspector]
    public Dictionary<int, HexCellData> GenerateOrder_HexCellData = new Dictionary<int, HexCellData>();

    //��ͼ����ʱ����
    public List<Vector3> verticesList;
    public Mesh mesh;
    public GameObject gridGameObject;

    // ���ɵ�ͼ���ݣ�����䵽 IMapDataService ��
    public Vector3[] Generate()
    {
        // ���ôο�ռλ����
        uiConfigProvider.SetNextCardPlaceholder(NextCardPlaceholder);

        // ��ʼ����̬����ͼ
        HexMetrics.noiseSource = _config.noiseSource;
        // 注入有界竖直扰动强度 = elevationStep * verticalPerturbRatio（保证水下不捅穿、陆地不沉水）
        HexMetrics.yPerturbStrength = _config.elevationStep * _config.verticalPerturbRatio;

        centerWorldCoordinates.Clear();
        CenterWorldC_HexC.Clear();
        HexC_HexCellData.Clear();
        GenerateOrder_HexCellData.Clear();

        // ��ͼ�������ɣ���������ֵ����������
        MapDataGeneration(out Vector3[] hexVertices);

        // ��ʼ����ͼ���񣬴��������ɺõ�����
        InitializeMapDataService(hexVertices);

        return hexVertices;
    }

    private void InitializeMapDataService(Vector3[] hexVertices)
    {
        _mapDataService.Initialize(
            hexToCell: HexC_HexCellData,
            orderToCell: GenerateOrder_HexCellData,
            centerWorldCoordinates: centerWorldCoordinates,
            centerWorldToHex: CenterWorldC_HexC,
            mapGameObject: transform.gameObject,

            //��ͼ����
            hexVertices: hexVertices,

            //��ͼ����ʱ����
            verticesList: verticesList,
            mesh: mesh,
            gridGameObject: gridGameObject
        );
    }

    private void MapDataGeneration(out Vector3[] hexVertices)
    {
        int seed = _config.useFixedSeed
            ? _config.randomSeed
            : (int)(System.DateTime.UtcNow.Ticks & int.MaxValue);
        SeedService.Initialize(seed);

        // 从配置同步水位阈值，确保后续 HexCellData 构造时 IsWater() 判定正确
        WaterLevelConfig.WaterLevel = _config.seaLevel;

        int x = _config.xNumber;
        int z = _config.zNumber;
        float InnerRadius = _config.InnerRadius;
        float OuterRadius = _config.OuterRadius;
        TerrainGenerator.TerrainHeights terrainHeights = new TerrainGenerator.TerrainHeights(0.05f, 3, 0.6f, _config.minHeight, _config.maxHeight);
        int minLongestLength = _config.minLongestLength;
        int maxLongestLength = _config.maxLongestLength;
        float riverSourceGenerationProbability = _config.RiverSourceGenerationProbability;

        System.Random terrainRandom = SeedService.GetRandom("Terrain");
        System.Random riverRandom = SeedService.GetRandom("River");
        System.Random landFormRandom = SeedService.GetRandom("LandForm");
        System.Random resourceRandom = SeedService.GetRandom("Resource");

        List<float> heights;
        if (_config.heightGenerationMode == Enums.HeightGenerationMode.PaletteMap && _config.heightPaletteMap != null)
        {
            Debug.Log($"[MapGenerator] 使用颜色图模式生成地形。纹理：{_config.heightPaletteMap.name}, 可读性：{_config.heightPaletteMap.isReadable}");
            heights = PaletteHeightGeneration(x, z, InnerRadius, OuterRadius, terrainRandom);
        }
        else
        {
            string reason = _config.heightGenerationMode != Enums.HeightGenerationMode.PaletteMap 
                ? "模式选择为 PerlinNoise" 
                : "颜色图纹理未分配";
            Debug.Log($"[MapGenerator] 使用 Perlin 噪声生成地形（{reason}）");
            heights = TerrainHeightGeneration(x, z, terrainHeights, terrainRandom);
        }

        hexVertices = HexCoordinatesGeneration(x, z, InnerRadius, OuterRadius, heights);

        InitializeMapDataService(hexVertices);

        RiverGenerator.RiverGeneration(x, z, minLongestLength, maxLongestLength, riverSourceGenerationProbability, _mapDataService, riverRandom);
        LandFormDataGeneration(hexVertices, landFormRandom);
        ResourceDataGeneration(hexVertices, resourceRandom);
    }

    private List<float> TerrainHeightGeneration(int xNumber, int zNumber, TerrainGenerator.TerrainHeights terrainHeights, System.Random random)
    {
        List<float> height = new List<float>();

        int[,] arrHeight = TerrainGenerator.GenerateTerrainHeight(xNumber, zNumber, random,
            terrainHeights.frequency, terrainHeights.octaves, terrainHeights.persistence,
            terrainHeights.minHeight, terrainHeights.maxHeight);

        for (int z = 0; z < arrHeight.GetLength(1); z++)
        {
            for (int x = 0; x < arrHeight.GetLength(0); x++)
            {
                height.Add(arrHeight[x, z]);
            }
        }

        if (height.Count > 0)
        {
            float max = height[0];
            for (int i = 1; i < height.Count; i++)
                if (height[i] > max) max = height[i];
            WaterLevelConfig.MaxHeight = max;
        }

        return height;
    }

    private List<float> PaletteHeightGeneration(int xNumber, int zNumber, float InnerRadius, float OuterRadius, System.Random random)
    {
        List<Vector3> worldCenters = ComputeWorldCenterCoordinates(xNumber, zNumber, InnerRadius, OuterRadius);

        int[,] arrHeight = TerrainGenerator.GenerateTerrainHeightFromPalette(
            xNumber, zNumber,
            _config.heightPaletteMap,
            _config.minHeight, _config.maxHeight, _config.seaLevel,
            _config.heightNoiseAmplitude, _config.heightNoiseFrequency,
            worldCenters,
            random);

        List<float> height = new List<float>();
        for (int z = 0; z < arrHeight.GetLength(1); z++)
        {
            for (int x = 0; x < arrHeight.GetLength(0); x++)
            {
                height.Add(arrHeight[x, z]);
            }
        }

        if (height.Count > 0)
        {
            float max = height[0];
            for (int i = 1; i < height.Count; i++)
                if (height[i] > max) max = height[i];
            WaterLevelConfig.MaxHeight = max;
        }

        return height;
    }

    private List<Vector3> ComputeWorldCenterCoordinates(int xNum, int zNum, float InnerRadius, float OuterRadius)
    {
        List<Vector3> coords = new List<Vector3>(xNum * zNum);
        for (int j = 0; j < zNum; j++)
        {
            int offset = j / 2;
            for (int i = 0; i < xNum; i++)
            {
                float x = (i - offset) * 2f * InnerRadius + j * InnerRadius;
                float z = j * 1.5f * OuterRadius;
                coords.Add(new Vector3(x, 0, z));
            }
        }
        return coords;
    }

    //�����ؿ���������ϵ�����������꣩
    private Vector3[] HexCoordinatesGeneration(int xNum, int zNum, float InnerRadius, float OuterRadius, List<float> height)
    {
        //����������Щ�ؿ�(��������������)
        //ȫ��ͼ�ؿ������������
        Vector3[] hexVertices = new Vector3[xNum * zNum];
        int generateOrder = 0;
        for (int j = 0; j < zNum; j++)
        {
            for (int i = 0; i < xNum; i++)
            {
                //ƫ��
                int offset = j / 2;
                hexVertices[j * xNum + i] = new Vector3(i - offset, -(i - offset) - j, j);

                //������������������ - ȫ��ͼ�ؿ����ĵ����������
                float x = (hexVertices[j * xNum + i].x * 2 * InnerRadius) + (hexVertices[j * xNum + i].z * 1 * InnerRadius);
                float y = 0;
                float z = hexVertices[j * xNum + i].z * 1.5f * OuterRadius;
                //����ȫ���ؿ�����ĵ���������
                //Debug.Log("new Vector3(x, y, z)��" + new Vector3(x, y, z));
                centerWorldCoordinates.Add(new Vector3(x, y, z));
                //���ӵؿ����ĵ����������_�ؿ������������_�ֵ�
                CenterWorldC_HexC.Add(new Vector3(x, y, z), hexVertices[j * xNum + i]);
                //���ӵؿ������������_�ؿ��HexCell_�ֵ䣨�ؿ�ʵ������������ȫ������Ϊ�޺������������޸ģ�
                //Debug.Log("height[generateOrder]��" + height[generateOrder]);
                HexCellData hexCellData = new HexCellData(Enums.HexType.NoRiver, generateOrder, hexVertices[j * xNum + i], new Vector3(x, y, z), height[generateOrder]);
                HexC_HexCellData.Add(hexVertices[j * xNum + i], hexCellData);
                //���ӵؿ����ɵ�˳��_�ؿ��HexCell_�ֵ�
                GenerateOrder_HexCellData.Add(generateOrder++, hexCellData);

                //����������
                //Debug.Log($" - {generateOrder} - �ŵؿ飬�����������ǣ�{hexVertices[j * xNum + i]}");
            }
        }
        generateOrder = 0;

        return hexVertices;
    }

    private void LandFormDataGeneration(Vector3[] hexVertices, System.Random random)
    {
        //��ò���ɹ������ڼ򵥵��������
        for (int j = 0; j < hexVertices.Length; j++)
        {
            //���������������ȡhexCell;
            HexCellData hexCellData = HexC_HexCellData[hexVertices[j]];
            //���ǡ����򺣡�������
            if (isLakeOrSea(hexCellData) || hexCellData.hasRiver) { continue; }

            // 【地图地貌配置化】按数据库权重表掷点；掷中空白保持 null
            hexCellData.landForm = LandFormSpawnRule.RollLandForm(_landFormDatabase, random);
        }

        // 【金矿扎堆】阶段二：簇生成。仅 clusterSpawn=true 的地貌（金矿）走固定 n 堆
        // 不规则扎堆；散落池保持原权重锁定随机流（同种子下其他地貌位置不变），
        // 簇外掷中该地貌的格由 RemoveScatteredForm 拦截改写为空白。
        MapLandFormSO clusterForm = LandFormClusterSpawnRule.FindClusterForm(_landFormDatabase);
        if (clusterForm != null)
        {
            List<HexCellData> allCells = _mapDataService.GetAllCells();
            HashSet<HexCellData> claimed = LandFormClusterSpawnRule.PlaceClusters(
                clusterForm, allCells, _mapDataService.GetNeighbors, SeedService.GetRandom("LandFormCluster"));
            LandFormClusterSpawnRule.RemoveScatteredForm(clusterForm, allCells, claimed);
        }
    }

    private void ResourceDataGeneration(Vector3[] hexVertices, System.Random random)
    {
        //��Դ���ɹ������ڼ򵥵��������
        for (int j = 0; j < hexVertices.Length; j++)
        {
            //���������������ȡhexCell;
            HexCellData hexCellData = HexC_HexCellData[hexVertices[j]];
            //���ǡ����򺣡�������
            if (isLakeOrSea(hexCellData) || hexCellData.hasRiver) { continue; }
            //【地图地貌配置化】有地貌的格不生成资源（与旧行为一致）
            if (hexCellData.landForm != null) { continue; }

            // 【地图资源配置化】按数据库权重表掷点；掷中空白保持 null
            hexCellData.resource = ResourceSpawnRule.RollResource(_resourceDatabase, random);
        }
    }

    //�ж�ĳ���ؿ��Ƿ�Ϊ����
    private bool isLakeOrSea(HexCellData hexCellData)
    {
        return WaterLevelConfig.IsWater(hexCellData);
    }
}
