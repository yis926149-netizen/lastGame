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
    [Inject] private ExplorationRewardConfigSO _explorationRewardConfig;

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

    // ���ɵ�ͼ���ݣ�����䵽 IMapDataService ��
    public Vector3[] Generate()
    {
        // ���ôο�ռλ����
        uiConfigProvider.SetNextCardPlaceholder(NextCardPlaceholder);

        // ��ʼ����̬����ͼ
        HexMetrics.noiseSource = _config.noiseSource;
        HexMetrics.noiseScale = _config.visualPerturbFrequency;
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
            hexVertices: hexVertices
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

        // 【程序化山脉】生成顺序（决策 ③）：地形 → 山 → 其他地貌 → 河 → 资源
        // 山脉由 RidgeGenerator 专属 pass 生成（决策 ⑬：不参与散落权重池），
        // 山格数据（固化参数快照 + d/s）在生成时写入格级数据（决策 ②）。
        System.Random mountainRandom = SeedService.GetRandom("Mountain");
        List<MountainRidgeData> ridges = RidgeGenerator.Generate(
            _config.mountainConfig, _mapDataService.GetAllCells(), _mapDataService.GetNeighbors, mountainRandom,
            _config.mountainHeightScale);
        if (ridges.Count > 0)
            Debug.Log($"[MapGenerator] 山脉生成：{ridges.Count} 条脊线、共 {CountMountainCells(ridges)} 个山脉地块。");

        LandFormDataGeneration(hexVertices, landFormRandom);
        RiverGenerator.RiverGeneration(x, z, minLongestLength, maxLongestLength, riverSourceGenerationProbability, _mapDataService, riverRandom);
        ResourceDataGeneration(hexVertices, resourceRandom);
    }

    private static int CountMountainCells(List<MountainRidgeData> ridges)
    {
        int count = 0;
        foreach (MountainRidgeData ridge in ridges)
            count += ridge.mountainCellCount;
        return count;
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
            // Palette generation derives its water/flat/high buckets from the configured
            // height range. Keep rendering on the same range even when this particular
            // palette does not sample a max-height cell, otherwise the render threshold
            // shifts down and green cells rounded to height 3 can appear as highland.
            WaterLevelConfig.MaxHeight = _config.maxHeight;
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
            // 【程序化山脉】山脉先于其他地貌生成：散落地貌跳过已有山格（决策 ③/⑫）
            if (isLakeOrSea(hexCellData) || MountainCellRule.IsMountainCell(hexCellData)) { continue; }

            // 【地图地貌配置化】按数据库权重表掷点；掷中空白保持 null
            hexCellData.landForm = LandFormSpawnRule.RollLandForm(_landFormDatabase, random);
        }

        // 【金矿扎堆 + 程序化山脉】多簇遍历（决策 ⑮）：按数据库顺序处理所有 clusterSpawn 地貌，
        // 共享占用集合保证簇间互斥；山脉地貌不入数据库（RidgeGenerator 专属 pass），山格不参与簇生长。
        LandFormClusterSpawnRule.PlaceAllClusters(
            _landFormDatabase, _mapDataService.GetAllCells(), _mapDataService.GetNeighbors,
            SeedService.GetRandom("LandFormCluster"));
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

    /// <summary>
    /// 【探索奖励预生成】地块奖励快照固化。必须在公共建筑占位格（含外一环）与竞技场预留区
    /// 被标记 IsUnexplorable 之后调用（GameFlowManager.Initialize 在公共建筑生成后、势力范围
    /// 初始化前调用），否则会为永不可探索的格生成无法被消费的快照。
    /// 确定性：独立随机流（SeedService "ExplorationReward"），与调用时机无关。
    /// </summary>
    public void GenerateExplorationRewards()
    {
        System.Random explorationRewardRandom = SeedService.GetRandom("ExplorationReward");
        foreach (HexCellData cell in _mapDataService.GetAllCells())
        {
            if (cell == null) continue;
            if (WaterLevelConfig.IsWater(cell) || cell.IsUnexplorable)
            {
                cell.SetExplorationReward(null);
                continue;
            }

            cell.SetExplorationReward(_explorationRewardConfig.GenerateReward(explorationRewardRandom));
        }
    }

    //�ж�ĳ���ؿ��Ƿ�Ϊ����
    private bool isLakeOrSea(HexCellData hexCellData)
    {
        return WaterLevelConfig.IsWater(hexCellData);
    }
}
