using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class GameFlowManager : MonoBehaviour, IInitializable
{
    [Inject] private IMapPresentationBootstrap _mapPresentationBootstrap;
    [Inject] private MapGenerator mapGenerator;
    [Inject] private IMapDataService _mapDataService;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private IAIManager _iaiManager;
    [Inject] private AudioManager _audioManager;
    [Inject] private EndGame _endGame;
    [Inject] private ITerritoryService _territoryService;
    [Inject] private ILogisticsService _logisticsService;
    [Inject] private IBuildingDataProvider _buildingDataProvider;
    [Inject] private DiContainer _container;
    [Inject] private IUIConfigProvider _uiConfigProvider;
    [Inject] private PlayerModelManager _playerModelManager;
    [Inject] private GoldWallet _goldWallet;
    [Inject] private EnemyModelManager _enemyModelManager;
    [Inject] private PublicBuildingGenerator _publicBuildingGenerator;
    [Inject] private LandFormMarkerManager _landFormMarkerManager;
    [Inject] private MapGenerationConfigSO _config;
    [Inject] private ArenaEventManager _arenaEventManager;

    // ── PlayerIndex 分配器（决策#22/#27）─────────────
    private int _nextPlayerIndex = 0;

    /// <summary>
    /// 动态分配 PlayerIndex。玩家固定0，真AI从1递增，公共建筑紧随其后。
    /// 调用顺序：玩家 → 真AI → 公共建筑（在 Initialize 中依次调用）。
    /// </summary>
    public int AllocatePlayerIndex()
    {
        return _nextPlayerIndex++;
    }

    public void Initialize()
    {
        Debug.Log("<color=lime>[GameFlowManager] ===== 游戏开局 =====</color>");
        _audioManager.PlayBGM("Theme_Mistery_But_Then_Happy_Loop");

        // 1. 地图生成
        mapGenerator.Generate();

        // 1.1 初始化唯一 Chunk 后端及后端无关地图视觉。
        _mapPresentationBootstrap.InitializeMapPresentation();

        // 1.2 【竞技场-阶段二】预留区标记（IsUnexplorable × 37）——
        // 必须在公共建筑生成与玩家出生点选择之前，供两者排除预留区
        _arenaEventManager.OnMapInitialized();

        // 1.5 【金矿提示图标】地图数据落定后为金矿堆创建提示浮标
        _landFormMarkerManager.CreateAllMarkers();

        // 2. 分配 PlayerIndex：玩家固定0（决策#22）
        _nextPlayerIndex = 0;
        int playerIndex = AllocatePlayerIndex(); // 0
        Debug.Assert(playerIndex == 0, "Player must be allocated PlayerIndex 0");

        // 3. AI 初始化（真AI 从 1 递增）
        _iaiManager.AIInit();
        int aiCount = 1; // 当前项目只有1个AI（AIIndex=1）
        for (int i = 0; i < aiCount; i++)
        {
            AllocatePlayerIndex(); // AI 占用 PlayerIndex 1
        }

        // 4. 【公共建筑系统-决策#38/#28】公共建筑生成（地形固定后、势力范围初始化前）
        _nextPlayerIndex = _publicBuildingGenerator.GenerateAll(_nextPlayerIndex);

        // 5. 玩家势力范围初始化
        PlayerInit();

        _logisticsService.RecalculateAll();

        _endGame.MarkInitializationComplete();
    }

    public void PlayerInit()
    {
        // 【探索重构-阶段7】初始化玩家金币
        _goldWallet.InitPlayer(0);

        // 玩家初始化：非水、无城、位于地图 x 列中间（第 xNumber/2 列）、底边缘前一行（z=1）的陆地格
        System.Random random = SeedService.GetRandom("Player");

        int middleColumn = _config.xNumber / 2;
        float targetRow = 1f; // 底边缘(z=0)的前一行
        var candidates = new List<HexCellData>();
        float bestDist = float.MaxValue;
        foreach (HexCellData cell in _mapDataService.GetAllCells())
        {
            if (cell != null &&
                cell.HexType != Enums.HexType.LakeOrSea &&
                cell.Player_City_Index.Equals(new KeyValuePair<int, int>(-1, -1)) &&
                // 【竞技场-阶段二】主城出生点排除预留区外 2 环（玩法文档 §7.3）
                !_arenaEventManager.IsNearReservedZone(cell, 2))
            {
                // 六边形行偏移：列号 i = HexCoordinate.x + floor(z / 2)；取最接近 (中间列, 目标行) 者
                float column = cell.HexCoordinate.x + Mathf.Floor(cell.HexCoordinate.z / 2f);
                float dist = Mathf.Abs(column - middleColumn) + Mathf.Abs(cell.HexCoordinate.z - targetRow);
                if (dist > bestDist) continue;
                if (dist < bestDist) { bestDist = dist; candidates.Clear(); }
                candidates.Add(cell);
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogError("玩家初始化失败：地图上没有可用的陆地格（水域阈值过高或高度范围过低）。");
            return;
        }

        HexCellData h = candidates[random.Next(candidates.Count)];
        mapGenerator.SpawnHexCenterPoint = h.RealCenterWorldCoordinate;

        // 【探索重构-阶段5.5】主城固有领地初始化 + 生成主城建筑模型
        GeneratePlayerMainCity(h);
        InitializeMainCityTerritory(h);
        _mapVisualEvent.Raise();
    }

    /// <summary>
    /// 生成玩家主城建筑模型（开局唯一主城）
    /// </summary>
    private void GeneratePlayerMainCity(HexCellData centerCell)
    {
        Vector3 position = centerCell.RealCenterWorldCoordinate;

        // 实例化主城模型
        GameObject city = Object.Instantiate(_buildingDataProvider.GetCityModel());
        city.transform.SetParent(GameObject.Find("PlayerBuilding")?.transform, false);
        city.transform.position = position;
        city.tag = "PlayerBuilding";

        // BuildingController 组件
        BuildingController controller = city.AddComponent<BuildingController>();
        _container.Inject(controller);

        BuildingData data = new BuildingData(Enums.BulidingType.City, _buildingDataProvider);
        controller.buildingData = data;
        data.controller = controller;
        controller.bulidingType = Enums.BulidingType.City;
        _endGame.RegisterMainCity(0, controller);

        // 地块绑定城市
        centerCell.BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>(
            Enums.BulidingType.City, city);
        centerCell.movementCost = float.MaxValue; // 城市格不可移动

        // 主城编号 (0, 0)——玩家索引0，主城索引0
        int cityIndex = 0;
        var cityKey = new KeyValuePair<int, int>(0, cityIndex);
        controller.Player_City_Index = cityKey;
        _logisticsService.RegisterMainCity(0, centerCell);

        // UI 画布 + 血条
        SpawnUIWiring.WireBuildingCanvas(city, controller, Color.green, _container, _uiConfigProvider);

        // 注册主城到势力范围管理器
        _playerModelManager.SingleCity_SphereOfInfluence_HexC_HexCellData[cityIndex] = 
            new Dictionary<Vector3, HexCellData>();
        _playerModelManager.CityCount = 1; // 主城是玩家唯一的城市
    }

    /// <summary>
    /// 主城开局初始化：主城格 + 一环（6格）直接圈入领地 + 标记已探索。
    /// 跳过探索成本检查（固有领地，免费）。
    /// </summary>
    private void InitializeMainCityTerritory(HexCellData centerCell)
    {
        // 主城格
        ClaimAndExplore(centerCell);

        // 周围一环
        for (int i = 0; i < 6; i++)
        {
            var neighbor = _mapDataService.GetNeighbor(centerCell, (Enums.HexDirection)i);
            if (neighbor != null)
                ClaimAndExplore(neighbor);
        }
    }

    /// <summary>
    /// 将地块圈入领地并标记已探索（仅供初始化使用，跳过成本）
    /// </summary>
    private void ClaimAndExplore(HexCellData cell)
    {
        if (cell == null || cell.HexType == Enums.HexType.LakeOrSea) return;
        cell.ExploreThisHexCell();
        _territoryService.Claim(cell);
    }
}
