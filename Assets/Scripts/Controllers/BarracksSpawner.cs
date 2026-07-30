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
    private float _timer;
    private bool _isPlayer;
    private Slider _productionProgressBar;

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

        if (_logisticsService != null)
        {
            var hexCoord = _mapDataService.WorldToHexCoordinate(transform.position);
            var centerHex = _mapDataService.GetCell(hexCoord);
            if (centerHex != null && !_logisticsService.IsLogisticsConnected(centerHex, _isPlayer ? 0 : 1))
            {
                return;
            }
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
        int factionId = _isPlayer ? 0 : 1;
        for (int i = 0; i < 6; i++)
        {
            HexCellData neighbor = _mapDataService.GetNeighbor(center, (Enums.HexDirection)i);
            if (neighbor == null) continue;
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
        GameObject prefab = _unitData.GetUnitPrefab(_spawnUnitID);
        Transform parent = GameObject.Find("PlayerUnit")?.transform;
        Canvas prefabCanvas = prefab != null ? prefab.GetComponentInChildren<Canvas>() : null;
        bool hasUnitUi = prefabCanvas != null &&
                         prefabCanvas.transform.childCount >= 2 &&
                         prefabCanvas.transform.GetChild(1).childCount >= 3 &&
                         prefabCanvas.transform.GetChild(1).GetComponent<Slider>() != null;
        if (prefab == null || parent == null || !hasUnitUi) return false;

        GameObject g = Object.Instantiate(prefab);
        g.transform.SetParent(parent, false);
        g.transform.position = position;
        g.tag = "PlayerUnit";

        g.AddComponent<UnitMovementController>();
        _container.InjectGameObject(g);

        CharacterData characterData = new CharacterData(
            _spawnUnitID,
            g,
            g.GetComponent<UnitMovementController>(),
            _unitData.GetUnitData(_spawnUnitID));

        g.GetComponent<UnitMovementController>().characterData = characterData;
        g.GetComponent<UnitMovementController>().PlayerIndex = 0;

        CharacterData.InfoPanelData infoPanelData = new CharacterData.InfoPanelData();
        infoPanelData.sprite = _unitData.GetCard(characterData.UnitID);
        infoPanelData.name = characterData.unitData.unitName;
        infoPanelData.skillIcon = _unitData.GetSkillIcon(characterData.UnitID);
        infoPanelData.InfoDatas = new List<KeyValuePair<KeyValuePair<Sprite, string>, float>>();

        KeyValuePair<Sprite, string> Movement = new KeyValuePair<Sprite, string>(_uiConfig.GetMovementPointsIcon(), "剩余移动力");
        KeyValuePair<Sprite, string> MeleeAttack = new KeyValuePair<Sprite, string>(_uiConfig.GetMeleeAttackPointsIcon(), "攻击力");

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

        SpawnUIWiring.WireUnitCanvas(g, characterData, Color.green, _container, _uiConfig);

        Vector3 hexCoord = _mapDataService.WorldToHexCoordinate(g.transform.position);
        HexCellData h = _mapDataService.GetCell(hexCoord);
        _unitRepository.AddPlayerUnit(g, characterData);
        h.SetHaveUnit(true, g);

        var brain = g.AddComponent<PlayerUnitBrain>();
        brain.Initialize(
            characterData,
            UnitStrategyFactory.Create(_spawnUnitID),
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
