using DG.Tweening;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Zenject;
using UIToolkitDemo;

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
    [Inject] private UIManagerPresenter _uiPresenter;
    [Inject] private IUnitRepository _unitRepository;
    [Inject] private UnitRemovalService _unitRemovalService;
    [Inject] private PlayerModelManager _playerModelManager;
    [Inject] private EndGame _endGame;
    [Inject] private AudioManager _audioManager;
    [Inject] private GoldWallet _goldWallet; // 【探索重构-阶段7】
    [Inject] private GoldIncomeService _goldIncomeService;
    [Inject] private ExplorationCoinFlyPresenter _coinFlyPresenter; // 金币飞入表现（方案 B：显示层延迟）
    [Inject] private IFactionBuffService _factionBuff; // 天赋 Buff 服务
    [Inject] private HexHighlightRenderer _hexHighlightRenderer;

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

    // 本实例若为速度按钮，缓存其 Button 组件用于启用/禁用
    private Button _speedButtonComponent = null;

    // 【速度按钮】状态图标，可在 Inspector 中配置
    [Tooltip("x1 速度时按钮显示的精灵")]
    [SerializeField] private Sprite _x1Sprite;
    [Tooltip("x2 速度时按钮显示的精灵")]
    [SerializeField] private Sprite _x2Sprite;
    [Tooltip("x3 速度时按钮显示的精灵")]
    [SerializeField] private Sprite _x3Sprite;
    [Tooltip("暂停时按钮显示的精灵")]
    [SerializeField] private Sprite _pauseSprite;

    // 速度按钮的 Image 组件与当前速度档缓存
    private Image _speedButtonImage = null;
    private GameLoop.GameSpeed _lastSpeed = GameLoop.GameSpeed.x1;

    // 【探索奖励预生成】奖励类型图标精灵数组：索引与 ExplorationRewardConfigSO.ExplorationRewardType 枚举值一致
    // （0=无奖励 / 1=金币 / 2=军事单位 / 3=战术卡牌 / 4=建筑）。显示在本物体第二个子物体（Type）的 Image 上。
    [Tooltip("奖励类型图标精灵数组，索引与 ExplorationRewardType 枚举值一致（0=无/1=金币/2=军事/3=战术/4=建筑）")]
    public Sprite[] rewardTypeIcons;

    // 远程攻击状态
    private bool _isRangedAttackMode = false;
    private GameObject _rangedAttacker = null;

    // 临时高亮列表（用于远程攻击范围）
    // 临时敌方指示器列表
    private List<GameObject> _tempEnemyIndicators = new List<GameObject>();
    private bool _isInfoPanelVisible;

    // 【P3 对象池】敌方单位指示器池：替代每次刷新时的 Instantiate/Destroy。
    private readonly PrefabPool _enemyIndicatorPool = new PrefabPool();
    private GameObject _enemyIndicatorPrefab;


    void Start()
    {
        // 自动获取主摄像机（若未手动指定）
        if (Camera == null) { Camera = Camera.main; }

        // 速度按钮
        if (UIType == "nextTurnButton")// && nextTurnButton != null)
        {
            //Debug.Log("挂载了【下一回合按钮】");
            _speedButtonComponent = transform.GetComponent<Button>();
            _speedButtonImage = transform.GetComponent<Image>();
            UITool.AddButtonClickEvent(_speedButtonComponent, CycleSpeed);

            // 【检查点 6】回合制已停用，按钮始终可点击，无阶段变化订阅
            RefreshSpeedButtonInteractable();
            _lastSpeed = _gameLoop != null ? _gameLoop.CurrentSpeed : GameLoop.GameSpeed.x1;
            RefreshSpeedButtonSprite();
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
            UnitMovementController movement = GetComponentInParent<UnitMovementController>();
            int id = movement != null && movement.characterData?.unitData != null
                ? movement.characterData.unitData.id
                : -1;

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

        // 金币显示 HUD（方案 B：显示值 = 真实钱包 - 飞行中金币数，飞币落地逐枚追平）
        if (UIType == "GoldWallet")
        {
            var goldText = transform.GetChild(0)?.GetComponent<TMP_Text>();
            if (goldText != null)
            {
                RefreshGoldText(goldText);
                _goldWallet.OnGoldChanged += (_) => RefreshGoldText(goldText);
                if (_coinFlyPresenter != null)
                {
                    _coinFlyPresenter.InFlightChanged += () => RefreshGoldText(goldText);
                }
            }

            var incomeText = transform.GetChild(2)?.GetComponent<TMP_Text>();
            if (incomeText != null)
            {
                RefreshIncomeText(incomeText);
                // 金矿占领会改变真实被动收入；金币每秒结算后同步刷新显示。
                _goldWallet.OnGoldChanged += (_) => RefreshIncomeText(incomeText);
                if (_factionBuff != null)
                    _factionBuff.OnBuffsChanged += () => RefreshIncomeText(incomeText);
            }
        }
    }

    void Update()
    {
        // 按钮图标随速度档位切换（EndGame/天赋卡牌选择等也会改速度，故每帧检测变化）
        if (UIType == "nextTurnButton" && _gameLoop != null && _gameLoop.CurrentSpeed != _lastSpeed)
        {
            _lastSpeed = _gameLoop.CurrentSpeed;
            RefreshSpeedButtonSprite();
        }

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
                Vector2 target = InfoPanelOriginalRectPosition + (shouldBeVisible ? new Vector2(0, UIScreenHelper.ReferenceHeight * 0.29f) : Vector2.zero); // B3: 信息面板滑入改为 Canvas 参考高度比例
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

            // 远程攻击由 RangedStrategy 自动索敌，不再监听桌面右键。
        }
    }

    // ---------- 探索奖励图标（CostLabel 地块标签）----------
    /// <summary>
    /// 根据地块预生成的奖励类型，把第二个子物体（Type）的 Image 切换为对应图标。
    /// 索引与 ExplorationRewardConfigSO.ExplorationRewardType 枚举值一致；无图标或类型无效时隐藏。
    /// </summary>
    public void SetRewardTypeIcon(ExplorationRewardConfigSO.ExplorationRewardType rewardType)
    {
        if (transform.childCount <= 1) return;
        Image iconImage = transform.GetChild(1).GetComponent<Image>();
        if (iconImage == null) return;

        int index = (int)rewardType;
        if (rewardTypeIcons == null || index < 0 || index >= rewardTypeIcons.Length || rewardTypeIcons[index] == null)
        {
            iconImage.gameObject.SetActive(false);
            return;
        }

        iconImage.gameObject.SetActive(true);
        iconImage.sprite = rewardTypeIcons[index];
    }

    // ---------- 速度切换（原“下一回合”按钮）----------
    // 【批次 C】实时化后此按钮改为速度循环：x1 → x2 → x3 → 暂停 → x1。
    public void CycleSpeed()
    {
        _audioManager.PlaySFX("Trumpet-009");
        if (_gameLoop == null) return;

        var next = _gameLoop.CurrentSpeed switch
        {
            GameLoop.GameSpeed.x1 => GameLoop.GameSpeed.x2,
            GameLoop.GameSpeed.x2 => GameLoop.GameSpeed.x3,
            GameLoop.GameSpeed.x3 => GameLoop.GameSpeed.Paused,
            _ => GameLoop.GameSpeed.x1
        };
        _gameLoop.SetSpeed(next);
        RefreshSpeedButtonSprite();
    }

    // 根据当前速度档位切换按钮精灵
    private void RefreshSpeedButtonSprite()
    {
        if (_speedButtonImage == null) return;
        var speed = _gameLoop != null ? _gameLoop.CurrentSpeed : GameLoop.GameSpeed.x1;
        _speedButtonImage.sprite = speed switch
        {
            GameLoop.GameSpeed.Paused => _pauseSprite,
            GameLoop.GameSpeed.x2 => _x2Sprite,
            GameLoop.GameSpeed.x3 => _x3Sprite,
            _ => _x1Sprite
        };
    }

    // 【批次 C】暂停/继续按钮始终可点击（无回合阶段限制）
    private void RefreshSpeedButtonInteractable()
    {
        if (_speedButtonComponent == null) return;
        _speedButtonComponent.interactable = true;
    }

    private void OnDestroy()
    {
        // 【检查点 6】回合制 PhaseChanged 事件已移除，无取消订阅需要
        _enemyIndicatorPool.Clear();
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

        // 单位类型判断：数值仅取 Excel（阶段6 唯一主源，Provider 内部处理）。
        UnitStrategyType strategyType = unitDataProvider.GetUnitStrategyType(characterData.UnitID);

        if (strategyType == UnitStrategyType.Settler)
        {
            CityBuilderSkill();
        }
        else if (strategyType == UnitStrategyType.Ranged)
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

        int baseRange = controller.characterData?.unitData?.BasicAttackRange ?? 0;
        if (baseRange <= 1) return;

        int attackRange = baseRange;
        // 【多单位计划九.10】按逻辑格取；同一坐标只算一次（原来 attackerHex / startHex 是两次同样的反查）
        Vector3 attackerHex = controller.CurrentHexCoordinate;
        HexCellData attackerCell = _mapDataService.GetCell(attackerHex);
        if (attackerCell != null && WaterLevelConfig.ClassifyHeight(attackerCell.Height) == 2)
            attackRange = baseRange + BattleFormulaRule.HighGroundRangeBonus;

        Vector3 startHex = attackerHex;
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

    // ---------- 高亮指定六边形列表 ----------
    private void ShowReachableHexes(List<Vector3> hexCoords, int mode = 0)
    {
        ClearReachableHexes();

        // 场景组件可能在 Zenject 注入前被 SetActive 触发 OnDisable，此时渲染器尚未注入。
        if (_hexHighlightRenderer == null) return;

        var cells = new List<HexCellData>();
        foreach (Vector3 coord in hexCoords)
        {
            var cell = _mapDataService.GetCell(coord);
            // 【程序化山脉-阶段6.5】攻击范围/可达集合过滤有效山格（决策 ⑨）：
            // 山格不可通行不可部署，无高亮目标；Renderer 门禁只作兜底。
            if (cell == null || MountainCellRule.IsEffectiveMountainCell(cell)) continue;
            cells.Add(cell);
        }
        Color highlightColor = mode == 1 ? Color.cyan : Color.yellow;
        _hexHighlightRenderer.SetHighlightedCells(HexHighlightChannel.Reachable, cells, highlightColor);
    }

    private void ClearReachableHexes()
    {
        if (_hexHighlightRenderer == null) return;
        _hexHighlightRenderer.ClearChannel(HexHighlightChannel.Reachable);
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
        _enemyIndicatorPrefab = prefab;
        foreach (var group in _unitRepository.AllEnemyUnitGroups)
        {
            foreach (var kv in group)
            {
                GameObject enemy = kv.Key;
                if (enemy == null) continue;

                // 【探索重构-阶段1】所有敌方单位始终可见，无需 IsVisible 过滤
                // 【多单位计划九.10】cell 在此仅作「在图内」的有效性判定，按逻辑格取更准也更快
                var enemyUmc = enemy.GetComponent<UnitMovementController>();
                HexCellData cell = enemyUmc != null
                    ? _mapDataService.GetCell(enemyUmc.CurrentHexCoordinate)
                    : _mapDataService.GetCellByWorldPosition(enemy.transform.position);
                if (cell == null) continue;

                var indicator = _enemyIndicatorPool.Get(prefab, indicatorParent);
                if (indicator == null) continue;
                indicator.transform.position = enemy.transform.position + Vector3.up * 0.2f;
                _tempEnemyIndicators.Add(indicator);
            }
        }
    }

    private void ClearEnemyIndicators()
    {
        foreach (var ind in _tempEnemyIndicators)
        {
            if (ind != null) _enemyIndicatorPool.Release(_enemyIndicatorPrefab, ind);
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

    /// <summary>金币 HUD 显示值 = 真实钱包 - 飞行中金币数（方案 B：显示层延迟，飞币落地逐枚追平）。</summary>
    private void RefreshGoldText(TMP_Text goldText)
    {
        if (goldText == null || _goldWallet == null) return;
        int inFlight = _coinFlyPresenter != null ? _coinFlyPresenter.InFlightGold : 0;
        int display = _goldWallet.Gold - inFlight;
        if (display < 0) display = 0;
        goldText.text = display.ToString();
    }

    private void RefreshIncomeText(TMP_Text incomeText)
    {
        if (incomeText == null) return;
        int income = _goldIncomeService != null
            ? _goldIncomeService.GetIncomePerTick(0)
            : _goldWallet.PassiveIncomePerTick;
        incomeText.text = income.ToString();
    }
}
