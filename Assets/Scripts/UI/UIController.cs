using DG.Tweening;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Zenject;

public class UIController : MonoBehaviour
{
    // 注入服务
    [Inject] private IEnvironmentModelsProvider environmentModelsProvider;
    [Inject] private IUnitDataProvider unitDataProvider;
    [Inject] private IBuildingDataProvider buildingDataProvider;
    [Inject] private IUIConfigProvider uiConfigProvider;
    [Inject] private IMapDataService _mapDataService;
    [Inject] private DiContainer _container;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private IGameStateMachine _gameStateMachine;
    [Inject] private UnitMovementSystem _movementSystem;
    [Inject] private IInputService _input;
    [Inject] private PlayerInputHandler _playerInputHandler;
    [Inject] private UIManagerPresenter _uiPresenter;
    [Inject] private IUnitRepository _unitRepository;
    [Inject] private UnitRemovalService _unitRemovalService;
    [Inject] private PlayerModelManager _playerModelManager;
    [Inject] private EndGame _endGame;
    [Inject] private AudioManager _audioManager;
    [Inject] private Tech_CultureTreeController _tech_CultureTreeController;

    // 摄像机
    private Camera Camera;

    // UI类型
    public string UIType;

    // 单位信息面板原始位置
    [HideInInspector]
    private Vector2 InfoPanelOriginalRectPosition;

    // 按钮
    [HideInInspector]
    public Button nextTurnButton = null;

    // 本实例若为“下一回合”按钮，缓存其 Button 组件用于启用/禁用
    private Button _nextTurnButtonComponent = null;

    // 远程攻击状态
    private bool _isRangedAttackMode = false;
    private GameObject _rangedAttacker = null;

    // 临时高亮列表（用于远程攻击范围）
    private List<HexCellData> _tempHighlightedCells = new List<HexCellData>();
    private Dictionary<HexCellData, GridVisualState> _tempGridStates = new Dictionary<HexCellData, GridVisualState>();
    // 临时敌方指示器列表
    private List<GameObject> _tempEnemyIndicators = new List<GameObject>();
    private bool _isInfoPanelVisible;

    private struct GridVisualState
    {
        public bool WasActive;
        public Color Color;
        public bool HasColor;
    }

    void Start()
    {
        // 自动获取主摄像机（若未手动指定）
        if (Camera == null) { Camera = Camera.main; }

        // 下一回合按钮
        if (UIType == "nextTurnButton")// && nextTurnButton != null)
        {
            //Debug.Log("挂载了【下一回合按钮】");
            _nextTurnButtonComponent = transform.GetComponent<Button>();
            UITool.AddButtonClickEvent(_nextTurnButtonComponent, NextTurn);

            // 订阅阶段变化：仅玩家阶段可用，AI/结算阶段禁用
            if (_gameStateMachine != null)
                _gameStateMachine.PhaseChanged += RefreshNextTurnButtonInteractable;
            RefreshNextTurnButtonInteractable();
        }

        // 单位信息面板 - 技能按钮
        if (UIType == "SkillButton")
        {
            UITool.AddButtonClickEvent(transform.GetComponent<Button>(), UnitInfoPanelSkillButton);
        }

        // 单位信息面板 - 跳过按钮
        if (UIType == "SkipButton")
        {
            UITool.AddButtonClickEvent(transform.GetComponent<Button>(), UnitInfoPanelSkipButton);
        }

        // 单位信息面板 - 收割资源按钮
        if (UIType == "ReapButton")
        {
            UITool.AddButtonClickEvent(transform.GetComponent<Button>(), UnitInfoPanelReapButton);
        }

        // 单位模型图标
        if (UIType == "unitIcon")
        {
            GameObject unitObj = gameObject.transform.parent.transform.parent.gameObject;
            int id = -1;

            // 尝试从玩家单位获取
            if (_unitRepository.TryGetPlayerUnit(unitObj, out var playerData))
            {
                id = playerData.unitData.id;
            }
            else
            {
                // 尝试从敌方单位获取
                if (_unitRepository.TryGetEnemyUnit(unitObj, out var enemyData))
                {
                    id = enemyData.unitData.id;
                }
            }

            if (id >= 0)
                GetComponent<Image>().sprite = unitDataProvider.GetUnitIcon(id);
        }

        // 单位模型血条
        if (UIType == "healthBar")
        {
            Slider slider = gameObject.GetComponent<Slider>();
            slider.value = 1;
        }

        // 单位信息面板（仅记录原始位置，不填充数据）
        if (UIType == "UnitInfoPanel")
        {
            // 获取原始位置
            InfoPanelOriginalRectPosition = transform.GetComponent<RectTransform>().localPosition;
            _isInfoPanelVisible = _uiPresenter.CurrentSelectedUnit != null;
        }

        // 结束界面按钮 - 重播影片
        if (UIType == "ReplayVideo")
        {
            UITool.AddButtonClickEvent(transform.GetComponent<Button>(), ReplayVideo);
        }

        // 结束界面按钮 - 返回主菜单
        if (UIType == "MainMenu")
        {
            UITool.AddButtonClickEvent(transform.GetComponent<Button>(), MainMenu);
        }

        // 结束界面按钮 - 再战一回合
        if (UIType == "ContinueGame")
        {
            UITool.AddButtonClickEvent(transform.GetComponent<Button>(), ContinueGame);
        }
    }

    void Update()
    {
        // 让Canvas朝向摄像机
        if (UIType == "unitCanvas" || UIType == "buildingCanvas")
        {
            if (Camera == null) return;
            transform.LookAt(transform.position + Camera.transform.forward, Camera.transform.up);
        }

        // 信息面板（仅保留弹出动画，数据填充由 Presenter 在选中时一次性完成）
        if (UIType == "UnitInfoPanel")
        {
            bool shouldBeVisible = _uiPresenter.CurrentSelectedUnit != null;
            if (shouldBeVisible != _isInfoPanelVisible)
            {
                _isInfoPanelVisible = shouldBeVisible;
                RectTransform panel = transform.GetComponent<RectTransform>();
                panel.DOKill();
                Vector2 target = InfoPanelOriginalRectPosition + (shouldBeVisible ? new Vector2(0, 315) : Vector2.zero);
                panel.DOAnchorPos(target, 0.5f).SetLink(gameObject);
            }
        }

        // 远程攻击模式处理
        if (_isRangedAttackMode)
        {
            var controller = _rangedAttacker != null ? _rangedAttacker.GetComponent<UnitMovementController>() : null;
            if (_gameStateMachine.CurrentPhase is not PlayerPhase ||
                _uiPresenter.CurrentSelectedUnit != _rangedAttacker ||
                controller == null || !controller.CanBeSelected)
            {
                CancelRangedAttackMode();
                return;
            }

            // 检测右键点击选择目标
            if (_input.GetMouseButtonDown(1) && !_input.IsPointerOverUI())
            {
                GameObject target = GetEnemyUnderMouse();
                if (target == null)
                {
                    CancelRangedAttackMode();
                    _playerInputHandler.ForceDeselectUnit();
                }
            }
        }
    }

    // ---------- 下一回合 ----------
    public void NextTurn()
    {
        Debug.Log("点击了【下一回合按钮】");
        _audioManager.PlaySFX("Trumpet-009");
        _gameStateMachine?.EndTurn();
    }

    // 仅在玩家阶段允许点击“下一回合”，AI/结算阶段禁用
    private void RefreshNextTurnButtonInteractable()
    {
        if (_nextTurnButtonComponent == null) return;
        _nextTurnButtonComponent.interactable = _gameStateMachine?.CurrentPhase is PlayerPhase;
    }

    private void OnDestroy()
    {
        if (UIType == "nextTurnButton" && _gameStateMachine != null)
            _gameStateMachine.PhaseChanged -= RefreshNextTurnButtonInteractable;
    }

    // ---------- 单位信息面板按钮 ----------
    public void UnitInfoPanelSkillButton()
    {
        if (_gameStateMachine.CurrentPhase is not PlayerPhase) return;

        GameObject selectedUnit = _uiPresenter.CurrentSelectedUnit;
        if (selectedUnit == null) return;

        if (!_unitRepository.TryGetPlayerUnit(selectedUnit, out var characterData))
        {
            Debug.LogWarning("选中的单位不在玩家单位仓库中");
            return;
        }

        if (characterData.UnitID == 0)
        {
            CityBuilderSkill();
        }
        else if (characterData.UnitID == 3 || characterData.UnitID == 5 || characterData.UnitID == 9)
        {
            Debug.Log("点击了远程攻击按钮");
            EnterRangedAttackMode(characterData.model);
        }
        else
        {
            Debug.Log("点击了近战技能按钮");
        }
    }

    public void UnitInfoPanelSkipButton()
    {
        Debug.Log("点击了跳过按钮");
    }

    // ---------- 移民建城技能 ----------
    private void CityBuilderSkill()
    {
        GameObject unit = _uiPresenter.CurrentSelectedUnit;
        if (unit == null) return;

        HexCellData targetCell = _mapDataService.GetCellByWorldPosition(unit.transform.position);
        if (!IsValidPlayerCityCell(targetCell, unit))
        {
            Debug.LogWarning("[UIController] 当前位置无法建城：地块不合法或属于敌方势力。");
            return;
        }

        // 生成城市模型
        GameObject city = Instantiate(buildingDataProvider.GetCityModel());
        city.transform.SetParent(GameObject.Find("PlayerBuilding").transform, false);
        city.transform.position = unit.transform.position;
        city.tag = "PlayerBuilding";

        BuildingController buildingController = city.AddComponent<BuildingController>();
        _container.Inject(buildingController);
        BuildingData buildingData = new BuildingData(Enums.BulidingType.City, buildingDataProvider);
        buildingController.buildingData = buildingData;
        buildingData.controller = buildingController;

        HexCellData hB = _mapDataService.GetCellByWorldPosition(city.transform.position);
        buildingController.bulidingType = Enums.BulidingType.City;
        hB.BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>(Enums.BulidingType.City, city);
        int cityIndex = _playerModelManager.AllocateCityIndex();
        buildingController.Player_City_Index = new KeyValuePair<int, int>(0, cityIndex);

        // UI画布
        Canvas canvas = city.GetComponentInChildren<Canvas>();
        UIController buildingCanvas = _container.InstantiateComponent<UIController>(canvas.gameObject);
        _container.Inject(buildingCanvas);
        buildingCanvas.UIType = "buildingCanvas";
        uiConfigProvider.AddRuntimeCanvas(canvas);

        GameObject healthBar = canvas.transform.GetChild(0).gameObject;
        UIController healthBarUI = _container.InstantiateComponent<UIController>(healthBar.gameObject);
        _container.Inject(healthBarUI);
        healthBarUI.UIType = "buildingHealthBar";
        buildingController.uiHealthBar = healthBar.GetComponent<Slider>();

        if (city.CompareTag("PlayerBuilding"))
            UITool.TrySetSliderFillColor(buildingController.uiHealthBar, Color.green);
        else if (city.CompareTag("EnemyBuilding"))
            UITool.TrySetSliderFillColor(buildingController.uiHealthBar, Color.red);

        _unitRemovalService.RemoveUnit(unit);

        // 添加势力范围
        _playerModelManager.SingleCity_SphereOfInfluence_HexC_HexCellData.Add(cityIndex, new Dictionary<Vector3, HexCellData>());

        _playerModelManager.ExpandTheSphereOfInfluence(
            _mapDataService.WorldToHexCoordinate(city.transform.position),
            _playerModelManager.SphereOfInfluence_HexC_HexCellData,
            new KeyValuePair<int, int>(0, cityIndex)
        );

        _playerModelManager.ExpandTheSphereOfInfluence(
            _mapDataService.WorldToHexCoordinate(city.transform.position),
            _playerModelManager.SingleCity_SphereOfInfluence_HexC_HexCellData[cityIndex],
            new KeyValuePair<int, int>(0, cityIndex)
        );

        _mapVisualEvent.Raise();
        _playerModelManager.CityCount++;

        // 添加视野
        HexCellData h = _mapDataService.GetCellByWorldPosition(city.transform.position);
        for (int i = 0; i < 6; i++)
        {
            var neighbor = _mapDataService.GetNeighbor(h, (Enums.HexDirection)i);
            if (neighbor != null)
                neighbor.ExploreThisHexCell();
        }
        _mapVisualEvent.Raise();

        _audioManager.PlaySFX("Drum_Rolls-006");
    }

    private bool IsValidPlayerCityCell(HexCellData cell, GameObject settlerObj)
    {
        if (cell == null) return false;
        if (cell.HexType == Enums.HexType.LakeOrSea) return false;
        if (cell.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding) return false;
        if (cell.Player_City_Index.Key != -1 && cell.Player_City_Index.Key != 0) return false;
        if (!cell.IsHaveUnit()) return true;

        GameObject occupiedUnit = cell.GetUnit();
        return occupiedUnit == settlerObj;
    }

    // ---------- 进入远程攻击模式 ----------
    private void EnterRangedAttackMode(GameObject attacker)
    {
        var controller = attacker != null ? attacker.GetComponent<UnitMovementController>() : null;
        if (_gameStateMachine.CurrentPhase is not PlayerPhase || controller == null || !controller.CanBeSelected) return;

        int attackRange = controller.characterData?.unitData?.BasicAttackRange ?? 0;
        if (attackRange <= 1) return;

        Vector3 startHex = _mapDataService.WorldToHexCoordinate(attacker.transform.position);
        List<Vector3> reachableHexes = _mapDataService.GetAllCells()
            .Where(cell => cell != null && HexDistance(startHex, cell.HexCoordinate) > 0f && HexDistance(startHex, cell.HexCoordinate) <= attackRange)
            .Select(cell => cell.HexCoordinate)
            .ToList();

        ShowReachableHexes(reachableHexes, mode: 1);
        ShowEnemyIndicators();

        _isRangedAttackMode = true;
        _rangedAttacker = attacker;
    }

    // ---------- 取消远程攻击模式 ----------
    private void CancelRangedAttackMode()
    {
        _isRangedAttackMode = false;
        _rangedAttacker = null;

        ClearReachableHexes();
        ClearEnemyIndicators();
    }

    private static float HexDistance(Vector3 a, Vector3 b)
    {
        return (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z)) * 0.5f;
    }

    // ---------- 获取鼠标下的敌人 ----------
    private GameObject GetEnemyUnderMouse()
    {
        RaycastHit[] hits = _input.RaycastAllFromScreen(_input.MousePosition, 100f, Physics.DefaultRaycastLayers);
        foreach (RaycastHit hit in hits)
        {
            Transform current = hit.transform;
            while (current != null)
            {
                if (current.CompareTag("EnemyUnit") || current.CompareTag("EnemyBuilding"))
                    return current.gameObject;
                current = current.parent;
            }
        }
        return null;
    }

    // ---------- 高亮指定六边形列表 ----------
    private void ShowReachableHexes(List<Vector3> hexCoords, int mode = 0)
    {
        ClearReachableHexes();

        foreach (Vector3 coord in hexCoords)
        {
            var cell = _mapDataService.GetCell(coord);
            if (cell != null && cell.GridMesh != null)
            {
                var renderer = cell.GridMesh.GetComponent<Renderer>();
                _tempGridStates[cell] = new GridVisualState
                {
                    WasActive = cell.GridMesh.activeSelf,
                    Color = renderer != null ? renderer.material.color : default,
                    HasColor = renderer != null
                };
                cell.GridMesh.SetActive(true);
                if (mode == 1)
                {
                    if (renderer != null)
                        renderer.material.color = Color.cyan;
                }
                _tempHighlightedCells.Add(cell);
            }
        }
    }

    private void ClearReachableHexes()
    {
        foreach (var cell in _tempHighlightedCells)
        {
            if (cell.GridMesh == null || !_tempGridStates.TryGetValue(cell, out GridVisualState state)) continue;

            if (state.HasColor)
            {
                var renderer = cell.GridMesh.GetComponent<Renderer>();
                if (renderer != null) renderer.material.color = state.Color;
            }
            cell.GridMesh.SetActive(state.WasActive);
        }
        _tempHighlightedCells.Clear();
        _tempGridStates.Clear();
    }

    private void OnDisable()
    {
        if (UIType == "UnitInfoPanel") transform.GetComponent<RectTransform>().DOKill();
        CancelRangedAttackMode();
    }

    // ---------- 显示敌方单位指示器 ----------
    private void ShowEnemyIndicators()
    {
        ClearEnemyIndicators();

        Transform indicatorParent = GameObject.Find("UIModel")?.transform.Find("EnemyUnit_Indicator");
        if (indicatorParent == null)
        {
            GameObject parent = new GameObject("EnemyUnit_Indicator");
            parent.transform.SetParent(GameObject.Find("UIModel")?.transform);
            indicatorParent = parent.transform;
        }

        GameObject prefab = uiConfigProvider.GetEnemyUnitIndicatorPrefab();
        foreach (var group in _unitRepository.AllEnemyUnitGroups)
        {
            foreach (var kv in group)
            {
                GameObject enemy = kv.Key;
                if (enemy == null) continue;

                // 迷雾过滤：只给玩家当前视野内(IsVisible)的敌方单位生成红圈。
                // 否则记忆区/未探索地块上的敌人也会被标出，等于泄露迷雾外的敌人位置。
                HexCellData cell = _mapDataService.GetCellByWorldPosition(enemy.transform.position);
                if (cell == null || !cell.IsVisible) continue;

                var indicator = Instantiate(prefab, indicatorParent);
                indicator.transform.position = enemy.transform.position + Vector3.up * 0.2f;
                _tempEnemyIndicators.Add(indicator);
            }
        }
    }

    private void ClearEnemyIndicators()
    {
        foreach (var ind in _tempEnemyIndicators)
        {
            if (ind != null) Destroy(ind);
        }
        _tempEnemyIndicators.Clear();
    }

    // ---------- 收割资源 ----------
    public void UnitInfoPanelReapButton()
    {
        if (_gameStateMachine.CurrentPhase is not PlayerPhase) return;

        GameObject selectedUnit = _uiPresenter.CurrentSelectedUnit;
        if (selectedUnit == null) return;

        if (!_unitRepository.TryGetPlayerUnit(selectedUnit, out var characterData))
        {
            Debug.LogWarning("选中的单位不在玩家单位仓库中");
            return;
        }

        HexCellData h = _mapDataService.GetCellByWorldPosition(characterData.model.transform.position);

        Enums.ResourceType resource = h.GetResource();
        if (resource == Enums.ResourceType.None)
        {
            Debug.Log("该地块无资源");
            return;
        }

        h.ReapResource();
        Destroy(h.resourceModel);
        h.resourceModel = null;

        switch (resource)
        {
            case Enums.ResourceType.Animals:
                _audioManager.PlaySFX("Cymbals-008");
                ReapResource_Animals(characterData);
                break;
            case Enums.ResourceType.Plants:
                _audioManager.PlaySFX("heal5");
                ReapResource_Plants(characterData);
                break;
            case Enums.ResourceType.Minerals:
                _audioManager.PlaySFX("Metallic_Weapon_Hit-020");
                ReapResource_Minerals(characterData);
                break;
            case Enums.ResourceType.Chest:
                _audioManager.PlaySFX("Coin8");
                ReapResource_Chest(characterData);
                break;
        }
    }

    private void ReapResource_Animals(CharacterData Reaper)
    {
        Reaper.Resource_Animals = 0.7f;
        ReapResource(environmentModelsProvider.GetReapAnimalsEffect());
    }

    private void ReapResource_Plants(CharacterData Reaper)
    {
        Reaper.Heal(0.25f * Reaper.unitData.hp);
        ReapResource(environmentModelsProvider.GetReapPlantsEffect());
    }

    private void ReapResource_Minerals(CharacterData Reaper)
    {
        Reaper.Resource_Minerals = 0.25f;
        ReapResource(environmentModelsProvider.GetReapMineralsEffect());
    }

    private void ReapResource_Chest(CharacterData Reaper)
    {
        _tech_CultureTreeController.AddTechPoints(30);
        _tech_CultureTreeController.AddCulturePoints(30);
        ReapResource(environmentModelsProvider.GetReapChestEffect());
    }

    private void ReapResource(GameObject resourcePrefab)
    {
        GameObject selectedUnit = _uiPresenter.CurrentSelectedUnit;
        if (selectedUnit == null) return;

        if (!_unitRepository.TryGetPlayerUnit(selectedUnit, out var characterData))
        {
            Debug.LogWarning("选中的单位不在玩家单位仓库中");
            return;
        }

        Vector3 v = _mapDataService.GetCellByWorldPosition(characterData.model.transform.position).RealCenterWorldCoordinate;
        GameObject g = Instantiate(resourcePrefab);
        g.transform.position = v + new Vector3(0, 0.5f, 0);
        StartCoroutine(DestroyAfterDelay(4.0f, g));
    }

    IEnumerator DestroyAfterDelay(float delay, GameObject obj)
    {
        yield return new WaitForSeconds(delay);
        Destroy(obj);
    }

    // ---------- 结束界面按钮 ----------
    private void ReplayVideo()
    {
        var animation = _endGame.CurrentEndAnimation;
        if (animation == null) return;

        animation.gameObject.SetActive(true);
        animation.SetAsLastSibling();
        Invoke("TurnOffTheVideo", 6.5f);
    }

    private void TurnOffTheVideo()
    {
        var animation = _endGame.CurrentEndAnimation;
        if (animation != null)
        {
            animation.gameObject.SetActive(false);
        }
    }

    private void MainMenu()
    {
        SceneManager.LoadScene(0);
        _audioManager.PlayBGM("Place_Village_Loop");
    }

    private void ContinueGame()
    {
        _audioManager.PlayBGM("Theme_Mistery_But_Then_Happy_Loop");
        _endGame.HideEndUI();
    }
}
