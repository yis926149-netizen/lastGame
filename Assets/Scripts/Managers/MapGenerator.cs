using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class MapGenerator : MonoBehaviour
{
    //注入
    [Inject] private IUIConfigProvider uiConfigProvider;
    [Inject] private IMapDataService _mapDataService;
    [Inject] private MapGenerationConfigSO _config; 
    [Inject] private IEnvironmentModelsProvider environmentModelsProvider;

    //次卡
    public GameObject NextCardPlaceholder;

    //随机出生点的中心位置
    public Vector3 SpawnHexCenterPoint = new Vector3();

    //全部地块的中心点世界坐标
    [HideInInspector]
    public List<Vector3> centerWorldCoordinates = new List<Vector3>();
    //地块中心点的世界坐标_地块的六边形坐标_字典
    [HideInInspector]
    public Dictionary<Vector3, Vector3> CenterWorldC_HexC = new Dictionary<Vector3, Vector3>();
    //地块的六边形坐标_地块的HexCell_字典
    [HideInInspector]
    public Dictionary<Vector3, HexCellData> HexC_HexCellData = new Dictionary<Vector3, HexCellData>();
    //地块生成的顺序_地块的HexCell_字典
    [HideInInspector]
    public Dictionary<int, HexCellData> GenerateOrder_HexCellData = new Dictionary<int, HexCellData>();

    //地图运行时数据
    public List<Vector3> verticesList;
    public Mesh mesh;
    public GameObject gridGameObject;

    // 生成地图数据，并填充到 IMapDataService 中
    public Vector3[] Generate()
    {
        // 设置次卡占位对象
        uiConfigProvider.SetNextCardPlaceholder(NextCardPlaceholder);

        // 初始化静态噪声图
        HexMetrics.noiseSource = _config.noiseSource;

        // 地图数据生成，填充所有字典和坐标数据
        MapDataGeneration(out Vector3[] hexVertices);

        // 初始化地图服务，传入已生成好的数据
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

            //地图生成
            hexVertices: hexVertices,

            //地图运行时数据
            verticesList: verticesList,
            mesh: mesh,
            gridGameObject: gridGameObject
        );
    }

    private void MapDataGeneration(out Vector3[] hexVertices)
    {
        //读写数据层的初始设置
        int x = _config.xNumber;
        int z = _config.zNumber;
        float InnerRadius = _config.InnerRadius;
        float OuterRadius = _config.OuterRadius;
        TerrainGenerator.TerrainHeights terrainHeights = new TerrainGenerator.TerrainHeights(0.05f, 3, 0.6f, 0.4f, 0.5f);
        int minLongestLength = _config.minLongestLength;
        int maxLongestLength = _config.maxLongestLength;
        float riverSourceGenerationProbability = _config.RiverSourceGenerationProbability;

        //生成全部地块高度
        List<float> heights = TerrainHeightGeneration(x, z, terrainHeights);

        //构建地块间的数据联系（六边形坐标）0.866025404f * 3f
        hexVertices = HexCoordinatesGeneration(x, z, InnerRadius, OuterRadius, heights);

        // RiverGenerator 依赖 IMapDataService 读地块数据，这里先完成一次初始化
        // 否则当河流源头数量 > 0 时会在 HexMapService 内部触发空引用。
        InitializeMapDataService(hexVertices);

        //标定河流地块
        RiverGenerator.RiverGeneration(x, z, minLongestLength, maxLongestLength, riverSourceGenerationProbability, _mapDataService);

        //标定地块的地貌和资源类型
        LandFormDataGeneration(hexVertices);
        ResourceDataGeneration(hexVertices);
    }

    //生成全部地块高度
    private List<float> TerrainHeightGeneration(int xNumber, int zNumber, TerrainGenerator.TerrainHeights terrainHeights)
    {
        //地块高度在这处理
        List<float> height = new List<float>();
        /*
        //频率：控制地形区块大小（值越小，区块越大越连贯）-0.03~0.08（网格越大，值越小）
        //float frequency = UnityEngine.Random.Range(0.05f, 0.08f);
        ///float frequency = 0.05f;
        //八度：分层叠加细节（值越多，细节越丰富但不碎片化）-  2~4（3 个高度无需过多细节）
        //int octaves = UnityEngine.Random.Range(2, 4);
        ///int octaves = 3;
        //持续性：控制高频细节的贡献度（值越小，地形越平滑）- 0.4~0.6
        //float persistence = UnityEngine.Random.Range(0.4f, 0.5f);
        ///float persistence = 0.6f;
        //阈值1 - （0，T1）
        //float T1 = UnityEngine.Random.Range(0.5f, 0.6f);
        ///float T1 = 0.4f;
        //阈值2 - （T1,T2）
        //float T2 = UnityEngine.Random.Range(T1 + 0.05f, 0.7f);
        ///float T2 = 0.5f;
        */
        int[,] arrHeight = TerrainGenerator.GenerateTerrainHeight(xNumber, zNumber, terrainHeights.frequency, terrainHeights.octaves, terrainHeights.persistence, terrainHeights.T1, terrainHeights.T2);

        for (int i = 0; i < arrHeight.GetLength(0); i++)
        {
            for (int j = 0; j < arrHeight.GetLength(1); j++)
            {
                //Debug.Log("height：" + arrHeight[i, j]);
                height.Add(arrHeight[i, j]);
            }
        }

        return height;
    }

    //构建地块间的数据联系（六边形坐标）
    private Vector3[] HexCoordinatesGeneration(int xNum, int zNum, float InnerRadius, float OuterRadius, List<float> height)
    {
        //计算生成哪些地块(计算六边形坐标)
        //全地图地块的六边形坐标
        Vector3[] hexVertices = new Vector3[xNum * zNum];
        int generateOrder = 0;
        for (int j = 0; j < zNum; j++)
        {
            for (int i = 0; i < xNum; i++)
            {
                //偏移
                int offset = j / 2;
                hexVertices[j * xNum + i] = new Vector3(i - offset, -(i - offset) - j, j);

                //按照六边形坐标生成 - 全地图地块中心点的世界坐标
                float x = (hexVertices[j * xNum + i].x * 2 * InnerRadius) + (hexVertices[j * xNum + i].z * 1 * InnerRadius);
                float y = 0;
                float z = hexVertices[j * xNum + i].z * 1.5f * OuterRadius;
                //保存全部地块的中心点世界坐标
                //Debug.Log("new Vector3(x, y, z)：" + new Vector3(x, y, z));
                centerWorldCoordinates.Add(new Vector3(x, y, z));
                //添加地块中心点的世界坐标_地块的六边形坐标_字典
                CenterWorldC_HexC.Add(new Vector3(x, y, z), hexVertices[j * xNum + i]);
                //添加地块的六边形坐标_地块的HexCell_字典（地块实心区域类型先全部设置为无河流，后面再修改）
                //Debug.Log("height[generateOrder]：" + height[generateOrder]);
                HexCellData hexCellData = new HexCellData(Enums.HexType.NoRiver, generateOrder, hexVertices[j * xNum + i], new Vector3(x, y, z), height[generateOrder]);
                HexC_HexCellData.Add(hexVertices[j * xNum + i], hexCellData);
                //添加地块生成的顺序_地块的HexCell_字典
                GenerateOrder_HexCellData.Add(generateOrder++, hexCellData);

                //六边形坐标
                //Debug.Log($" - {generateOrder} - 号地块，六边形坐标是：{hexVertices[j * xNum + i]}");
            }
        }
        generateOrder = 0;

        return hexVertices;
    }

    private void LandFormDataGeneration(Vector3[] hexVertices)
    {
        System.Random random = new System.Random();

        //地貌生成规则：现在简单的随机生成
        for (int j = 0; j < hexVertices.Length; j++)
        {
            //根据六边形坐标获取hexCell;
            HexCellData hexCellData = HexC_HexCellData[hexVertices[j]];
            //若是“湖或海”即跳过
            if (isLakeOrSea(hexCellData)) { continue; }
            //地貌
            hexCellData.landFormType = (Enums.LandFormType)Mathf.Clamp(random.Next(0, Enum.GetValues(typeof(Enums.LandFormType)).Length + 9), 0, Enum.GetValues(typeof(Enums.LandFormType)).Length - 1);
            if (hexCellData.landFormType == Enums.LandFormType.None) continue;
        }
    }

    private void ResourceDataGeneration(Vector3[] hexVertices)
    {
        System.Random random = new System.Random();
        GameObject Resource = new GameObject("Resource");

        //资源生成规则：现在简单的随机生成
        for (int j = 0; j < hexVertices.Length; j++)
        {
            //根据六边形坐标获取hexCell;
            HexCellData hexCellData = HexC_HexCellData[hexVertices[j]];
            //若是“湖或海”即跳过
            if (isLakeOrSea(hexCellData)) { continue; }
            //若存在“地貌”则跳过
            if (hexCellData.landFormType != Enums.LandFormType.None) { continue; }

            //地貌
            hexCellData.resourceType = (Enums.ResourceType)Mathf.Clamp(random.Next(0, Enum.GetValues(typeof(Enums.ResourceType)).Length + 13), 0, Enum.GetValues(typeof(Enums.ResourceType)).Length - 1);
            if (hexCellData.resourceType == Enums.ResourceType.None) continue;
        }
    }

    //判断某个地块是否为湖或海
    private bool isLakeOrSea(HexCellData hexCellData)
    {
        //若高度为0，则为湖或海
        return !(hexCellData.Height > 0);
    }
}
