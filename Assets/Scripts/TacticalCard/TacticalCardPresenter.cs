using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Zenject;
using GameConfig;

public class TacticalCardPresenter : IInitializable, ICardDropHandler
{
    [Inject] private TacticalCardDatabaseSO _database;
    [Inject(Optional = true)] private TacticalCardBalanceDatabaseSO _balance; // Excel 数值（只读；阶段6 唯一主源）
    [Inject] private DiContainer _container;
    [Inject] private IMapDataService _mapDataService;
    [Inject] private IUIConfigProvider _uiConfig;
    [Inject] private GameLoop _gameLoop;
    [Inject] private BattleOrderBuffService _battleOrderBuff;

    private readonly Transform _anchor1;
    private readonly Transform _anchor2;
    private readonly TMP_Text[] _quantityTexts = new TMP_Text[MaxAnchorCount];
    private readonly List<TacticalCardInstance> _instances = new();
    private List<CardController> _cardViews;
    private GameObject _cardPrefab;

    private int _borrowedSlot = -1;   // 叠放借出中的槽位（-1 = 无）
    private GameObject _activeGhost;  // 拖拽幽灵（借出的那一张的视觉）
    private ICardView _borrowedView;  // 被借出卡的本体视图（拖拽期间留在槽位）

    private bool _flyBusy;                                          // 飞行卡动画进行中
    private readonly Queue<(TacticalCardSO Config, Vector3 WorldPos)> _pendingFlies = new(); // 连续奖励队列

    // 【Excel 数值化】战术卡槽位数量迁移至 CoreGameplayConfigProvider。
    private static int MaxAnchorCount => CoreGameplayConfigProvider.TacticalCardSlotCount;

    public TacticalCardPresenter(Transform tacticalCardAnchor1, Transform tacticalCardAnchor2,
        GameObject quantityBadge1, GameObject quantityBadge2)
    {
        _anchor1 = tacticalCardAnchor1;
        _anchor2 = tacticalCardAnchor2;
        _quantityTexts[0] = quantityBadge1 != null ? quantityBadge1.GetComponent<TMP_Text>() : null;
        _quantityTexts[1] = quantityBadge2 != null ? quantityBadge2.GetComponent<TMP_Text>() : null;
    }

    public void Initialize()
    {
        if (_database == null || _database.cards == null)
        {
            Debug.LogWarning("[TacticalCardPresenter] Database is null or empty.");
            return;
        }

        _cardPrefab = _uiConfig.GetTacticalCardPrefab();
        if (_cardPrefab == null)
        {
            Debug.LogWarning("[TacticalCardPresenter] Card prefab is null.");
            return;
        }

        // 构造注入的固定锚点
        if (_anchor1 == null || _anchor2 == null)
        {
            Debug.LogWarning("[TacticalCardPresenter] TacticalCard anchor points not registered via IUIConfigProvider. " +
                             "Ensure MapGenerator has TacticalCardAnchor1/TacticalCardAnchor2 assigned in the scene.");
            return;
        }

        _cardViews = new List<CardController>();

        // 启用卡列表：仅 Excel 数值库（enabled=true）→ 按 cardId 找资源（阶段6 唯一主源）。
        if (_balance == null)
            throw new System.InvalidOperationException(
                "[TacticalCard] Excel 战术卡数值库未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 TacticalCardBalanceDatabaseSO。");
        var source = new List<TacticalCardSO>();
        foreach (var b in _balance.EnabledCards)
        {
            var config = FindResource(b.cardId);
            if (config != null) source.Add(config);
        }

        for (int i = 0; i < source.Count && i < MaxAnchorCount; i++)
        {
            var config = source[i];
            if (config == null) continue;

            CardController cardController = CreateCardView(i, config);
            _cardViews.Add(cardController);

            _instances.Add(new TacticalCardInstance
            {
                Config = config,
                Quantity = 1,
            });

            RefreshQuantityBadge(i);
            Debug.Log($"[TacticalCardPresenter] Created: {config.cardName} (id={config.cardId})");
        }
    }

    /// <summary>
    /// 外部奖励（探索战术奖励等）发放一张战术牌：
    /// 已有同名牌 → 数量 +1；同名牌已耗尽（视图已销毁）→ 重建视图；
    /// 新类型 → 在空闲锚点槽位新建。数量 >1 时显示叠放徽标。
    /// 返回发放所在槽位索引，失败返回 -1。
    /// </summary>
    public int AddCard(TacticalCardSO config)
    {
        Debug.Log($"[RewardTrace] AddCard enter card={(config == null ? "NULL" : config.cardId)} prefab={(_cardPrefab == null ? "NULL" : "OK")} anchor1={(_anchor1 == null ? "NULL" : "OK")} anchor2={(_anchor2 == null ? "NULL" : "OK")}");
        if (config == null) return -1;
        if (_cardPrefab == null || _anchor1 == null || _anchor2 == null)
        {
            Debug.LogWarning($"[TacticalCardPresenter] 尚未完成初始化，无法发放战术牌：{config.cardName}");
            return -1;
        }
        if (_cardViews == null) _cardViews = new List<CardController>();

        // 1. 已有同名牌实例 → 数量 +1（含耗尽后重建视图）
        for (int i = 0; i < _instances.Count; i++)
        {
            if (_instances[i] == null || _instances[i].Config != config) continue;

            _instances[i].Quantity++;
            RebuildViewIfNeeded(i, config);
            RefreshQuantityBadge(i);
            Debug.Log($"[TacticalCardPresenter] 获得战术牌 {config.cardName}，数量 x{_instances[i].Quantity}");
            return i;
        }

        // 2. 新类型 → 找空闲锚点槽位（0/1）
        for (int slot = 0; slot < MaxAnchorCount; slot++)
        {
            bool occupied = slot < _instances.Count &&
                            _instances[slot] != null &&
                            !_instances[slot].IsEmpty;
            if (occupied) continue;

            while (_instances.Count <= slot) _instances.Add(null);
            while (_cardViews.Count <= slot) _cardViews.Add(null);

            _instances[slot] = new TacticalCardInstance { Config = config, Quantity = 1 };
            _cardViews[slot] = CreateCardView(slot, config);
            RefreshQuantityBadge(slot);
            Debug.Log($"[TacticalCardPresenter] 获得新战术牌 {config.cardName}（槽位 {slot}）");
            return slot;
        }

        Debug.LogWarning($"[TacticalCardPresenter] 战术牌锚点已满，无法发放 {config.cardName}");
        return -1;
    }

    /// <summary>
    /// 探索奖励发放带飞入表现：在世界坐标（探索地块）对应屏幕位置生成一张飞行卡，
    /// 两段式动画 —— ①从地块位置上升并放大（到达最高点时缩放最大，同步淡入、倾斜归正）→
    /// ②从最高点加速收入战术牌槽位，落地后结算 AddCard 并弹跳槽位卡与数量徽标。
    /// 飞行卡复用卡牌 prefab，但禁用 CardController（避免交互）。
    /// 动画进度由 GameLoop.GameTime 驱动，暂停时整段冻结；连续奖励进入队列，间隔 0.12s。
    /// </summary>
    public void AddCardWithFly(TacticalCardSO config, Vector3 worldStartPos)
    {
        Debug.Log($"[RewardTrace] AddCardWithFly card={(config == null ? "NULL" : config.cardId)} flyBusy={_flyBusy}");
        if (config == null) return;

        if (_flyBusy)
        {
            _pendingFlies.Enqueue((config, worldStartPos));
            return;
        }

        _flyBusy = true;
        PlayFlySequence(config, worldStartPos);
    }

    /// <summary>播放单张飞行卡动画。startOffset 秒后开始（队列间隔，计入 GameTime 实现暂停冻结）。</summary>
    private void PlayFlySequence(TacticalCardSO config, Vector3 worldStartPos, float startOffset = 0f)
    {
        // 预判目标槽位（与 AddCard 同款规则），无法预判或环境不完整时直接结算发放
        int slot = FindTargetSlot(config);
        if (slot < 0 || _cardPrefab == null || _anchor1 == null || _anchor2 == null || Camera.main == null)
        {
            FinishFly(config);
            return;
        }

        Transform targetAnchor = slot == 0 ? _anchor1 : _anchor2;
        if (targetAnchor is not RectTransform anchorRect)
        {
            FinishFly(config);
            return;
        }

        // 实例化飞行卡（复用卡牌 prefab，禁用 CardController；挂在锚点下与拖拽换算同空间）
        GameObject flyGO = _container.InstantiatePrefab(_cardPrefab, targetAnchor);
        var controller = flyGO.GetComponent<CardController>();
        if (controller != null) controller.enabled = false;
        var image = flyGO.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = config.cardSprite;
            image.raycastTarget = false;
        }

        var flyRect = (RectTransform)flyGO.transform;
        flyRect.anchorMin = new Vector2(0.5f, 0.5f);
        flyRect.anchorMax = new Vector2(0.5f, 0.5f);
        flyGO.transform.SetAsLastSibling();

        // 卡牌实际渲染尺寸（本地单位 × 缩放 × Canvas 缩放 → 屏幕像素），用于屏幕边缘安全区限制
        float canvasScale = anchorRect.lossyScale.x;
        if (canvasScale <= 0f) canvasScale = 1f;
        Vector2 cardRenderSize = Vector2.Scale(flyRect.rect.size, (Vector2)_uiConfig.CardSize);
        if (cardRenderSize.x <= 0f || cardRenderSize.y <= 0f)
            cardRenderSize = new Vector2(120f, 170f);
        cardRenderSize *= canvasScale;
        float marginX = cardRenderSize.x * 0.5f + 8f;
        float marginY = cardRenderSize.y * 0.5f + 8f;

        // 世界坐标 → 屏幕坐标 → 锚点本地坐标；限制在安全区内保证整卡可见（起点；落点 = 槽位原点 (0,0)）
        Vector3 screenPos = Camera.main.WorldToScreenPoint(worldStartPos);
        screenPos.x = Mathf.Clamp(screenPos.x, marginX, Screen.width - marginX);
        screenPos.y = Mathf.Clamp(screenPos.y, marginY, Screen.height - marginY);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(anchorRect, screenPos, null, out Vector2 revealLocal))
        {
            Object.Destroy(flyGO);
            FinishFly(config);
            return;
        }

        var displayScale = _uiConfig.CardSize;
        float appearTime = 0.50f;   // 阶段① 上升 + 放大（到达最高点放大到最大）
        float flyTime = 0.25f;      // 阶段② 从最高点加速收入槽位
        float totalTime = appearTime + flyTime;

        float arcBump = Mathf.Min(UIScreenHelper.ReferenceHeight * 0.12f, 120f);
        Vector2 liftPos = revealLocal + new Vector2(0f, arcBump);                 // 阶段①顶点（最高点）
        Vector2 dashControl = liftPos * 0.5f + new Vector2(0f, -arcBump * 0.25f); // 阶段②俯冲弧线控制点

        // 初始状态：出现点、小尺寸、微倾斜、透明
        flyRect.anchoredPosition = revealLocal;
        flyRect.localScale = displayScale * 0.35f;
        flyRect.localRotation = Quaternion.Euler(0f, 0f, -3f);
        var canvasGroup = flyGO.GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = flyGO.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;

        // 进度由 GameLoop.GameTime 驱动（暂停即冻结），阶段内手工采样
        float startGameTime = (_gameLoop != null ? _gameLoop.GameTime : Time.time) + startOffset;
        float elapsed = 0f;

        // flyCompleted 守卫：防止 DOTween 同帧 Update 循环在 driver.Complete() 之后再次触发 OnComplete
        bool flyCompleted = false;

        Tween driver = null;
        driver = DOTween.To(() => 0f, _ => { }, 1f, 10f)
            .SetEase(Ease.Linear)
            .OnUpdate(() =>
            {
                if (flyGO == null) // 飞行卡被意外销毁（场景切换等）：终止并继续队列
                {
                    driver.Kill();
                    NextFlyInQueue();
                    return;
                }

                float current = _gameLoop != null ? _gameLoop.GameTime : Time.time;
                elapsed = current - startGameTime;
                if (elapsed < 0f) return;

                if (elapsed < appearTime)
                {
                    // 阶段① 上升 + 放大同步进行：到达最高点（liftPos）时缩放到最大
                    float p = Mathf.Clamp01(elapsed / appearTime);
                    flyRect.anchoredPosition = Vector2.Lerp(revealLocal, liftPos, EaseOutQuad(p));
                    flyRect.localScale = displayScale * Mathf.Lerp(0.35f, 1f, EaseOutBack(p));
                    flyRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-3f, 0f, p));
                    canvasGroup.alpha = Mathf.Clamp01(p * 5f);
                }
                else
                {
                    // 阶段② 从最高点沿俯冲弧线加速飞向槽位 (0,0)
                    float p = Mathf.Clamp01((elapsed - appearTime) / flyTime);
                    float eased = EaseInCubic(p);
                    flyRect.anchoredPosition = QuadraticBezier(liftPos, dashControl, Vector2.zero, eased);
                    flyRect.localScale = displayScale * Mathf.Lerp(1f, 0.97f, p);
                    flyRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 2f, Mathf.Sin(p * Mathf.PI)));
                    canvasGroup.alpha = 1f;
                }

                if (elapsed >= totalTime)
                {
                    driver.Complete();
                    return; // Complete 后立即退出，防止后续帧重复触发
                }
            })
            .OnComplete(() =>
            {
                if (flyCompleted) return; // 防止 DOTween 在 Complete 后再次触发 OnComplete
                flyCompleted = true;

                flyGO.SetActive(false);
                int landedSlot = AddCard(config);
                if (landedSlot >= 0)
                {
                    PunchSlotView(landedSlot);
                    PunchQuantityBadge(landedSlot);
                }
                Object.Destroy(flyGO);
                NextFlyInQueue();
            });
    }

    /// <summary>播放队列中的下一张飞行卡（间隔 0.12s，通过 startOffset 计入 GameTime，暂停时同样冻结）。</summary>
    private void NextFlyInQueue()
    {
        if (_pendingFlies.Count == 0)
        {
            _flyBusy = false;
            return;
        }

        var next = _pendingFlies.Dequeue();
        PlayFlySequence(next.Config, next.WorldPos, 0.12f);
    }

    /// <summary>无法播放动画时的兜底结算：直接发放并继续队列。</summary>
    private void FinishFly(TacticalCardSO config)
    {
        AddCard(config);
        NextFlyInQueue();
    }

    /// <summary>按 AddCard 同款规则预判发放槽位：同名牌 → 原槽位；新类型 → 空闲槽位；无可用 → -1。</summary>
    private int FindTargetSlot(TacticalCardSO config)
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            if (_instances[i] != null && _instances[i].Config == config) return i;
        }

        for (int slot = 0; slot < MaxAnchorCount; slot++)
        {
            bool occupied = slot < _instances.Count &&
                            _instances[slot] != null &&
                            !_instances[slot].IsEmpty;
            if (!occupied) return slot;
        }

        return -1;
    }

    /// <summary>落地后对槽位卡做一次弹跳放大，增强"入槽"感。</summary>
    private void PunchSlotView(int slotIndex)
    {
        if (slotIndex < 0 || _cardViews == null || slotIndex >= _cardViews.Count || _cardViews[slotIndex] == null) return;
        _cardViews[slotIndex].transform.DOPunchScale(new Vector3(0.2f, 0.2f, 0.2f), 0.3f, 6, 0.5f);
    }

    /// <summary>入槽时数量徽标弹跳一次，强化"数量+1"反馈。</summary>
    private void PunchQuantityBadge(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _quantityTexts.Length || _quantityTexts[slotIndex] == null) return;
        _quantityTexts[slotIndex].transform.DOPunchScale(Vector3.one * 0.35f, 0.3f, 4, 0.5f);
    }

    public bool HandleCardDragEnd(ICardView view, HexCellData targetCell, Vector3 releaseWorldPos)
    {
        if (targetCell == null || view == null) return false;

        // 通过 CardData.ID 定位实例（负数 ID = -(index+1)）
        int placementId = view.PlacementID;
        if (placementId < 0 || placementId >= _instances.Count) return false;

        var instance = _instances[placementId];
        if (instance == null || instance.IsEmpty) return false;

        var config = instance.Config;
        if (config == null) return false;

        // 按效果类型分发释放逻辑（Repair 为落点及其周围一环内的己方建筑回血）
        switch (GetEffectType(config))
        {
            case TacticalEffectType.Repair:
                TryExecuteRepair(targetCell, config);
                break;

            case TacticalEffectType.BattleOrder:
                if (TryExecuteBattleOrder(targetCell, config)) break;
                ReturnBorrowedCard(placementId);
                view.ResetToOrigin();
                return false;

            default:
                Debug.LogWarning($"[TacticalCardPresenter] Unknown effect type: {config.effectType}");
                ReturnBorrowedCard(placementId);
                view.ResetToOrigin();
                return false;
        }

        if (_borrowedSlot == placementId)
        {
            // 叠放借出场景：数量已在拖拽开始时扣减（xN → xN-1），被拖的"那张"随使用消耗，不飞回槽位
            Debug.Log($"[TacticalCardPresenter] Used {config.cardName}, remaining: {instance.Quantity}");
            _borrowedSlot = -1;
            DestroyGhost();
        }
        else
        {
            // 单张场景：现状逻辑
            instance.Quantity--;
            Debug.Log($"[TacticalCardPresenter] Used {config.cardName}, remaining: {instance.Quantity}");

            if (instance.IsEmpty)
            {
                // 耗尽：销毁对应的 card GameObject
                if (placementId < _cardViews?.Count && _cardViews[placementId] != null)
                {
                    Object.Destroy(_cardViews[placementId].gameObject);
                    _cardViews[placementId] = null;
                }
            }
        }

        RefreshQuantityBadge(placementId);

        return true;
    }

    /// <summary>
    /// 拖拽开始：叠放（x≥2）时"借出"1 张 —— 数量 -1、本体留在槽位显示剩余、
    /// 拖拽视觉由新建的幽灵卡承担（复用卡牌 prefab，禁用交互）。
    /// </summary>
    public void OnCardDragBegin(ICardView view)
    {
        if (view == null || _borrowedSlot >= 0) return;

        int slot = view.PlacementID;
        if (slot < 0 || slot >= _instances.Count) return;

        var instance = _instances[slot];
        if (instance == null || instance.IsEmpty || instance.Config == null) return;
        if (instance.Quantity < 2) return; // 单张保持现状：直接拖本体

        // 1. 先创建幽灵，环境不完整则放弃借出（保持现状直接拖本体）
        if (_cardPrefab == null || _anchor1 == null || _anchor2 == null) return;

        Transform targetAnchor = slot == 0 ? _anchor1 : _anchor2;

        // 幽灵挂在锚点下（与槽位卡同一空间），起点即槽位位置；拖拽 localPoint 也相对锚点换算，坐标天然一致
        GameObject ghostGO = _container.InstantiatePrefab(_cardPrefab, targetAnchor);
        var controller = ghostGO.GetComponent<CardController>();
        if (controller != null) controller.enabled = false;
        var image = ghostGO.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = instance.Config.cardSprite;
            image.raycastTarget = false;
        }

        var ghostRect = (RectTransform)ghostGO.transform;
        ghostRect.anchorMin = new Vector2(0.5f, 0.5f);
        ghostRect.anchorMax = new Vector2(0.5f, 0.5f);
        ghostRect.anchoredPosition = Vector2.zero;
        ghostRect.localScale = _uiConfig.CardSize;
        ghostGO.transform.SetAsLastSibling();

        // 2. 借出：数量 -1，本体留在槽位，拖拽移动/缩放交给幽灵
        _activeGhost = ghostGO;
        _borrowedView = view;
        _borrowedSlot = slot;
        instance.Quantity--;
        view.SetDragProxy(ghostRect);
        view.ResetToOrigin(); // 本体可能因悬停上浮，借出后立即回落槽位（_isDragging 会屏蔽 OnPointerExit）
        RefreshQuantityBadge(slot);
    }

    /// <summary>拖拽取消（失焦/暂停/落空/释放失败）：归还借出并销毁幽灵。</summary>
    public void OnCardDragCancel(ICardView view)
    {
        if (view == null) return;
        ReturnBorrowedCard(view.PlacementID);
    }

    /// <summary>ICardDropHandler：战术卡任意有效地图格都可部署（放置预览恒高亮）。</summary>
    public bool CanDeployTo(CardData data, HexCellData cell)
    {
        return cell != null;
    }

    /// <summary>
    /// 读取指定战术卡（以 CardData.ID 定位槽位）的效果半径 effectRadius。
    /// 仅 Excel 数值（阶段6 唯一主源）。非战术卡 / 未命中 / 数值库缺失返回 0。
    /// 供影响范围遮罩读取与结算同一份半径（R1：遮罩画的 = 打出来的）。
    /// </summary>
    public int GetEffectRadius(CardData data)
    {
        if (data == null || _balance == null) return 0;

        int slot = -data.ID - 1; // 战术卡 CardData.ID = -(slotIndex+1)
        if (slot < 0 || slot >= _instances.Count) return 0;
        TacticalCardInstance instance = _instances[slot];
        if (instance == null || instance.IsEmpty || instance.Config == null) return 0;

        return _balance.TryGetCard(instance.Config.cardId, out TacticalCardBalanceData b)
            ? b.effectRadius
            : 0;
    }

    /// <summary>归还借出卡：数量 +1、销毁幽灵、恢复槽位徽标。</summary>
    private void ReturnBorrowedCard(int slot)
    {
        if (_borrowedSlot != slot) return;

        if (slot >= 0 && slot < _instances.Count && _instances[slot] != null)
        {
            _instances[slot].Quantity++;
            RefreshQuantityBadge(slot);
        }

        DestroyGhost();
        _borrowedSlot = -1;
    }

    /// <summary>销毁幽灵卡并解除本体的拖拽代理。</summary>
    private void DestroyGhost()
    {
        if (_borrowedView != null)
        {
            _borrowedView.SetDragProxy(null);
            _borrowedView = null;
        }

        if (_activeGhost != null)
        {
            Object.Destroy(_activeGhost);
            _activeGhost = null;
        }
    }

    /// <summary>在指定锚点槽位创建一张战术牌视图（slotIndex: 0=锚点1, 1=锚点2）。</summary>
    private CardController CreateCardView(int slotIndex, TacticalCardSO config)
    {
        Transform parent = slotIndex == 0 ? _anchor1 : _anchor2;
        GameObject cardGO = _container.InstantiatePrefab(_cardPrefab, parent);
        RectTransform cardRect = (RectTransform)cardGO.transform;
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = Vector2.zero;
        cardGO.transform.localScale = _uiConfig.CardSize;

        var cardController = cardGO.GetComponent<CardController>();
        if (cardController != null)
        {
            cardController.OverrideDropHandler(this);
            cardController.SetData(new CardData { ID = -(slotIndex + 1), CardSprite = config.cardSprite, IsUnit = false },
                slotIndex, Vector2.zero);
        }

        return cardController;
    }

    /// <summary>实例存在但视图为空（已耗尽销毁）时重建视图。</summary>
    private void RebuildViewIfNeeded(int slotIndex, TacticalCardSO config)
    {
        while (_cardViews.Count <= slotIndex) _cardViews.Add(null);
        if (_cardViews[slotIndex] != null) return;

        _cardViews[slotIndex] = CreateCardView(slotIndex, config);
    }

    /// <summary>刷新指定槽位的数量文本：数量 ≥1 显示 "xN"，0（已耗尽）隐藏。</summary>
    private void RefreshQuantityBadge(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _quantityTexts.Length || _quantityTexts[slotIndex] == null) return;

        int quantity = slotIndex < _instances.Count && _instances[slotIndex] != null
            ? _instances[slotIndex].Quantity
            : 0;

        _quantityTexts[slotIndex].text = $"x{quantity}";
        _quantityTexts[slotIndex].gameObject.SetActive(quantity >= 1);
    }

    private void TryExecuteRepair(HexCellData cell, TacticalCardSO config)
    {
        TacticalCardEffect effect = GetEffect(config);
        // 【群体回血】范围：落点格及其 n 环（effectRadius）内的己方建筑与单位。
        int radius = Mathf.Max(0, effect.effectRadius);
        List<BuildingBase> ownBuildings = FindOwnBuildingsInRange(cell, radius);
        List<CharacterData> ownUnits = FindOwnUnitsInRange(cell, radius);
        if (ownBuildings.Count == 0 && ownUnits.Count == 0)
        {
            Debug.Log("[TacticalCardPresenter] Repair: no own building or unit found in drop cell and its ring, card consumed without effect.");
            return;
        }

        float healRatio = effect.healRatio;
        float unitHealRatio = effect.unitHealRatio > 0f ? effect.unitHealRatio : effect.healRatio;
        int healedCount = 0;

        foreach (BuildingBase building in ownBuildings)
        {
            if (building == null || building.buildingData == null) continue;

            float maxHp = building.buildingData.hp;
            float currentHp = building.buildingData.currentHp;
            if (currentHp >= maxHp) continue; // 满血建筑跳过

            float healAmount = maxHp * healRatio;
            building.buildingData.currentHp = Mathf.Min(currentHp + healAmount, maxHp);
            building.SyncHealthBar();
            healedCount++;
        }

        foreach (CharacterData unit in ownUnits)
        {
            if (unit == null || unit.unitData == null) continue;
            if (unit.currentHp >= unit.unitData.hp) continue; // 满血单位跳过

            float healAmount = unit.unitData.hp * unitHealRatio;
            unit.Heal(healAmount);
            healedCount++;
        }

        Debug.Log($"[TacticalCardPresenter] Repair: group-healed {healedCount} own target(s) in drop ring (building {healRatio * 100:F0}%, unit {unitHealRatio * 100:F0}%).");
    }

    private bool TryExecuteBattleOrder(HexCellData cell, TacticalCardSO config)
    {
        var effect = GetEffect(config);

        // 作用范围：落点及其 n 环（effectRadius）内的己方单位（与「群体回血」一致）
        int radius = Mathf.Max(0, effect.effectRadius);
        List<CharacterData> targets = FindOwnUnitsInRange(cell, radius);
        if (targets.Count == 0)
        {
            Debug.Log("[TacticalCardPresenter] BattleOrder: no own unit found in drop cell and its ring, card consumed without effect.");
            return true; // 卡仍消耗（与 Repair 行为一致）
        }

        _battleOrderBuff.Apply(targets, effect.attackMultiplier, effect.speedMultiplier, effect.duration);
        Debug.Log($"[TacticalCardPresenter] BattleOrder: buffed {targets.Count} own unit(s) +{(effect.attackMultiplier - 1f) * 100:F0}% ATK, +{(effect.speedMultiplier - 1f) * 100:F0}% SPD for {effect.duration}s.");
        return true;
    }

    /// <summary>按 cardId 在资源库中查找战术卡资源对象（图标等人工引用）。</summary>
    private TacticalCardSO FindResource(string cardId)
    {
        if (_database == null || _database.cards == null) return null;
        foreach (var card in _database.cards)
            if (card != null && card.cardId == cardId) return card;
        return null;
    }

    private TacticalCardBalanceData RequireBalance(TacticalCardSO config)
    {
        if (_balance == null)
            throw new System.InvalidOperationException(
                "[TacticalCard] Excel 战术卡数值库未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 TacticalCardBalanceDatabaseSO。");
        if (config == null)
            throw new System.InvalidOperationException("[TacticalCard] 战术卡配置为 null，无法读取效果数值。");
        if (!_balance.TryGetCard(config.cardId, out var b))
            throw new System.InvalidOperationException(
                $"[TacticalCard] 战术卡 {config.cardId} 未在 Excel 战术卡数值库命中，无法读取效果数值。");
        return b;
    }

    /// <summary>效果类型：仅 Excel 数值（阶段6 唯一主源）。</summary>
    private TacticalEffectType GetEffectType(TacticalCardSO config)
    {
        return ParseEffectType(RequireBalance(config).effectType);
    }

    /// <summary>效果参数：仅 Excel 数值（阶段6 唯一主源）。</summary>
    private TacticalCardEffect GetEffect(TacticalCardSO config)
    {
        var b = RequireBalance(config);
        return new TacticalCardEffect
        {
            healRatio = b.healRatio,
            unitHealRatio = b.unitHealRatio,
            attackMultiplier = b.attackMultiplier,
            speedMultiplier = b.speedMultiplier,
            duration = b.duration,
            effectRadius = b.effectRadius,
        };
    }

    private static TacticalEffectType ParseEffectType(string s)
    {
        return s == "BattleOrder" ? TacticalEffectType.BattleOrder : TacticalEffectType.Repair;
    }

    /// <summary>
    /// 收集落点格及其 n 环内的己方建筑（去重）。「哪些格」这一层由 HexRange.CollectInRange
    /// 统一枚举，与影响范围遮罩读同一份 effectRadius、调同一个枚举函数（R1 唯一真源）。
    /// </summary>
    private List<BuildingBase> FindOwnBuildingsInRange(HexCellData cell, int radius)
    {
        List<BuildingBase> result = new List<BuildingBase>();
        if (cell == null) return result;

        var cells = new List<HexCellData>();
        HexRange.CollectInRange(_mapDataService, cell, radius, cells);
        for (int i = 0; i < cells.Count; i++)
            AddOwnBuildingOnCell(cells[i], result);
        return result;
    }

    /// <summary>若该格上有己方建筑，加入结果（避免多格建筑重复入列）。</summary>
    private void AddOwnBuildingOnCell(HexCellData cell, List<BuildingBase> result)
    {
        if (cell == null) return;
        var entry = cell.BulidingTypeOnHex_Building;
        if (entry.Key == Enums.BulidingType.NoBuilding || entry.Value == null) return;
        if (!entry.Value.CompareTag("PlayerBuilding")) return;

        BuildingBase building = entry.Value.GetComponent<BuildingBase>();
        if (building != null && !building.IsDestroyed && !result.Contains(building))
            result.Add(building);
    }

    /// <summary>收集落点格及其 n 环内的己方单位（去重）。n 环枚举与遮罩共用 HexRange.CollectInRange。</summary>
    private List<CharacterData> FindOwnUnitsInRange(HexCellData cell, int radius)
    {
        List<CharacterData> result = new List<CharacterData>();
        if (cell == null) return result;

        var cells = new List<HexCellData>();
        HexRange.CollectInRange(_mapDataService, cell, radius, cells);
        for (int i = 0; i < cells.Count; i++)
            AddOwnUnitOnCell(cells[i], result);
        return result;
    }

    /// <summary>若该格上有己方单位，加入结果（跳过死亡中的单位，去重）。</summary>
    private void AddOwnUnitOnCell(HexCellData cell, List<CharacterData> result)
    {
        if (cell == null) return;

        // 【多单位落点】枚举格内全部站位单位。
        foreach (GameObject unit in cell.GetStandingUnits())
        {
            if (unit == null) continue;

            var controller = unit.GetComponent<UnitMovementController>();
            if (controller == null || controller.characterData == null) continue;
            if (controller.PlayerIndex != 0) continue;              // 仅己方（玩家）单位
            if (controller.IsDeathScheduled) continue;              // 死亡流程中的单位跳过
            if (controller.characterData.currentHp <= 0) continue;  // 已阵亡单位跳过

            if (!result.Contains(controller.characterData))
                result.Add(controller.characterData);
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
}
