using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Zenject;
using DG.Tweening;

public class CardPresenter : IInitializable, IPlayerUnitSpawnService, IPlayerBuildingSpawnService, ICardDropHandler
{
    [Inject] private ICardService _cardService;
    [Inject] private IMapDataService _mapDataService;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private DiContainer _container;
    [Inject] private IUIConfigProvider _uiConfig;
    [Inject] private IUnitDataProvider _unitData;
    [Inject] private IBuildingDataProvider _buildingData;
    [Inject] private IUnitService _unitService;
    [Inject] private PlayerModelManager _playerModelManager;
    [Inject] private IUnitRepository _unitRepository;
    [Inject] private AudioManager _audioManager;
    [Inject] private UnitMovementSystem _movementSystem;
    [Inject(Optional = true)] private ILogisticsService _logisticsService;
    [Inject] private UnitRemovalService _unitRemovalService;
    [Inject] private CombatResolver _combatResolver;
    [Inject] private GameLoop _gameLoop;
    [Inject] private ITerritoryService _territoryService;
    [Inject] private GoldWallet _goldWallet;  // 【探索重构-阶段5.5】部署合法性检查
    [Inject] private PublicBuildingMarkerManager _publicBuildingMarkerManager;
    [Inject(Optional = true)] private IMapInteractionGate _interactionGate; // 动态地图-阶段二：事务/动画期间交互锁

    private ICardView _nextCardView;
    private List<ICardView> _cardViews = new List<ICardView>();
    private Transform _handRoot;          
    private bool _isDealing = false;
    private Queue<CardData> _initialDealQueue = new Queue<CardData>();

    public void Initialize()
    {
        _handRoot = GameObject.Find("Canvas/card")?.transform;
        if (_handRoot == null)
        {
            throw new System.InvalidOperationException("[CardPresenter] Initialization failed: Canvas/card was not found.");
        }

        GameObject placeholder = _uiConfig.NextCardPlaceholder;
        if (placeholder == null)
        {
            throw new System.InvalidOperationException(
                "[CardPresenter] Initialization failed: MapGenerator did not set NextCardPlaceholder.");
        }

        RectTransform placeholderRect = placeholder.GetComponent<RectTransform>();
        if (placeholderRect == null)
        {
            throw new System.InvalidOperationException(
                "[CardPresenter] Initialization failed: NextCardPlaceholder has no RectTransform.");
        }

        // 【决策 3】开局纯随机：5 张手牌全部从随机池抽取（第一张触发移民保底），无固定箭塔
        for (int i = 0; i < 5; i++)
        {
            _initialDealQueue.Enqueue(BuildCardData(_cardService.GenerateNextCard()));
        }
       // Debug.Log($"[CardPresenter] 初始卡牌数据准备完成，共 {_initialDealQueue.Count} 张");

        // 开始顺序发牌
        //Debug.Log("[CardPresenter] 开始发牌...（5张手牌 + 1张预告卡）");
        DealNextCardFromQueue(placeholderRect, () =>
        {
            //Debug.Log("[CardPresenter] 初始5张手牌已全部发放完成 → 立即生成第1张预告卡");
            DealFirstPreviewCard(placeholderRect);
        });
    }

    /// <summary>
    /// 游戏开始时专用：生成并放置第一张预告卡（位置固定在 NextCardPlaceholder）
    /// </summary>
    private void DealFirstPreviewCard(RectTransform placeholderRect)
    {
        if (_nextCardView != null) return;

        DealOneCard(placeholderRect, BuildCardData(_cardService.GenerateNextCard()), null, true);  // true = 预告卡模式
    }

    /// <summary>
    /// 从队列中取出一张牌并发牌，完成后继续下一张（回调链）
    /// </summary>
    private void DealNextCardFromQueue(RectTransform placeholderRect, System.Action onAllDealt = null)
    {
        if (_initialDealQueue.Count == 0 || _isDealing)
        {
            onAllDealt?.Invoke();   // 队列为空时触发最终回调
            return;
        }

        _isDealing = true;

        CardData cardData = _initialDealQueue.Dequeue();
        DealOneCard(placeholderRect, cardData, () =>
        {
            _isDealing = false;
            if (_initialDealQueue.Count > 0)
            {
                DealNextCardFromQueue(placeholderRect, onAllDealt);  // 继续递归，带上最终回调
            }
            else
            {
                onAllDealt?.Invoke();   // 最后一张发完 → 执行最终回调
            }
        });
    }

    /// <summary>由普通卡配置构造 CardData（ID/IsUnit/CardSprite 派生自配置）。</summary>
    private static CardData BuildCardData(NormalCardConfigSO config)
    {
        if (config is UnitConfigSO unitConfig)
        {
            return new CardData
            {
                NormalCardConfig = config,
                ID = unitConfig.Id,
                CardSprite = config.cardSprite,
                IsUnit = true,
            };
        }
        if (config is BuildingConfigSO buildingConfig)
        {
            return new CardData
            {
                NormalCardConfig = config,
                ID = buildingConfig.buildingId,
                CardSprite = config.cardSprite,
                IsUnit = false,
            };
        }
        return new CardData { NormalCardConfig = config, CardSprite = config.cardSprite };
    }

    /// <summary>
    /// 真正执行发一张牌的逻辑，完成后调用 onComplete
    /// </summary>
    private void DealOneCard(RectTransform placeholderRect, CardData cardData, System.Action onComplete, bool isNext = false)
    {
        //Debug.Log($"[CardPresenter] DealOneCard 被调用，cardID={cardData.ID}，IsUnit={cardData.IsUnit}，isPreview={isPreview}");

        int emptySlot = -1;
        if (!isNext)
        {
            emptySlot = _cardService.GetFirstEmptySlot();
            if (emptySlot == -1)
            {
                Debug.Log("[CardPresenter] 手牌槽已满，跳过手牌补充");
                onComplete?.Invoke();
                return;
            }
        }

        // 实例化卡牌...
        GameObject prefab = _uiConfig.GetCardPrefab();
        GameObject cardObj = _container.InstantiatePrefab(prefab, _handRoot);

        RectTransform cardRect = cardObj.GetComponent<RectTransform>();
        cardRect.anchoredPosition = placeholderRect.anchoredPosition;
        cardRect.localScale = _uiConfig.NextCardSize;

        var view = cardObj.GetComponent<ICardView>() ?? cardObj.AddComponent<CardController>();

        Vector3 targetPosition;

        if (isNext)
        {
            targetPosition = placeholderRect.anchoredPosition;

            // 销毁旧预告卡（防止重复）
            if (_nextCardView != null)
            {
                GameObject.Destroy((_nextCardView as MonoBehaviour)?.gameObject);
            }

            _nextCardView = view;
            view.SetData(cardData, -1, targetPosition);   // placementID = -1 表示预告
            view.IsNextCard = true;

            _cardService.MarkDrawThisTurn();   // 标记已抽卡
        }
        else
        {
            Vector2 slotOffset = _cardService.GetSlotOffset(emptySlot);
            targetPosition = (Vector3)cardRect.anchoredPosition + (Vector3)slotOffset;

            view.SetData(cardData, emptySlot, targetPosition);
            view.IsNextCard = true;                     // 动画期间临时不可拖拽

            _cardService.RegisterCardView(emptySlot, view);
            _cardViews.Add(view);                       // 保持原有 list 一致性
        }

        // 播放入场动画
        view.PlayDealAnimation(targetPosition, () =>
        {
            if (!isNext)
            {
                view.IsNextCard = false;
            }           
            onComplete?.Invoke();
        }, isNext);   
    }

    /// <summary>
    /// 把次卡平滑移动到手牌槽位（恢复原始简洁版，无需回调）
    /// </summary>
    private void PromoteNextCardToHand()
    {
        if (_nextCardView == null) return;

        int emptySlot = _cardService.GetFirstEmptySlot();
        if (emptySlot == -1) return;

        RectTransform placeholderRect = _uiConfig.NextCardPlaceholder != null
            ? _uiConfig.NextCardPlaceholder.GetComponent<RectTransform>()
            : null;
        if (placeholderRect == null) return;

        Vector2 slotOffset = _cardService.GetSlotOffset(emptySlot);
        Vector3 targetPosition = (Vector3)placeholderRect.anchoredPosition
                               + new Vector3(slotOffset.x, slotOffset.y, 0);

        _nextCardView.PlacementID = emptySlot;
        _nextCardView.IsNextCard = false;
        _nextCardView.OriginPosition = targetPosition;

        _cardService.RegisterCardView(emptySlot, _nextCardView);
        _cardViews.Add(_nextCardView);

        // 滑动 + 放大（保持 0.3s）
        _nextCardView.RectTransform.DOKill();
        _nextCardView.RectTransform.DOAnchorPos(targetPosition, 0.3f).SetEase(Ease.OutQuad);
        _nextCardView.RectTransform.DOScale(_uiConfig.CardSize, 0.3f).SetEase(Ease.OutQuad);

        _nextCardView = null;   // 立即清空，让新卡可以立刻生成
    }

    /// <summary>
    /// 处理拖拽结束（放置卡牌）
    /// </summary>
    public bool HandleCardDragEnd(ICardView view, HexCellData targetCell, Vector3 releaseWorldPos)
    {
        if (_gameLoop != null && _gameLoop.IsPaused)
            return false;

        if (!IsReleaseValid(view.Data?.NormalCardConfig, targetCell))
            return false;

        NormalCardConfigSO config = view.Data?.NormalCardConfig;
        bool spawned = false;
        if (config is UnitConfigSO unitConfig)
        {
            spawned = SpawnUnit(unitConfig.Id, targetCell.RealCenterWorldCoordinate) != null;
        }
        else if (config is BuildingConfigSO buildingConfig)
        {
            spawned = SpawnBuilding(buildingConfig.buildingId, targetCell.RealCenterWorldCoordinate);
        }
        if (!spawned) return false;

        // 【探索重构-阶段7】出牌扣费
        _goldWallet.TrySpendGold(0, _goldWallet.CardCost);

        _cardService.RemoveCard(view.PlacementID);

        (view as MonoBehaviour)?.gameObject.SetActive(false);
        GameObject.Destroy((view as MonoBehaviour)?.gameObject);
        _cardViews.Remove(view);

        TryDealFromNextIfPossible();
        return true;
    }

    public void OnCardDragBegin(ICardView view) { }

    public void OnCardDragCancel(ICardView view) { }

    private bool IsReleaseValid(NormalCardConfigSO config, HexCellData cell)
    {
        if (cell == null || _movementSystem.IsDestinationReserved(cell.HexCoordinate)) return false;
        // 【程序化山脉-阶段 7.6】统一部署资格（决策 ①）：山格/水域不可部署单位或建筑。
        // 放置预览已按同一资格过滤（PlayerInputHandler），确认路径必须再次校验，防止"无高亮但可执行"窗口。
        if (config is UnitConfigSO && !MountainCellRule.CanSpawnUnitOnCell(cell)) return false;
        if (config is BuildingConfigSO && !MountainCellRule.CanBuildOnCell(cell)) return false;
        // 【动态地图-阶段二】交互锁：事务/动画期间受影响格禁止部署（§12.6）
        if (_interactionGate != null && _interactionGate.IsLocked(cell, MapInteractionType.Deploy)) return false;
        // 【探索重构-阶段7】部署需在势力范围内 + 有足够金币
        if (!_territoryService.IsInPlayerTerritory(cell)) return false;
        if (_logisticsService != null && !_logisticsService.IsLogisticsConnected(cell, 0)) return false;
        if (_goldWallet.Gold < _goldWallet.CardCost) return false;
        if (cell.HexType == Enums.HexType.LakeOrSea) return false;
        if (cell.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding) return false;
        if (cell.IsHaveUnit()) return false;
        // 金矿格不可部署建筑（单位不受限）
        if (config is BuildingConfigSO && cell.landForm != null && cell.landForm.blockBuildingSpawn) return false;
        return true;
    }

    // ====================== 单位生成 ======================

    /// <summary>IPlayerUnitSpawnService 实现：外部系统（如探索奖励）调用此方法生成玩家单位。</summary>
    public GameObject SpawnPlayerUnit(int unitID, Vector3 worldPosition)
    {
        return SpawnUnit(unitID, worldPosition);
    }

    private GameObject SpawnUnit(int unitID, Vector3 position)
    {
        GameObject prefab = _unitData.GetUnitPrefab(unitID);
        Transform parent = GameObject.Find("PlayerUnit")?.transform;
        Canvas prefabCanvas = prefab != null ? prefab.GetComponentInChildren<Canvas>() : null;
        bool hasUnitUi = prefabCanvas != null &&
                         prefabCanvas.transform.childCount >= 2 &&
                         prefabCanvas.transform.GetChild(1).childCount >= 3 &&
                         prefabCanvas.transform.GetChild(1).GetComponent<Slider>() != null;
        if (prefab == null || parent == null || !hasUnitUi)
        {
            Debug.LogError($"[CardPresenter] Unit card {unitID} cannot be deployed because its prefab hierarchy is incomplete.");
            return null;
        }

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
            _unitData.GetUnitData(unitID)
        );

        g.GetComponent<UnitMovementController>().characterData = characterData;
        g.GetComponent<UnitMovementController>().PlayerIndex = 0;

        // 面板数据初始化
        CharacterData.InfoPanelData infoPanelData = new CharacterData.InfoPanelData();
        infoPanelData.sprite = _unitData.GetCard(characterData.UnitID);
        infoPanelData.name = characterData.unitData.unitName;
        infoPanelData.skillIcon = _unitData.GetSkillIcon(characterData.UnitID);
        infoPanelData.InfoDatas = new List<KeyValuePair<KeyValuePair<Sprite, string>, float>>();

        KeyValuePair<Sprite, string> Movement = new KeyValuePair<Sprite, string>(_uiConfig.GetMovementPointsIcon(), "剩余移动力");
        KeyValuePair<Sprite, string> MeleeAttack = new KeyValuePair<Sprite, string>(_uiConfig.GetMeleeAttackPointsIcon(), "攻击力");

        if (characterData.UnitID == 0) // 移民
        {
            infoPanelData.InfoDatas.Add(new KeyValuePair<KeyValuePair<Sprite, string>, float>(Movement, characterData.unitData.MovementPoints));
        }
        else
        {
            infoPanelData.InfoDatas.Add(new KeyValuePair<KeyValuePair<Sprite, string>, float>(MeleeAttack, characterData.unitData.BasicAttackValue));
            infoPanelData.InfoDatas.Add(new KeyValuePair<KeyValuePair<Sprite, string>, float>(Movement, characterData.unitData.MovementPoints));
        }
        characterData.infoPanelData = infoPanelData;

        // UI 画布设置（共享样板 SpawnUIWiring；颜色沿用原玩家逻辑）
        Color unitHealthColor = g.CompareTag("PlayerUnit") ? Color.green : Color.red;
        SpawnUIWiring.WireUnitCanvas(g, characterData, unitHealthColor, _container, _uiConfig);

        // Commit domain state only after the runtime object and UI are complete.
        Vector3 hexCoord = _mapDataService.WorldToHexCoordinate(g.transform.position);
        HexCellData h = _mapDataService.GetCell(hexCoord);
        _unitRepository.AddPlayerUnit(g, characterData);
        h.SetHaveUnit(true, g);
        // 【探索重构-阶段5】部署不再自动探索周围地块
        _mapVisualEvent.Raise();

        // 【批次 D】挂载 PlayerUnitBrain，注入全部依赖（含 CombatResolver 和建城所需依赖），注册到 GameLoop
        var brain = g.AddComponent<PlayerUnitBrain>();
        brain.Initialize(
            characterData,
            UnitStrategyFactory.Create(_unitData.TryGetUnitConfig(unitID, out var unitConfig) ? unitConfig : null),
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
            audioManager: _audioManager,
            markerManager: _publicBuildingMarkerManager);
        _gameLoop.Register(brain);

        if(_audioManager != null)
        {
            _audioManager.PlaySFX("Chimes_Harp-013");
        }
        else 
        {
            Debug.LogWarning("[CardPresenter] AudioManager 未注入，无法播放单位生成");
        }

        return g;
    }

    // ====================== 建筑生成 ======================

    /// <summary>IPlayerBuildingSpawnService 实现：外部系统（如探索奖励）调用此方法生成玩家建筑。</summary>
    public bool SpawnPlayerBuilding(int buildingID, Vector3 worldPosition)
    {
        return SpawnBuilding(buildingID, worldPosition);
    }

    private bool SpawnBuilding(int buildingID, Vector3 position)
    {
        Vector3 v = _mapDataService.WorldToHexCoordinate(position);
        HexCellData h = _mapDataService.GetCell(v);

        BuildingConfigSO config = _buildingData.TryGetBuildingConfig(buildingID, out var bc) ? bc : null;
        Enums.BulidingType buildingType = config != null ? config.buildingType : (Enums.BulidingType)(buildingID + 1);

        GameObject prefab = _buildingData.GetBuildingPrefab(buildingID);
        Transform parent = GameObject.Find("PlayerBuilding")?.transform;
        Canvas prefabCanvas = prefab != null ? prefab.GetComponentInChildren<Canvas>() : null;
        bool hasBuildingUi = prefabCanvas != null &&
                             prefabCanvas.transform.childCount >= 1 &&
                             prefabCanvas.transform.GetChild(0).childCount >= 1 &&
                             prefabCanvas.transform.GetChild(0).GetComponent<Slider>() != null;
        if (h == null || prefab == null || parent == null || !hasBuildingUi)
        {
            Debug.LogError($"[CardPresenter] Building card {buildingID} cannot be deployed because its prefab hierarchy is incomplete.");
            return false;
        }

        GameObject g = Object.Instantiate(prefab);
        g.transform.SetParent(parent, false);
        g.transform.position = position;
        g.tag = "PlayerBuilding";

        BuildingController buildingController = g.AddComponent<BuildingController>();
        _container.Inject(buildingController);

        BuildingData buildingData = new BuildingData(
            buildingType,
            _buildingData,
            buildingID);
        buildingController.buildingData = buildingData;
        buildingData.controller = buildingController;
        buildingController.bulidingType = buildingType;

        // Finish runtime UI before committing map, ownership, and progression state.
        // 共享样板 SpawnUIWiring；玩家建筑血条为绿色（canvas 已由上方 hasBuildingUi 预校验非空）。
        SpawnUIWiring.WireBuildingCanvas(g, buildingController, Color.green, _container, _uiConfig);

        // 【探索重构-阶段5.5】建筑部署不拓展势力范围。势力范围仅由探索和公共建筑占领产生。

        // 【探索重构-阶段5】部署不再自动探索周围地块
        _mapVisualEvent.Raise();

        h.BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>(buildingType, g);

        // 建筑类型特殊处理
        switch (buildingController.bulidingType)
        {
            case Enums.BulidingType.AttackStatue:
                _audioManager?.PlaySFX("Long_Sword_Scrape 01");
                _playerModelManager.Index_AttackBuilding.Add(_playerModelManager.AttackBuildingIndex++, g);
                break;
            case Enums.BulidingType.DefenseStatue:
                _audioManager?.PlaySFX("Metallic_Weapon_Hit-014");
                _audioManager?.PlaySFX("Metallic_Weapon_Hit-020");
                _playerModelManager.Index_DefenseBuilding.Add(_playerModelManager.DefenseBuildingIndex++, g);
                break;
            case Enums.BulidingType.Altar:
                _audioManager?.PlaySFX("Chimes_Harp-012");
                _playerModelManager.Index_AltarBuilding.Add(_playerModelManager.AltarBuildingIndex++, g);
                break;
            case Enums.BulidingType.TechnologyAndCultural:
                _audioManager?.PlaySFX("LevelUP6");
                _playerModelManager.Index_TechnologyAndCulturalBuilding.Add(_playerModelManager.TechnologyAndCulturalBuildingIndex++, g);
                break;
            case Enums.BulidingType.Barracks:
                _audioManager?.PlaySFX("Chimes_Harp-012");
                _playerModelManager.Index_BarracksBuilding.Add(_playerModelManager.BarracksBuildingIndex++, g);
                SetupBarracksSpawner(g, config);
                break;
            case Enums.BulidingType.ArrowTower:
                _audioManager?.PlaySFX("Chimes_Harp-012");
                _playerModelManager.Index_ArrowTowerBuilding.Add(_playerModelManager.ArrowTowerBuildingIndex++, g);
                SetupArrowTowerShooter(g);
                break;
        }

        if (config != null ? config.blocksMovement : (buildingID == 0 || buildingID == 1))
            h.movementCost = float.MaxValue;

        buildingController.Player_City_Index = h.Player_City_Index;

        return true;
    }

    private void SetupBarracksSpawner(GameObject buildingObj, BuildingConfigSO config)
    {
        var spawner = buildingObj.AddComponent<BarracksSpawner>();
        _container.Inject(spawner);
        if (config != null && config.producedUnit != null)
        {
            spawner.Initialize(config.producedUnit);
        }
    }

    private void SetupArrowTowerShooter(GameObject buildingObj)
    {
        var shooter = buildingObj.GetComponent<ArrowTowerShooter>() ?? buildingObj.AddComponent<ArrowTowerShooter>();
        _container.Inject(shooter);
    }

    /// <summary>
    /// 每回合结束时补充一张卡（使用队列方式或直接发牌）
    /// </summary>
    public void OnTurnEnded()
    {
        // 抽卡/发卡已完全分离，此处只需安全兜底
        if (_nextCardView == null)
        {
            Debug.LogWarning("[CardPresenter] OnTurnEnded: 次卡槽意外为空，立即补充");
            DrawNewNextCard();
        }
    }

    // 【批次 B】实时化：移除每回合发卡限制（CanDealThisTurn 在实时下无意义）
    // 只要手牌有空位且预览槽有卡，立即补发。
    public void TryDealFromNextIfPossible()
    {
        if (_nextCardView == null) return;
        if (_cardService.GetFirstEmptySlot() == -1) return;

        // 立即开始滑动旧次卡（不等待）
        PromoteNextCardToHand();

        // 同时立即刷新新次卡（带优雅弹出动画）
        DrawNewNextCard();
    }

    /// <summary>
    /// 次卡槽为空时立即抽一张新卡（与回合无关）
    /// </summary>
    private void DrawNewNextCard()
    {
        if (_nextCardView != null) return;

        var placeholder = _uiConfig?.NextCardPlaceholder;
        if (placeholder == null || placeholder.GetComponent<RectTransform>() == null)
        {
            Debug.LogWarning("[CardPresenter] NextCardPlaceholder 尚未就绪，跳过抽卡");
            return;
        }

        NormalCardConfigSO config = _cardService.GenerateNextCard();

        DealOneCard(placeholder.GetComponent<RectTransform>(), BuildCardData(config), null, true);
    }
}
