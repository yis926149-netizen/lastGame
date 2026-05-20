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

    // 远程攻击状态
    private bool _isRangedAttackMode = false;
    private GameObject _rangedAttacker = null;

    // 临时高亮列表（用于远程攻击范围）
    private List<HexCellData> _tempHighlightedCells = new List<HexCellData>();
    // 临时敌方指示器列表
    private List<GameObject> _tempEnemyIndicators = new List<GameObject>();

    void Start()
    {
        // 自动获取主摄像机（若未手动指定）
        if (Camera == null) { Camera = Camera.main; }

        // 下一回合按钮
        if (UIType == "nextTurnButton")// && nextTurnButton != null)
        {
            //Debug.Log("挂载了【下一回合按钮】");
            UITool.AddButtonClickEvent(transform.GetComponent<Button>(), NextTurn);
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
            if (_uiPresenter.CurrentSelectedUnit != null)
            {
                transform.GetComponent<RectTransform>().DOAnchorPos(InfoPanelOriginalRectPosition + new Vector2(0, 315), 0.5f);
            }
            else
            {
                transform.GetComponent<RectTransform>().DOAnchorPos(InfoPanelOriginalRectPosition, 0.5f);
            }
        }

        // 远程攻击模式处理
        if (_isRangedAttackMode && _rangedAttacker != null)
        {
            // 检测右键点击选择目标
            if (_input.GetMouseButtonDown(1) && !_input.IsPointerOverUI())
            {
                GameObject target = GetEnemyUnderMouse();
                if (target != null)
                {
                    string targetTag = target.tag;
                    PerformRangedAttack(_rangedAttacker, target, targetTag);
                }
                else
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

    // ---------- 单位信息面板按钮 ----------
    public void UnitInfoPanelSkillButton()
    {
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
        buildingController.Player_City_Index = new KeyValuePair<int, int>(0, _playerModelManager.CityCount);

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
            healthBar.transform.GetChild(2).GetComponent<Image>().color = Color.green;
        else if (city.CompareTag("EnemyBuilding"))
            healthBar.transform.GetChild(2).GetComponent<Image>().color = Color.red;

        // 销毁移民单位
        Destroy(unit);
        // 取消选中（如果当前选中的是这个单位）
        if (_uiPresenter.CurrentSelectedUnit == unit)
        {
            _playerInputHandler.ForceDeselectUnit();
        }

        // 添加势力范围
        _playerModelManager.SingleCity_SphereOfInfluence_HexC_HexCellData.Add(_playerModelManager.CityCount, new Dictionary<Vector3, HexCellData>());

        _playerModelManager.ExpandTheSphereOfInfluence(
            _mapDataService.WorldToHexCoordinate(city.transform.position),
            _playerModelManager.SphereOfInfluence_HexC_HexCellData,
            new KeyValuePair<int, int>(0, _playerModelManager.CityCount)
        );

        _playerModelManager.ExpandTheSphereOfInfluence(
            _mapDataService.WorldToHexCoordinate(city.transform.position),
            _playerModelManager.SingleCity_SphereOfInfluence_HexC_HexCellData[_playerModelManager.CityCount],
            new KeyValuePair<int, int>(0, _playerModelManager.CityCount)
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
        List<Vector3> reachableHexes = _movementSystem.GetAllReachableHexesFromStartHex(
            new List<Vector3>(_mapDataService.GetAllHexCoordinates()),
            _mapDataService.WorldToHexCoordinate(attacker.transform.position),
            2
        );

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

    // ---------- 执行远程攻击 ----------
    private void PerformRangedAttack(GameObject attacker, GameObject target, string targetTag)
    {
        var controller = attacker.GetComponent<UnitMovementController>();
        if (controller == null) return;

        controller.attackedUnit = target;
        controller.attackTarget = targetTag;
        controller.isAttack = true;
        controller.isRangedAttack = true;

        CancelRangedAttackMode();
        _playerInputHandler.ForceDeselectUnit();
    }

    // ---------- 获取鼠标下的敌人 ----------
    private GameObject GetEnemyUnderMouse()
    {
        if (_input.RaycastFromScreen(_input.MousePosition, out RaycastHit hit, 100f, LayerMask.GetMask("Units", "Buildings")))
        {
            GameObject obj = hit.transform.gameObject;
            if (obj.CompareTag("EnemyUnit") || obj.CompareTag("EnemyBuilding"))
                return obj;
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
                cell.GridMesh.SetActive(true);
                if (mode == 1)
                {
                    var renderer = cell.GridMesh.GetComponent<Renderer>();
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
            if (cell.GridMesh != null)
                cell.GridMesh.SetActive(false);
        }
        _tempHighlightedCells.Clear();
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
        Reaper.currentHp += 0.25f * Reaper.unitData.hp;
        Reaper.healthBar.value += 0.25f;
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
        _endGame.EndAnimation.gameObject.SetActive(true);
        _endGame.EndAnimation.SetAsLastSibling();
        Invoke("TurnOffTheVideo", 6.5f);
    }

    private void TurnOffTheVideo()
    {
        _endGame.EndAnimation.gameObject.SetActive(false);
    }

    private void MainMenu()
    {
        SceneManager.LoadScene(0);
        _audioManager.PlayBGM("Place_Village_Loop");
    }

    private void ContinueGame()
    {
        _audioManager.PlayBGM("Theme_Mistery_But_Then_Happy_Loop");
        _endGame.EndAnimation.gameObject.SetActive(false);
        _endGame.EndUI.gameObject.SetActive(false);
    }
}