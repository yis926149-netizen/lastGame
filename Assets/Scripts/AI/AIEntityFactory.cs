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
    private const float TechCultureBuildingPointsPerTurn = 10f;

    private readonly IMapDataService _mapDataService;
    private readonly DiContainer _container;
    private readonly IUnitDataProvider _unitDataProvider;
    private readonly IBuildingDataProvider _buildingDataProvider;
    private readonly IUIConfigProvider _uiConfigProvider;
    private readonly IUnitRepository _unitRepository;
    private readonly EnemyModelManager _enemyModelManager;
    private readonly MapVisualEventSO _mapVisualEvent;
    private readonly AIPlayerState _aiPlayerState;

    // 城市预制体：由 AIManager 从其 Inspector 序列化字段传入（场景引用无法直接注入到普通类）。
    public GameObject CityPrefab { get; set; }

    public AIEntityFactory(
        IMapDataService mapDataService,
        DiContainer container,
        IUnitDataProvider unitDataProvider,
        IBuildingDataProvider buildingDataProvider,
        IUIConfigProvider uiConfigProvider,
        IUnitRepository unitRepository,
        EnemyModelManager enemyModelManager,
        MapVisualEventSO mapVisualEvent,
        AIPlayerState aiPlayerState)
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
        h.BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>(Enums.BulidingType.City, g);
        h.movementCost = float.MaxValue;

        // 获取当前AI的城市编号（新城市的索引）
        int cityIndex = _enemyModelManager.AllocateCityIndex(AIIndex);
        var cityKey = new KeyValuePair<int, int>(AIIndex, cityIndex);

        buildingController.Player_City_Index = cityKey;

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

        // 隐藏建筑（初始时不可见，等探索后才显示）
        g.SetActive(false);
    }

    /// <summary>AI 单位生成</summary>
    public void GenerateUnit(int UnitIndex, Vector3 position)
    {
        GameObject g = Object.Instantiate(_unitDataProvider.GetUnitPrefab(UnitIndex));
        g.transform.SetParent(GameObject.Find("EnemyUnit").transform, false);
        g.transform.position = position;
        g.tag = "EnemyUnit";

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

        g.SetActive(false);
    }

    /// <summary>AI 建筑生成</summary>
    public void GenerateBuilding(int CardIndex, Vector3 position)
    {
        Vector3 v = _mapDataService.WorldToHexCoordinate(position);
        HexCellData h = _mapDataService.GetCellByWorldPosition(position);

        int bulidingTypeInt = CardIndex - (int)_unitDataProvider.GetUnitIconCount();
        GameObject g = Object.Instantiate(_buildingDataProvider.GetBuildingPrefab(bulidingTypeInt));
        g.transform.SetParent(GameObject.Find("EnemyBuilding").transform, false);
        g.transform.position = position;
        g.tag = "EnemyBuilding";

        BuildingController buildingController = g.AddComponent<BuildingController>();
        _container.Inject(buildingController);

        BuildingData buildingData = new BuildingData(
            (Enums.BulidingType)(bulidingTypeInt + 1),
            _buildingDataProvider,
            bulidingTypeInt);
        buildingController.buildingData = buildingData;
        buildingData.controller = buildingController;

        buildingController.bulidingType = (Enums.BulidingType)(bulidingTypeInt + 1);
        h.BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>((Enums.BulidingType)(bulidingTypeInt + 1), g);

        if (bulidingTypeInt == 0 || bulidingTypeInt == 1)
        {
            h.movementCost = float.MaxValue;
        }

        // 与玩家一致：建筑落地后扩展势力范围
        if (h.Player_City_Index.Key == AIIndex &&
            _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData.ContainsKey(AIIndex))
        {
            SphereOfInfluenceRules.Expand(
                _mapDataService,
                v,
                _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData[AIIndex],
                h.Player_City_Index
            );

            if (_enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData.TryGetValue(h.Player_City_Index, out var citySphere))
            {
                SphereOfInfluenceRules.Expand(_mapDataService, v, citySphere, h.Player_City_Index);
            }
            _mapVisualEvent.Raise();
        }

        // 科技文化建筑：增加每回合科文产量
        if (bulidingTypeInt == 3)
        {
            _aiPlayerState.TechCulture.TechPointsPerTurn += TechCultureBuildingPointsPerTurn;
            _aiPlayerState.TechCulture.CulturePointsPerTurn += TechCultureBuildingPointsPerTurn;
        }

        buildingController.Player_City_Index = h.Player_City_Index;

        // 单位UI画布 + 血条（共享样板；canvas 为空则中止，保留原行为）
        if (!SpawnUIWiring.WireBuildingCanvas(g, buildingController, Color.red, _container, _uiConfigProvider)) { return; }

        g.SetActive(false);
    }
}
