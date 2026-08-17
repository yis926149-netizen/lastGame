using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

public class BarracksSpawner : MonoBehaviour
{
    [Inject] private IMapDataService _mapDataService;
    [Inject] private GameLoop _gameLoop;
    [Inject] private UnitMovementSystem _movementSystem;
    [Inject(Optional = true)] private ILogisticsService _logisticsService;
    [Inject] private PlayerModelManager _playerModelManager;
    [Inject] private IUnitDataProvider _unitData;
    [Inject] private IUnitRepository _unitRepository;
    [Inject] private DiContainer _container;
    [Inject] private CombatResolver _combatResolver;
    [Inject] private IBuildingDataProvider _buildingData;
    [Inject] private IUIConfigProvider _uiConfig;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private UnitRemovalService _unitRemovalService;
    [Inject] private PublicBuildingMarkerManager _publicBuildingMarkerManager;
    [InjectOptional] private AIEntityFactory _aiFactory;

    [SerializeField] private float _spawnInterval = 15f;
    [SerializeField] private int _spawnUnitID = 1;
    private UnitConfigSO _producedUnit;   // 由建筑配置注入（BuildingConfigSO.producedUnit），优先于 _spawnUnitID
    private float _timer;
    private bool _isPlayer;
    private Slider _productionProgressBar;

    /// <summary>由建筑生成器显式注入产出单位配置（动态 AddComponent 后调用，不能依赖 Inspector）。</summary>
    public void Initialize(UnitConfigSO producedUnit) => _producedUnit = producedUnit;

    private void Awake()
    {
        _isPlayer = gameObject.CompareTag("PlayerBuilding");
        _timer = 0f;
    }

    private void Start()
    {
        CreateProductionProgressBar();
        UpdateProductionProgressBar();
    }

    void Update()
    {
        if (_gameLoop == null || _gameLoop.IsPaused) return;

        // 【断供方案-阶段2】统一走门控：断供即失能（暂停生产、保留进度）
        BuildingBase building = GetComponent<BuildingBase>();
        if (building != null && !building.IsFunctional)
        {
            return;
        }

        if (_timer < _spawnInterval)
        {
            _timer = Mathf.Min(_spawnInterval, _timer + Time.deltaTime);
            UpdateProductionProgressBar();
        }

        if (_timer < _spawnInterval) return;
        if (!TrySpawnUnit()) return;

        _timer = 0f;
        UpdateProductionProgressBar();
    }

    private bool TrySpawnUnit()
    {
        Vector3 hexCoord = _mapDataService.WorldToHexCoordinate(transform.position);
        HexCellData centerHex = _mapDataService.GetCell(hexCoord);
        if (centerHex == null) return false;

        HexCellData targetHex = FindAdjacentEmptyHex(centerHex);
        if (targetHex == null) return false;

        Vector3 spawnPos = targetHex.RealCenterWorldCoordinate;

        if (_isPlayer)
        {
            return SpawnPlayerUnit(spawnPos);
        }
        else if (_aiFactory != null)
        {
            _aiFactory.GenerateUnit(_spawnUnitID, spawnPos);
            return true;
        }

        return false;
    }

    private HexCellData FindAdjacentEmptyHex(HexCellData center)
    {
        // 【断供方案-阶段2】阵营从建筑归属派生（吞并后自动跟随新主），不再用 Awake 快照
        BuildingBase building = GetComponent<BuildingBase>();
        int factionId = building != null && building.Player_City_Index.Key >= 0
            ? building.Player_City_Index.Key
            : (_isPlayer ? 0 : 1);

        for (int i = 0; i < 6; i++)
        {
            HexCellData neighbor = _mapDataService.GetNeighbor(center, (Enums.HexDirection)i);
            if (neighbor == null) continue;
            // 【程序化山脉-阶段 7.6】统一部署资格（决策 ①）：山格/水域不可部署（兵营生产入口收口）
            if (!MountainCellRule.CanSpawnUnitOnCell(neighbor)) continue;
            if (neighbor.HexType == Enums.HexType.LakeOrSea) continue;
            if (neighbor.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding) continue;
            if (neighbor.IsHaveUnit()) continue;
            if (_movementSystem.IsDestinationReserved(neighbor.HexCoordinate)) continue;
            if (_logisticsService != null && !_logisticsService.IsLogisticsConnected(neighbor, factionId)) continue;
            return neighbor;
        }
        return null;
    }

    private bool SpawnPlayerUnit(Vector3 position)
    {
        UnitConfigSO unitConfig = _producedUnit != null
            ? _producedUnit
            : (_unitData.TryGetUnitConfig(_spawnUnitID, out var cfg) ? cfg : null);

        GameObject prefab = unitConfig != null ? unitConfig.unitModel : _unitData.GetUnitPrefab(_spawnUnitID);
        Transform parent = GameObject.Find("PlayerUnit")?.transform;
        Canvas prefabCanvas = prefab != null ? prefab.GetComponentInChildren<Canvas>() : null;
        bool hasUnitUi = prefabCanvas != null &&
                         prefabCanvas.transform.childCount >= 2 &&
                         prefabCanvas.transform.GetChild(1).childCount >= 3 &&
                         prefabCanvas.transform.GetChild(1).GetComponent<Slider>() != null;
        if (prefab == null || parent == null || !hasUnitUi) return false;

        int unitID = unitConfig != null ? unitConfig.Id : _spawnUnitID;

        GameObject g = Object.Instantiate(prefab);
        g.transform.SetParent(parent, false);
        g.transform.position = position;
        g.tag = "PlayerUnit";

        g.AddComponent<UnitMovementController>();
        _container.InjectGameObject(g);

        CharacterData characterData = new CharacterData(
            unitID,
            g,
            g.GetComponent<UnitMovementController>(),
            _unitData.GetUnitData(unitID));

        g.GetComponent<UnitMovementController>().characterData = characterData;
        g.GetComponent<UnitMovementController>().PlayerIndex = 0;

        CharacterData.InfoPanelData infoPanelData = new CharacterData.InfoPanelData();
        infoPanelData.sprite = unitConfig != null ? unitConfig.cardSprite : _unitData.GetCard(characterData.UnitID);
        infoPanelData.name = characterData.unitData.unitName;
        infoPanelData.skillIcon = unitConfig != null ? unitConfig.skillIcon : _unitData.GetSkillIcon(characterData.UnitID);
        infoPanelData.InfoDatas = new List<KeyValuePair<KeyValuePair<Sprite, string>, float>>();

        KeyValuePair<Sprite, string> Movement = new KeyValuePair<Sprite, string>(_uiConfig.GetMovementPointsIcon(), "剩余移动力");
        KeyValuePair<Sprite, string> MeleeAttack = new KeyValuePair<Sprite, string>(_uiConfig.GetMeleeAttackPointsIcon(), "攻击力");

        bool isSettler = unitConfig != null
            ? _unitData.GetUnitStrategyType(unitID) == UnitStrategyType.Settler
            : characterData.UnitID == 0;
        if (isSettler)
        {
            infoPanelData.InfoDatas.Add(new KeyValuePair<KeyValuePair<Sprite, string>, float>(Movement, characterData.unitData.MovementPoints));
        }
        else
        {
            infoPanelData.InfoDatas.Add(new KeyValuePair<KeyValuePair<Sprite, string>, float>(MeleeAttack, characterData.unitData.BasicAttackValue));
            infoPanelData.InfoDatas.Add(new KeyValuePair<KeyValuePair<Sprite, string>, float>(Movement, characterData.unitData.MovementPoints));
        }
        characterData.infoPanelData = infoPanelData;

        SpawnUIWiring.WireUnitCanvas(g, characterData, Color.green, _container, _uiConfig);

        Vector3 hexCoord = _mapDataService.WorldToHexCoordinate(g.transform.position);
        HexCellData h = _mapDataService.GetCell(hexCoord);
        _unitRepository.AddPlayerUnit(g, characterData);
        h.SetHaveUnit(true, g);

        var brain = g.AddComponent<PlayerUnitBrain>();
        brain.Initialize(
            characterData,
            UnitStrategyFactory.Create(_unitData.GetUnitStrategyType(unitID)),
            _mapDataService,
            _unitRepository,
            _movementSystem,
            combatResolver: _combatResolver,
            container: _container,
            buildingData: _buildingData,
            uiConfig: _uiConfig,
            playerModelManager: _playerModelManager,
            mapVisualEvent: _mapVisualEvent,
            unitRemovalService: _unitRemovalService,
            audioManager: null,
            markerManager: _publicBuildingMarkerManager);
        _gameLoop.Register(brain);

        _mapVisualEvent.Raise();
        return true;
    }

    private void CreateProductionProgressBar()
    {
        var building = GetComponent<BuildingBase>();
        Slider healthBar = building != null ? building.uiHealthBar : null;
        if (healthBar == null) return;

        GameObject progressObject = Object.Instantiate(healthBar.gameObject, healthBar.transform.parent);
        progressObject.name = "ProductionProgressBar";
        progressObject.transform.SetSiblingIndex(healthBar.transform.GetSiblingIndex() + 1);

        RectTransform rect = progressObject.GetComponent<RectTransform>();
        if (rect != null)
            rect.anchoredPosition += new Vector2(0f, -12f);

        UIController inheritedController = progressObject.GetComponent<UIController>();
        if (inheritedController != null)
            Object.Destroy(inheritedController);

        _productionProgressBar = progressObject.GetComponent<Slider>();
        if (_productionProgressBar == null)
        {
            Object.Destroy(progressObject);
            return;
        }

        _productionProgressBar.interactable = false;
        UITool.TrySetSliderFillColor(_productionProgressBar, new Color(1f, 0.75f, 0.2f));
    }

    private void UpdateProductionProgressBar()
    {
        if (_productionProgressBar == null) return;
        _productionProgressBar.value = _spawnInterval > 0f
            ? Mathf.Clamp01(_timer / _spawnInterval)
            : 1f;
    }
}
