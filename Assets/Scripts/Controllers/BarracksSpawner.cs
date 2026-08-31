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

    // 【Excel 数值化】兵营生产间隔与兜底单位迁移至 CoreGameplayConfigProvider（旧 Inspector 字段已删除）。
    private float _spawnInterval => CoreGameplayConfigProvider.BarracksSpawnInterval;
    private int _spawnUnitID => CoreGameplayConfigProvider.BarracksFallbackUnitLegacyId;
    private UnitConfigSO _producedUnit;   // 由建筑配置注入（BuildingConfigSO.producedUnit），优先于 _spawnUnitID
    private float _timer;
    private bool _isPlayer;
    // 生产进度改由 ProductionProgressImages 帧动画驱动（挂载在兵营预制体上），替代旧 Slider 倒计时条
    private ProductionProgressImages _progressImages;

    /// <summary>由建筑生成器显式注入产出单位配置（动态 AddComponent 后调用，不能依赖 Inspector）。</summary>
    public void Initialize(UnitConfigSO producedUnit) => _producedUnit = producedUnit;

    private void Awake()
    {
        _isPlayer = gameObject.CompareTag("PlayerBuilding");
        _timer = 0f;
    }

    private void Start()
    {
        // 兵营预制体上挂载 ProductionProgressImages，此处仅查找并初始化为第一帧
        _progressImages = GetComponentInChildren<ProductionProgressImages>(true);
        _progressImages?.SetProgress(0f);
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
            // 缩放时间：x2/x3 时生产节奏同步加速（_gameLoop 已在方法开头判空）
            _timer = Mathf.Min(_spawnInterval, _timer + _gameLoop.ScaledDeltaTime);
            UpdateProgressDisplay();
        }

        if (_timer < _spawnInterval) return;
        if (!TrySpawnUnit()) return;

        _timer = 0f;
        UpdateProgressDisplay();
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
            // 【多单位落点】出生格选择改按有效容量：仍有自由站位槽即可。
            if (!neighbor.HasFreeStandingSlot()) continue;
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
        // 【多单位落点】按站位槽生成（满员退回旧单单位写入兜底）。
        if (h.TryClaimStandingUnit(g, position, position, preferLine: false, out _, out Vector3 slotPos))
            g.transform.position = slotPos;
        else
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

    private void UpdateProgressDisplay()
    {
        if (_progressImages == null) return;
        float progress = _spawnInterval > 0f ? _timer / _spawnInterval : 1f;
        _progressImages.SetProgress(progress);
    }
}
