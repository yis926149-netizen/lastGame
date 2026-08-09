using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

//****************************************
//功能说明：AI 实体工厂。负责敌方城市/单位/建筑的实例化、注入、UI 画布与势力范围。
//         方法体与拆分前 AIManager.AICityGenerator / AIUnitGenerator / AIBuildingGenerator 一致。
//         注：AIIndex 暂固定 1；Tier 3 多阵营化时改为按 aiIndex 参数化。
//****************************************

public class AIEntityFactory
{
    private const int AIIndex = 1;

    private readonly IMapDataService _mapDataService;
    private readonly DiContainer _container;
    private readonly IUnitDataProvider _unitDataProvider;
    private readonly IBuildingDataProvider _buildingDataProvider;
    private readonly IUIConfigProvider _uiConfigProvider;
    private readonly IUnitRepository _unitRepository;
    private readonly EnemyModelManager _enemyModelManager;
    private readonly MapVisualEventSO _mapVisualEvent;
    private readonly AIPlayerState _aiPlayerState;
    // 【探索重构-阶段6】AIFogService 已移除
    private readonly GameLoop _gameLoop;
    private readonly UnitMovementSystem _movementSystem;
    private readonly UnitRemovalService _unitRemovalService;
    private readonly CombatResolver _combatResolver;
    private readonly PublicBuildingMarkerManager _publicBuildingMarkerManager;
    private readonly EndGame _endGame;
    private readonly ILogisticsService _logisticsService;

    // 城市预制体：经 IBuildingDataProvider 从 BuildingDatabaseSO 读取（AI 专用 enemyCityModel，留空回退 cityModel）。
    public GameObject CityPrefab => _buildingDataProvider.GetEnemyCityModel();

    public AIEntityFactory(
        IMapDataService mapDataService,
        DiContainer container,
        IUnitDataProvider unitDataProvider,
        IBuildingDataProvider buildingDataProvider,
        IUIConfigProvider uiConfigProvider,
        IUnitRepository unitRepository,
        EnemyModelManager enemyModelManager,
        MapVisualEventSO mapVisualEvent,
        AIPlayerState aiPlayerState,
        GameLoop gameLoop,
        UnitMovementSystem movementSystem,
        UnitRemovalService unitRemovalService,
        CombatResolver combatResolver,
        PublicBuildingMarkerManager publicBuildingMarkerManager,
        EndGame endGame,
        ILogisticsService logisticsService)
    {
        _mapDataService = mapDataService;
        _container = container;
        _unitDataProvider = unitDataProvider;
        _buildingDataProvider = buildingDataProvider;
        _uiConfigProvider = uiConfigProvider;
        _unitRepository = unitRepository;
        _enemyModelManager = enemyModelManager;
        _mapVisualEvent = mapVisualEvent;
        _aiPlayerState = aiPlayerState;
        // 【探索重构-阶段6】AIFogService 参数已移除
        _gameLoop = gameLoop;
        _movementSystem = movementSystem;
        _unitRemovalService = unitRemovalService;
        _combatResolver = combatResolver;
        _publicBuildingMarkerManager = publicBuildingMarkerManager;
        _endGame = endGame;
        _logisticsService = logisticsService;
    }

    /// <summary>AI 建城</summary>
    public void GenerateCity(Vector3 position)
    {
        HexCellData h = _mapDataService.GetCellByWorldPosition(position);

        GameObject g = Object.Instantiate(CityPrefab);
        g.transform.SetParent(GameObject.Find("EnemyBuilding").transform, false);
        g.transform.position = position;
        g.tag = "EnemyBuilding";

        BuildingController buildingController = g.AddComponent<BuildingController>();
        _container.Inject(buildingController);

        BuildingData buildingData = new BuildingData(Enums.BulidingType.City, _buildingDataProvider);
        buildingController.buildingData = buildingData;
        buildingData.controller = buildingController;

        buildingController.bulidingType = Enums.BulidingType.City;
        _endGame.RegisterMainCity(AIIndex, buildingController);
        h.BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>(Enums.BulidingType.City, g);
        h.movementCost = float.MaxValue;

        // 获取当前AI的城市编号（新城市的索引）
        int cityIndex = _enemyModelManager.AllocateCityIndex(AIIndex);
        var cityKey = new KeyValuePair<int, int>(AIIndex, cityIndex);

        buildingController.Player_City_Index = cityKey;
        _logisticsService.RegisterMainCity(AIIndex, h);

        // 为该城市创建单独的势力范围字典
        if (!_enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData.ContainsKey(cityKey))
        {
            _enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData[cityKey] = new Dictionary<Vector3, HexCellData>();
        }

        // 单位UI画布 + 血条（共享样板；canvas 为空则中止，保留原行为）
        if (!SpawnUIWiring.WireBuildingCanvas(g, buildingController, Color.red, _container, _uiConfigProvider)) { return; }

        // 获取AI的总势力范围字典
        var aiTotalSphere = _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData;
        if (!aiTotalSphere.ContainsKey(AIIndex))
            aiTotalSphere[AIIndex] = new Dictionary<Vector3, HexCellData>();

        // 扩展至总势力范围（同时设置地块归属）
        SphereOfInfluenceRules.Expand(
            _mapDataService,
            _mapDataService.WorldToHexCoordinate(g.transform.position),
            aiTotalSphere[AIIndex],
            cityKey
        );

        // 扩展至该城市自己的势力范围
        SphereOfInfluenceRules.Expand(
            _mapDataService,
            _mapDataService.WorldToHexCoordinate(g.transform.position),
            _enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData[cityKey],
            cityKey
        );

        // 增加该AI的城市计数
        if (!_enemyModelManager.CityCount.ContainsKey(AIIndex))
            _enemyModelManager.CityCount[AIIndex] = 0;
        _enemyModelManager.CityCount[AIIndex]++;

        // 触发地图视觉更新（势力范围边缘线）
        _mapVisualEvent.Raise();

        // 【探索重构-阶段1】AI城市始终可见，不再隐藏
    }

    /// <summary>AI 单位生成</summary>
    public void GenerateUnit(int UnitIndex, Vector3 position)
    {
        GameObject g = Object.Instantiate(_unitDataProvider.GetEnemyUnitPrefab(UnitIndex));
        g.transform.SetParent(GameObject.Find("EnemyUnit").transform, false);
        g.transform.position = position;
        g.tag = "EnemyUnit";

        // 调试：检查 Animator 组件
        var animator = g.GetComponent<Animator>();
        if (animator == null)
        {
            UnityEngine.Debug.LogWarning($"[AIEntityFactory] Unit {UnitIndex} at {position} has NO Animator component!");
        }
        else
        {
            UnityEngine.Debug.Log($"[AIEntityFactory] Unit {UnitIndex} Animator: enabled={animator.enabled}, controller={animator.runtimeAnimatorController?.name ?? "NULL"}");
        }

        g.AddComponent<UnitMovementController>();
        _container.InjectGameObject(g);

        CharacterData characterData = new CharacterData(UnitIndex, g, g.GetComponent<UnitMovementController>(), _unitDataProvider.GetUnitData(UnitIndex));
        g.GetComponent<UnitMovementController>().characterData = characterData;

        // 使用仓库添加敌方单位
        _unitRepository.AddEnemyUnit(AIIndex, g, characterData);

        g.GetComponent<UnitMovementController>().PlayerIndex = AIIndex;

        // 面板数据初始化
        CharacterData.InfoPanelData infoPanelData = new CharacterData.InfoPanelData();
        infoPanelData.sprite = _unitDataProvider.GetUnitIcon(characterData.UnitID);
        infoPanelData.name = characterData.unitData.unitName;
        infoPanelData.skillIcon = _unitDataProvider.GetSkillIcon(characterData.UnitID);
        infoPanelData.InfoDatas = new List<KeyValuePair<KeyValuePair<Sprite, string>, float>>();

        KeyValuePair<Sprite, string> Movement = new KeyValuePair<Sprite, string>(_uiConfigProvider.GetMovementPointsIcon(), "剩余移动力");
        KeyValuePair<Sprite, string> MeleeAttack = new KeyValuePair<Sprite, string>(_uiConfigProvider.GetMeleeAttackPointsIcon(), "攻击力");

        if (characterData.UnitID == 0)
        {
            infoPanelData.InfoDatas.Add(new KeyValuePair<KeyValuePair<Sprite, string>, float>(Movement, characterData.unitData.MovementPoints));
        }
        else
        {
            infoPanelData.InfoDatas.Add(new KeyValuePair<KeyValuePair<Sprite, string>, float>(MeleeAttack, characterData.unitData.BasicAttackValue));
            infoPanelData.InfoDatas.Add(new KeyValuePair<KeyValuePair<Sprite, string>, float>(Movement, characterData.unitData.MovementPoints));
        }
        characterData.infoPanelData = infoPanelData;

        // 单位UI画布（共享样板 SpawnUIWiring；敌方单位血条为红色）
        SpawnUIWiring.WireUnitCanvas(g, characterData, Color.red, _container, _uiConfigProvider);

        HexCellData h = _mapDataService.GetCellByWorldPosition(position);
        h.SetHaveUnit(true, g);

        // 【批次 D】挂载 AIUnitBrain，注入全部依赖（含 CombatResolver 和建城依赖），注册到 GameLoop
        var brain = g.AddComponent<AIUnitBrain>();
        brain.Initialize(
            characterData,
            UnitStrategyFactory.Create(_unitDataProvider.TryGetUnitConfig(UnitIndex, out var unitConfig) ? unitConfig : null),
            _mapDataService,
            _unitRepository,
            _movementSystem,
            combatResolver: _combatResolver,
            factory: this,
            unitRemovalService: _unitRemovalService,
            markerManager: _publicBuildingMarkerManager);
        _gameLoop.Register(brain);

        // 【探索重构-阶段1】AI单位始终可见，不再隐藏
    }

    /// <summary>AI 建筑生成（对象化入口：直接消费建筑配置）。</summary>
    public void GenerateBuilding(BuildingConfigSO config, Vector3 position)
    {
        GenerateBuildingInternal(config, config != null ? config.buildingId : 0, position);
    }

    private void GenerateBuildingInternal(BuildingConfigSO config, int bulidingTypeInt, Vector3 position)
    {
        Vector3 v = _mapDataService.WorldToHexCoordinate(position);
        HexCellData h = _mapDataService.GetCellByWorldPosition(position);

        GameObject prefab = config != null && config.enemyBuildingModel != null
            ? config.enemyBuildingModel
            : (config != null ? config.buildingModel : _buildingDataProvider.GetBuildingPrefab(bulidingTypeInt));
        GameObject g = Object.Instantiate(prefab);
        g.transform.SetParent(GameObject.Find("EnemyBuilding").transform, false);
        g.transform.position = position;
        g.tag = "EnemyBuilding";

        BuildingController buildingController = g.AddComponent<BuildingController>();
        _container.Inject(buildingController);

        Enums.BulidingType buildingType = config != null ? config.buildingType : (Enums.BulidingType)(bulidingTypeInt + 1);
        BuildingData buildingData = new BuildingData(
            buildingType,
            _buildingDataProvider,
            bulidingTypeInt);
        buildingController.buildingData = buildingData;
        buildingData.controller = buildingController;

        buildingController.bulidingType = buildingType;
        h.BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>(buildingType, g);

        if (config != null ? config.blocksMovement : (bulidingTypeInt == 0 || bulidingTypeInt == 1))
        {
            h.movementCost = float.MaxValue;
        }

        // 【探索重构-阶段5.5】建筑部署不拓展势力范围。势力范围仅由探索和公共建筑占领产生。

        // 科技/文化系统已移除：科技文化建筑不再产生每回合产量。

        buildingController.Player_City_Index = h.Player_City_Index;

        // 单位UI画布 + 血条（共享样板；canvas 为空则中止，保留原行为）
        if (!SpawnUIWiring.WireBuildingCanvas(g, buildingController, Color.red, _container, _uiConfigProvider)) { return; }

        if (buildingController.bulidingType == Enums.BulidingType.Barracks)
        {
            var spawner = g.AddComponent<BarracksSpawner>();
            _container.Inject(spawner);
            if (config != null && config.producedUnit != null)
            {
                spawner.Initialize(config.producedUnit);
            }
        }

        if (buildingController.bulidingType == Enums.BulidingType.ArrowTower)
        {
            var shooter = g.GetComponent<ArrowTowerShooter>() ?? g.AddComponent<ArrowTowerShooter>();
            _container.Inject(shooter);
        }

        // 【探索重构-阶段1】AI建筑始终可见，不再隐藏
    }
}
