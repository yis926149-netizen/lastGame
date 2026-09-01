using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;
using DG.Tweening;
using UIToolkitDemo;

public class CardPresenter : IInitializable, IPlayerUnitSpawnService, IPlayerBuildingSpawnService, ICardDropHandler, ICardDragVisualHandler
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
    [Inject(Optional = true)] private CardDragWorldPreviewController _dragPreview; // 卡牌拖拽世界空间预览（缺失时静默降级为只缩卡）

    private ICardView _nextCardView;
    private List<ICardView> _cardViews = new List<ICardView>();
    // 持握期组件状态快照（PrepareForDrag 产出，RestoreForDeployment 消费），按拖拽 token 挂账。
    private readonly Dictionary<ICardView, CardDragPreviewUtils.PreparationState> _dragPrepareStates =
        new Dictionary<ICardView, CardDragPreviewUtils.PreparationState>();
    private Transform _handRoot;
    // 飞入特效互斥标志：不排队，飞行中再次触发直接同步结算。
    private bool _cardFlyInProgress;
    private bool _isDealing = false;
    private Queue<CardData> _initialDealQueue = new Queue<CardData>();

    public void Initialize()
    {
        _handRoot = GameObject.Find("card")?.transform;
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

        // 【决策 3】开局纯随机：N 张手牌全部从随机池抽取（第一张触发移民保底），无固定箭塔
        for (int i = 0; i < CoreGameplayConfigProvider.InitialHandCardCount; i++)
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

    /// <summary>由普通卡配置构造 CardData（ID/IsUnit/CardSprite 派生自配置，卡费取 Excel 数值）。</summary>
    private CardData BuildCardData(NormalCardConfigSO config)
    {
        int cost = GetCardCost(config);
        if (config is UnitConfigSO unitConfig)
        {
            return new CardData
            {
                NormalCardConfig = config,
                ID = unitConfig.Id,
                CardSprite = config.cardSprite,
                CardCost = cost,
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
                CardCost = cost,
                IsUnit = false,
            };
        }
        return new CardData { NormalCardConfig = config, CardSprite = config.cardSprite, CardCost = cost };
    }

    private int GetCardCost(NormalCardConfigSO config)
    {
        // 卡费数值仅取 Excel 平衡库（阶段6 唯一主源，Provider 内部处理）。
        if (config is UnitConfigSO unitConfig)
            return _unitData.GetUnitCardCost(unitConfig.Id);
        if (config is BuildingConfigSO buildingConfig)
            return _buildingData.GetBuildingCardCost(buildingConfig.buildingId);
        return config != null ? config.cardCost : 0;
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

        // 播放入场动画（IsTweening 锁由 PlayDealAnimation 内部随补间自行上锁/解锁）
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
        // IsNextCard 已在上面置 false（升为手牌），此刻卡牌已可交互，
        // 必须用 IsTweening 挡住"提起态"——否则补间途中鼠标移入/点击会 DOKill 掉滑动，
        // 卡牌从半路直接跳到槽位。
        // 上锁必须在 DOKill 之后：DOKill 会带出上一条补间的 OnComplete，把锁清回 false。
        ICardView promoting = _nextCardView;
        _nextCardView.RectTransform.DOKill();
        _nextCardView.IsTweening = true;
        _nextCardView.RectTransform.DOAnchorPos(targetPosition, 0.3f).SetEase(Ease.OutQuad)
            .OnComplete(() => promoting.IsTweening = false);
        _nextCardView.RectTransform.DOScale(_uiConfig.CardSize, 0.3f).SetEase(Ease.OutQuad);

        _nextCardView = null;   // 立即清空，让新卡可以立刻生成
    }

    /// <summary>
    /// 处理拖拽结束（放置卡牌）。同步提交、异步只做视觉（§4.1）：
    /// 校验 → 恢复运行时组件 → 交出实例所有权 → 复用实例完成生成 → 失败销毁实例返回 false、
    /// 成功则扣费腾槽补牌并启动纯视觉落位补间。返回真实成败。
    /// </summary>
    public bool HandleCardDragEnd(ICardView view, HexCellData targetCell, Vector3 releaseWorldPos)
    {
        if (!IsReleaseValid(view.Data?.NormalCardConfig, targetCell))
            return false;

        NormalCardConfigSO config = view.Data?.NormalCardConfig;
        if (config == null) return false;

        // 预览实例：同一对象就地升级为真实单位/建筑。ReleaseOwnership 必须先于
        // ConsumePlayedCard——后者会 SetActive(false) 卡牌并触发 OnDisable，
        // 若控制器仍持有实例，任何走到 Cancel 的分支都会把刚落地的真实单位销毁。
        GameObject instance = _dragPreview != null ? _dragPreview.ReleaseOwnership(view) : null;
        if (instance != null)
        {
            _dragPrepareStates.TryGetValue(view, out CardDragPreviewUtils.PreparationState dragState);
            _dragPrepareStates.Remove(view);
            CardDragPreviewUtils.RestoreForDeployment(instance, dragState);
        }

        // 落位补间起点 = 松手瞬间的悬停位置（含 hoverHeight）。
        Vector3 releaseHoverPos = instance != null ? instance.transform.position : releaseWorldPos;

        bool spawned;
        try
        {
            spawned = TrySpawnCard(config, targetCell, instance);
        }
        catch (System.Exception exception)
        {
            // Some spawn steps commit the entity before optional UI/audio/brain setup finishes.
            // A committed entity must still consume its card, or the same card can deploy twice.
            spawned = IsDeploymentCommitted(config, targetCell);
            Debug.LogException(exception);
        }

        if (!spawned)
        {
            // 提交失败：销毁预览实例、return false，卡牌由 OnEndDrag 复位（未扣费、未腾槽）。
            if (instance != null) GameObject.Destroy(instance);
            return false;
        }

        if (instance != null)
        {
            // 补间终点 = 生成后的实际位置（单位可能被 TryClaimStandingUnit 二次吸附到站位槽，
            // 不一定是格心）；把位置拨回释放悬停点，再交控制器补间回终点。
            // 逻辑状态从第 0 帧起就是「已落地」，只有画面在飞。
            instance.transform.position = releaseHoverPos;
        }

        ConsumePlayedCard(view);

        if (instance != null)
            _dragPreview.PlayLanding(instance, releaseHoverPos, null);

        return true;
    }

    private bool TrySpawnCard(NormalCardConfigSO config, HexCellData targetCell, GameObject instance = null)
    {
        if (config is UnitConfigSO unitConfig)
            return SpawnUnit(unitConfig.Id, targetCell.RealCenterWorldCoordinate, instance) != null;

        if (config is BuildingConfigSO buildingConfig)
            return SpawnBuilding(buildingConfig.buildingId, targetCell.RealCenterWorldCoordinate, instance);

        return false;
    }

    private static bool IsDeploymentCommitted(NormalCardConfigSO config, HexCellData targetCell)
    {
        if (targetCell == null) return false;
        if (config is UnitConfigSO) return targetCell.HasAnyStandingUnit();
        if (config is BuildingConfigSO) return targetCell.BulidingTypeOnHex_Building.Value != null;
        return false;
    }

    private void ConsumePlayedCard(ICardView view)
    {
        // Hide first so later callbacks cannot leave a successfully played card usable.
        MonoBehaviour viewBehaviour = view as MonoBehaviour;
        if (viewBehaviour != null) viewBehaviour.gameObject.SetActive(false);

        _cardService.RemoveCard(view.PlacementID);
        _cardViews.Remove(view);

        // 【探索重构-阶段7】出牌扣费（按卡单价收费）
        _goldWallet.TrySpendGold(0, view.Data?.CardCost ?? _goldWallet.CardCost);

        if (viewBehaviour != null) GameObject.Destroy(viewBehaviour.gameObject);
        TryDealFromNextIfPossible();
    }

    public void OnCardDragBegin(ICardView view)
    {
        // 拎起即实例化一次世界空间预览：同一实例松手后就地升级为真实单位/建筑（§4.5）。
        if (_dragPreview == null || view == null) return;

        GameObject modelPrefab = ResolveModelPrefab(view.Data?.NormalCardConfig);
        if (modelPrefab == null) return;

        // 统一 Object.Instantiate：注入由 SpawnUnit/SpawnBuilding 单点完成，
        // 避免与 _container.InjectGameObject 双重注入（§4.2 冲突处理）。
        GameObject instance = Object.Instantiate(modelPrefab);
        if (instance == null) return;

        CardDragPreviewUtils.PreparationState state = CardDragPreviewUtils.PrepareForDrag(instance);
        _dragPrepareStates[view] = state;   // 持握期状态随 token 挂账，落地/取消时清理。

        // token = view：拒绝上一张卡的迟到回调。
        _dragPreview.Begin(instance, view);
    }

    public void OnCardDragCancel(ICardView view)
    {
        _dragPrepareStates.Remove(view);
        _dragPreview?.Cancel(view);
    }

    /// <summary>ICardDragVisualHandler：逐帧把原始触点转发给持握控制器（内部换算逻辑射线坐标）。</summary>
    public void OnCardDragUpdate(ICardView view, Vector2 pointerPosition)
    {
        _dragPreview?.Follow(pointerPosition, view);
    }

    /// <summary>
    /// ICardDragVisualHandler：拖拽成功结束的显式清理入口。幂等且不销毁——
    /// 成功路径下实例所有权已在 HandleCardDragEnd 内交出，落位补间继续由控制器驱动；
    /// 失败路径的销毁由随后的 OnCardDragCancel 完成（成功路径不会走 Cancel）。
    /// </summary>
    public void OnCardDragEnd(ICardView view)
    {
        _dragPreview?.End(view);
    }

    /// <summary>取普通卡对应的模型 Prefab；与实际部署使用同一数据源（UnitConfigSO.unitModel / BuildingConfigSO.buildingModel）。</summary>
    private GameObject ResolveModelPrefab(NormalCardConfigSO config)
    {
        if (config is UnitConfigSO unitConfig)
            return _unitData?.GetUnitPrefab(unitConfig.Id);

        if (config is BuildingConfigSO buildingConfig)
            return _buildingData?.GetBuildingPrefab(buildingConfig.buildingId);

        return null;
    }

    /// <summary>ICardDropHandler：查询普通卡能否部署到指定格（放置预览高亮与确认路径共用同一规则）。</summary>
    public bool CanDeployTo(CardData data, HexCellData cell)
    {
        return IsReleaseValid(data?.NormalCardConfig, cell);
    }

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
        if (_goldWallet.Gold < (config != null ? GetCardCost(config) : _goldWallet.CardCost)) return false;
        if (cell.HexType == Enums.HexType.LakeOrSea) return false;
        if (cell.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding) return false;
        // 【多单位落点】部署改按有效容量判断：仍有自由站位槽即可部署。
        if (!cell.HasFreeStandingSlot()) return false;
        // 金矿格不可部署建筑（单位不受限）
        if (config is BuildingConfigSO && cell.landForm != null && LandFormEffectRule.GetBlockBuildingSpawn(cell.landForm)) return false;
        return true;
    }

    // ====================== 单位生成 ======================

    /// <summary>IPlayerUnitSpawnService 实现：外部系统（如探索奖励）调用此方法生成玩家单位。</summary>
    public GameObject SpawnPlayerUnit(int unitID, Vector3 worldPosition)
    {
        return SpawnUnit(unitID, worldPosition);
    }

    /// <summary>
    /// instance 非空时跳过 Object.Instantiate，直接对拖拽预览实例做后续接线（§4.2 模型实例复用）；
    /// instance 为空时行为不变（探索奖励等常规入口）。
    /// </summary>
    private GameObject SpawnUnit(int unitID, Vector3 position, GameObject instance = null)
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
            Debug.LogError($"[RewardTrace] SpawnUnit fail unitId={unitID} prefab={(prefab == null ? "NULL" : "OK")} parent={(parent == null ? "NULL" : "OK")} hasUnitUi={hasUnitUi}");
            return null;
        }

        GameObject g = instance != null ? instance : Object.Instantiate(prefab);
        g.transform.SetParent(parent, false);
        // 必须先清掉 hoverHeight（放到目标格地面高度），否则下方 WorldToHexCoordinate 反推的格子不是目标格。
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
        // 【多单位落点】按站位槽生成：取得自由站位槽并吸附到槽位世界坐标（满员退回旧单单位写入兜底）。
        if (h != null)
        {
            if (h.TryClaimStandingUnit(g, position, position, preferLine: false, out _, out Vector3 slotPos))
                g.transform.position = slotPos;
            else
                h.SetHaveUnit(true, g);
        }
        // 【探索重构-阶段5】部署不再自动探索周围地块
        _mapVisualEvent.Raise();

        // 【批次 D】挂载 PlayerUnitBrain，注入全部依赖（含 CombatResolver 和建城所需依赖），注册到 GameLoop
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

    /// <summary>
    /// instance 非空时跳过 Object.Instantiate，直接对拖拽预览实例做后续接线（§4.2 模型实例复用）；
    /// instance 为空时行为不变（探索奖励等常规入口）。
    /// </summary>
    private bool SpawnBuilding(int buildingID, Vector3 position, GameObject instance = null)
    {
        Vector3 v = _mapDataService.WorldToHexCoordinate(position);
        HexCellData h = _mapDataService.GetCell(v);

        BuildingConfigSO config = _buildingData.TryGetBuildingConfig(buildingID, out var bc) ? bc : null;
        Enums.BulidingType buildingType = config != null ? _buildingData.GetBuildingType(buildingID) : (Enums.BulidingType)(buildingID + 1);

        GameObject prefab = _buildingData.GetBuildingPrefab(buildingID);
        Transform parent = GameObject.Find("PlayerBuilding")?.transform;
        Canvas prefabCanvas = prefab != null ? prefab.GetComponentInChildren<Canvas>() : null;
        bool hasBuildingUi = prefabCanvas != null &&
                             prefabCanvas.transform.childCount >= 1 &&
                             prefabCanvas.transform.GetChild(0).childCount >= 1 &&
                             prefabCanvas.transform.GetChild(0).GetComponent<Slider>() != null;
        if (h == null || prefab == null || parent == null || !hasBuildingUi)
        {
            Debug.LogError($"[RewardTrace] SpawnBuilding fail buildingId={buildingID} h={(h == null ? "NULL" : "OK")} prefab={(prefab == null ? "NULL" : "OK")} parent={(parent == null ? "NULL" : "OK")} hasBuildingUi={hasBuildingUi}");
            return false;
        }

        GameObject g = instance != null ? instance : Object.Instantiate(prefab);
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

        // Commit before notifying fallible visual listeners. If a listener fails, the deployed building
        // remains authoritative and HandleCardDragEnd will still consume the card.
        h.BulidingTypeOnHex_Building = new KeyValuePair<Enums.BulidingType, GameObject>(buildingType, g);

        // 【探索重构-阶段5.5】建筑部署不拓展势力范围。势力范围仅由探索和公共建筑占领产生。

        // 【探索重构-阶段5】部署不再自动探索周围地块
        _mapVisualEvent.Raise();

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

        if (config != null ? _buildingData.GetBuildingBlocksMovement(buildingID) : (buildingID == 0 || buildingID == 1))
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
    /// 将指定卡牌强制插入手牌第一位（slot 0）的兼容入口。
    /// 现有卡牌整体右移一位；若手牌已满，末位卡牌被销毁。失败时仅记录日志，不抛出异常。
    /// </summary>
    public void InsertCardAtFront(NormalCardConfigSO config)
    {
        TryInsertCardAtFront(config);
    }

    /// <summary>
    /// 同步插牌的安全入口：完整校验依赖与实例化结果，任一无效即返回 false 且不部分移动 slot。
    /// 全部通过后原子执行「ShiftSlotsRight → 旧卡右移/末位挤出销毁 → 新卡落位 slot 0」。
    /// </summary>
    private bool TryInsertCardAtFront(NormalCardConfigSO config)
    {
        if (config == null) return false;

        if (_cardService == null || _container == null || _uiConfig == null || _handRoot == null)
        {
            Debug.LogError("[CardPresenter] TryInsertCardAtFront: 环境依赖缺失（cardService/container/uiConfig/handRoot），无法同步插入卡牌。");
            return false;
        }

        GameObject placeholder = _uiConfig.NextCardPlaceholder;
        if (placeholder == null)
        {
            Debug.LogWarning("[CardPresenter] TryInsertCardAtFront: NextCardPlaceholder 缺失，无法同步插入卡牌。");
            return false;
        }
        RectTransform placeholderRect = placeholder.GetComponent<RectTransform>();
        if (placeholderRect == null)
        {
            Debug.LogWarning("[CardPresenter] TryInsertCardAtFront: NextCardPlaceholder 无 RectTransform，无法同步插入卡牌。");
            return false;
        }

        GameObject prefab = _uiConfig.GetCardPrefab();
        if (prefab == null)
        {
            Debug.LogError("[CardPresenter] TryInsertCardAtFront: 卡牌 prefab 缺失，无法同步插入卡牌。");
            return false;
        }

        // 先实例化新卡作为「实例化结果」校验：失败时不移动任何既有 slot。
        GameObject cardObj = _container.InstantiatePrefab(prefab, _handRoot);
        if (cardObj == null)
        {
            Debug.LogError("[CardPresenter] TryInsertCardAtFront: 卡牌实例化失败，无法同步插入卡牌。");
            return false;
        }
        RectTransform cardRect = cardObj.GetComponent<RectTransform>();
        ICardView view = cardObj.GetComponent<ICardView>() ?? cardObj.AddComponent<CardController>();
        if (cardRect == null || view == null)
        {
            Debug.LogError("[CardPresenter] TryInsertCardAtFront: 卡牌实例缺少 RectTransform 或 ICardView。");
            if (cardObj != null) GameObject.Destroy(cardObj);
            return false;
        }

        // 1. CardService 层整体右移，取出被挤掉的末位卡（满手牌时才非 null）
        _cardService.ShiftSlotsRight(out ICardView droppedView);

        // 2. 更新所有现有手牌的 PlacementID 并滑动到新位置（只遍历有效 view，清除已销毁 view）
        for (int i = _cardViews.Count - 1; i >= 0; i--)
        {
            ICardView cardView = _cardViews[i];
            MonoBehaviour viewBehaviour = cardView as MonoBehaviour;
            if (viewBehaviour == null)
            {
                _cardViews.RemoveAt(i);
                continue;
            }

            int newSlot = cardView.PlacementID + 1;
            cardView.PlacementID = newSlot;
            Vector2 newOffset = _cardService.GetSlotOffset(newSlot);
            Vector3 newPos = (Vector3)placeholderRect.anchoredPosition
                           + new Vector3(newOffset.x, newOffset.y, 0);
            cardView.OriginPosition = newPos;
            cardView.RectTransform.DOKill();
            cardView.RectTransform.DOAnchorPos(newPos, 0.2f).SetEase(Ease.OutQuad);
        }

        // 3. 销毁被挤掉的末位卡
        if (droppedView != null)
        {
            _cardViews.Remove(droppedView);
            GameObject.Destroy((droppedView as MonoBehaviour)?.gameObject);
        }

        // 4. 在 slot 0 落位新卡
        CardData cardData = BuildCardData(config);
        cardRect.localScale = _uiConfig.CardSize;

        Vector2 slot0Offset = _cardService.GetSlotOffset(0);
        Vector3 slot0Pos = (Vector3)placeholderRect.anchoredPosition
                         + new Vector3(slot0Offset.x, slot0Offset.y, 0);

        view.SetData(cardData, 0, slot0Pos);
        view.IsNextCard = false;
        cardRect.anchoredPosition = slot0Pos;

        _cardService.RegisterCardView(0, view);
        _cardViews.Add(view);
        return true;
    }

    /// <summary>
    /// 将指定卡牌以飞入表现插入手牌第一位（slot 0）。
    /// 飞行从 worldStartPos 起飞；落地瞬间才执行同步插牌和旧卡右移。
    /// 飞行中再次触发时，本次直接同步结算，不进入队列。
    /// </summary>
    public void InsertCardAtFrontWithFly(NormalCardConfigSO config, Vector3 worldStartPos)
    {
        if (config == null) return;

        // 互斥不是队列：飞行中再次触发直接同步结算，不取消当前飞行。
        if (_cardFlyInProgress)
        {
            TryInsertCardAtFront(config);
            return;
        }

        // 动画环境校验：任一缺失即降级为同步插牌。
        if (_cardService == null || _container == null || _uiConfig == null || _handRoot == null)
        {
            TryInsertCardAtFront(config);
            return;
        }

        GameObject placeholder = _uiConfig.NextCardPlaceholder;
        RectTransform placeholderRect = placeholder != null ? placeholder.GetComponent<RectTransform>() : null;
        if (placeholderRect == null)
        {
            TryInsertCardAtFront(config);
            return;
        }

        RectTransform handRootRect = _handRoot as RectTransform;
        if (handRootRect == null)
        {
            TryInsertCardAtFront(config);
            return;
        }

        GameObject prefab = _uiConfig.GetCardPrefab();
        if (prefab == null)
        {
            TryInsertCardAtFront(config);
            return;
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            TryInsertCardAtFront(config);
            return;
        }

        Vector3 screenPos = camera.WorldToScreenPoint(worldStartPos);
        // 起点在相机背后：不 clamp 后从错误位置飞出，直接降级同步插牌。
        if (screenPos.z <= 0f)
        {
            TryInsertCardAtFront(config);
            return;
        }

        GameObject flyGO = null;
        Tween driver = null;
        bool flyCompleted = false;
        bool animationStarted = false;

        // 唯一幂等结束函数：正常抵达/对象销毁/坐标失败/创建异常/终止 driver 都只能经过这里。
        void FinishFly(bool settleCard)
        {
            if (flyCompleted) return;
            flyCompleted = true;

            if (driver != null)
            {
                driver.Kill();
                driver = null;
            }

            if (flyGO != null)
            {
                flyGO.SetActive(false);
                GameObject.Destroy(flyGO);
                flyGO = null;
            }

            _cardFlyInProgress = false;

            // 即使飞行卡已被销毁，也必须尝试一次同步结算。
            if (settleCard) TryInsertCardAtFront(config);
        }

        try
        {
            flyGO = _container.InstantiatePrefab(prefab, _handRoot);
            if (flyGO == null)
            {
                FinishFly(true);
                return;
            }

            RectTransform flyRect = flyGO.GetComponent<RectTransform>();
            if (flyRect == null)
            {
                FinishFly(true);
                return;
            }

            // ── 视觉初始化（普通卡 prefab 带费用节点与透明大 Button，必须全部关闭交互）──
            CardController controller = flyGO.GetComponent<CardController>();
            if (controller != null) controller.enabled = false;

            Image image = flyGO.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = config.cardSprite;
                image.raycastTarget = false;
            }

            WriteFlyCardCost(flyGO, config);

            foreach (Graphic graphic in flyGO.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic != null) graphic.raycastTarget = false;
            }
            foreach (Button button in flyGO.GetComponentsInChildren<Button>(true))
            {
                if (button != null) button.interactable = false;
            }

            flyRect.anchorMin = new Vector2(0.5f, 0.5f);
            flyRect.anchorMax = new Vector2(0.5f, 0.5f);
            flyGO.transform.SetAsLastSibling();

            CanvasGroup canvasGroup = flyGO.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = flyGO.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;

            // ── 坐标换算（slot0 落点与 revealLocal 必须同属 _handRoot 坐标系）──
            Vector2 slot0Offset = _cardService.GetSlotOffset(0);
            Vector2 slot0Pos = placeholderRect.anchoredPosition + slot0Offset;

            // 卡牌实际渲染尺寸（本地单位 × 缩放 × Canvas 缩放 → 屏幕像素），用于屏幕边缘安全区。
            float canvasScale = handRootRect.lossyScale.x;
            if (canvasScale <= 0f) canvasScale = 1f;
            Vector2 cardRenderSize = Vector2.Scale(flyRect.rect.size, (Vector2)_uiConfig.CardSize) * canvasScale;
            if (cardRenderSize.x <= 0f || cardRenderSize.y <= 0f)
                cardRenderSize = new Vector2(120f, 170f);
            float marginX = cardRenderSize.x * 0.5f + 8f;
            float marginY = cardRenderSize.y * 0.5f + 8f;
            screenPos.x = Mathf.Clamp(screenPos.x, marginX, Screen.width - marginX);
            screenPos.y = Mathf.Clamp(screenPos.y, marginY, Screen.height - marginY);

            Vector2 revealLocal;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(handRootRect, screenPos, null, out revealLocal))
            {
                FinishFly(true);
                return;
            }

            Vector3 displayScale = _uiConfig.CardSize;
            float appearTime = 0.50f;   // 阶段① 上升 + 放大
            float flyTime = 0.25f;      // 阶段② 俯冲入位
            float totalTime = appearTime + flyTime;

            float arcBump = Mathf.Min(UIScreenHelper.ReferenceHeight * 0.12f, 120f);
            Vector2 liftPos = revealLocal + new Vector2(0f, arcBump);                 // 阶段①顶点
            Vector2 dashControl = liftPos * 0.5f + new Vector2(0f, -arcBump * 0.25f); // 阶段②俯冲弧线控制点

            // 初始状态：出现点、小尺寸、微倾斜、透明
            flyRect.anchoredPosition = revealLocal;
            flyRect.localScale = displayScale * 0.35f;
            flyRect.localRotation = Quaternion.Euler(0f, 0f, -3f);
            canvasGroup.alpha = 0f;

            // 进度由 GameLoop.GameTime 驱动（暂停即冻结），driver 只承载每帧 OnUpdate。
            // SetLoops(-1) 使其永不自然结束，避免暂停时 DOTween 自身时钟走完而提前结算。
            float startGameTime = _gameLoop != null ? _gameLoop.GameTime : Time.time;

            driver = DOTween.To(() => 0f, _ => { }, 1f, totalTime)
                .SetEase(Ease.Linear)
                .SetLoops(-1)
                .OnUpdate(() =>
                {
                    if (flyGO == null)
                    {
                        FinishFly(true);
                        return;
                    }

                    float current = _gameLoop != null ? _gameLoop.GameTime : Time.time;
                    float elapsed = current - startGameTime;
                    if (elapsed < 0f) return;

                    if (elapsed < appearTime)
                    {
                        // 阶段① 上升 + 放大：到达最高点（liftPos）时缩放到最大，同步淡入、倾斜归正
                        float p = Mathf.Clamp01(elapsed / appearTime);
                        flyRect.anchoredPosition = Vector2.Lerp(revealLocal, liftPos, EaseOutQuad(p));
                        flyRect.localScale = displayScale * Mathf.Lerp(0.35f, 1f, EaseOutBack(p));
                        flyRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-3f, 0f, p));
                        canvasGroup.alpha = Mathf.Clamp01(p * 5f);
                    }
                    else
                    {
                        // 阶段② 从最高点沿俯冲弧线加速飞向 slot0
                        float p = Mathf.Clamp01((elapsed - appearTime) / flyTime);
                        float eased = EaseInCubic(p);
                        flyRect.anchoredPosition = QuadraticBezier(liftPos, dashControl, slot0Pos, eased);
                        flyRect.localScale = displayScale * Mathf.Lerp(1f, 0.97f, p);
                        flyRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 2f, Mathf.Sin(p * Mathf.PI)));
                        canvasGroup.alpha = 1f;
                    }

                    if (elapsed >= totalTime)
                    {
                        FinishFly(true);
                    }
                })
                .OnComplete(() => FinishFly(true));

            // 仅当实例化、坐标换算、driver 建立全部成功后才置忙碌标志。
            animationStarted = true;
            _cardFlyInProgress = true;
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
            FinishFly(true);
        }
        finally
        {
            // 只有未成功进入动画时才清除忙碌标志；成功路径由 FinishFly 统一清理。
            if (!animationStarted) _cardFlyInProgress = false;
        }
    }

    /// <summary>写入飞行卡费用：命中费用 TMP 则写入真实卡费；否则隐藏 cost 子节点避免默认费用错误。</summary>
    private void WriteFlyCardCost(GameObject flyGO, NormalCardConfigSO config)
    {
        if (flyGO == null) return;
        int cost = GetCardCost(config);

        // 与 CardController.SetData 同一费用节点：第 2 个子物体（cost）的第 1 个子物体（Text (TMP)）
        if (flyGO.transform.childCount > 1)
        {
            Transform costRoot = flyGO.transform.GetChild(1);
            if (costRoot != null && costRoot.childCount > 0)
            {
                TextMeshProUGUI costText = costRoot.GetChild(0).GetComponent<TextMeshProUGUI>();
                if (costText != null)
                {
                    costText.text = cost.ToString();
                    return;
                }
            }

            if (costRoot != null) costRoot.gameObject.SetActive(false);
        }
    }

    // ── 飞行卡动画用缓动与曲线 ──────────────────────────────

    private static float EaseOutQuad(float t)
    {
        return 1f - (1f - t) * (1f - t);
    }

    private static float EaseInCubic(float t)
    {
        return t * t * t;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    /// <summary>二次贝塞尔曲线采样（用于收槽弧线）。</summary>
    private static Vector2 QuadraticBezier(Vector2 a, Vector2 b, Vector2 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
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
