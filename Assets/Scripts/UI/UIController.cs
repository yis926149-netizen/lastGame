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
    [Inject] private IUnitDataProvider unitDataProvider;
    [Inject] private IBuildingDataProvider buildingDataProvider;
    [Inject] private IUIConfigProvider uiConfigProvider;
    [Inject] private IMapDataService _mapDataService;
    [Inject] private DiContainer _container;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private GameLoop _gameLoop;
    [Inject] private UnitMovementSystem _movementSystem;
    [Inject] private IInputService _input;
    [Inject] private PlayerInputHandler _playerInputHandler;
    [Inject] private UIManagerPresenter _uiPresenter;
    [Inject] private IUnitRepository _unitRepository;
    [Inject] private UnitRemovalService _unitRemovalService;
    [Inject] private PlayerModelManager _playerModelManager;
    [Inject] private EndGame _endGame;
    [Inject] private AudioManager _audioManager;
    [Inject] private GoldWallet _goldWallet; // 【探索重构-阶段7】
    [Inject] private IFactionBuffService _factionBuff; // 天赋 Buff 服务

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

            // 【检查点 6】回合制已停用，按钮始终可点击，无阶段变化订阅
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

        // 金币显示 HUD
        if (UIType == "GoldWallet")
        {
            var goldText = transform.GetChild(0)?.GetComponent<Text>();
            if (goldText != null)
            {
                goldText.text = _goldWallet.Gold.ToString();
                _goldWallet.OnGoldChanged += (g) => goldText.text = g.ToString();
            }

            var incomeText = transform.GetChild(2)?.GetComponent<Text>();
            if (incomeText != null)
            {
                RefreshIncomeText(incomeText);
                if (_factionBuff != null)
                    _factionBuff.OnBuffsChanged += () => RefreshIncomeText(incomeText);
            }
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
                Vector2 target = InfoPanelOriginalRectPosition + (shouldBeVisible ? new Vector2(0, Screen.height * 0.29f) : Vector2.zero); // B3: 信息面板滑入改为屏幕高度比例
                panel.DOAnchorPos(target, 0.5f).SetLink(gameObject);
            }
        }

        // 远程攻击模式处理
        if (_isRangedAttackMode)
        {
            var controller = _rangedAttacker != null ? _rangedAttacker.GetComponent<UnitMovementController>() : null;
            // 【批次 C】移除 CurrentPhase is not PlayerPhase 判断——实时化后无回合阶段
            if (_uiPresenter.CurrentSelectedUnit != _rangedAttacker ||
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

    // ---------- 暂停/继续（原“下一回合”按钮）----------
    // 【批次 C】实时化后此按钮改为暂停/继续切换，不再推进回合。
    public void NextTurn()
    {
        _audioManager.PlaySFX("Trumpet-009");
        _gameLoop?.SetPaused(!(_gameLoop?.IsPaused ?? false));
    }

    // 【批次 C】暂停/继续按钮始终可点击（无回合阶段限制）
    private void RefreshNextTurnButtonInteractable()
    {
        if (_nextTurnButtonComponent == null) return;
        _nextTurnButtonComponent.interactable = true;
    }

    private void OnDestroy()
    {
        // 【检查点 6】回合制 PhaseChanged 事件已移除，无取消订阅需要
    }

    // ---------- 单位信息面板按钮 ----------
    public void UnitInfoPanelSkillButton()
    {
        // 【批次 C】移除回合阶段门控，实时化后始终允许
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

    // ---------- 移民建城技能（已移除） ----------
    private void CityBuilderSkill()
    {
        // 【探索重构-阶段5.5】建新城功能移除。
        // 玩家只有一个主城（开局生成），势力范围通过探索扩张。
        Debug.LogWarning("[UIController] 建城功能已移除。");
    }

    private bool IsValidPlayerCityCell(HexCellData cell, GameObject settlerObj)
    {
        // 【探索重构-阶段5.5】建城检查已废弃，始终返回 false。
        return false;
    }

    // ---------- 进入远程攻击模式 ----------
    private void EnterRangedAttackMode(GameObject attacker)
    {
        var controller = attacker != null ? attacker.GetComponent<UnitMovementController>() : null;
        // 【批次 C】移除回合阶段门控
        if (controller == null || !controller.CanBeSelected) return;

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

                // 【探索重构-阶段1】所有敌方单位始终可见，无需 IsVisible 过滤
                HexCellData cell = _mapDataService.GetCellByWorldPosition(enemy.transform.position);
                if (cell == null) continue;

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

    private void RefreshIncomeText(Text incomeText)
    {
        if (incomeText == null) return;
        float multiplier = _factionBuff != null ? _factionBuff.GetStatMultiplier(0, "gold") : 1f;
        incomeText.text = Mathf.RoundToInt(_goldWallet.PassiveIncomePerTick * multiplier).ToString();
    }
}
