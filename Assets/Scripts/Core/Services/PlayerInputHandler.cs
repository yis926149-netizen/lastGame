using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

//****************************************
// 功能说明：玩家输入处理器。
//   【批次 B】移除单位选择/移动/攻击逻辑（约 400 行）。
//   保留：拖牌高亮、UI 阻挡检测辅助方法。
//   【2026-08-05 评审清理】G 键切格网已移除：格网对象写入方（旧 MapRenderer）删除后
//   gridGameObject 恒 null，分支不可达。
//   ForceDeselectUnit / ClearCardDragHighlight 保留为空桩（外部调用点不删）。
//
// 卡牌拖拽可在任何时间使用（移除 CurrentPhase is not PlayerPhase 门控）。
//****************************************

public class PlayerInputHandler : ITickable, System.IDisposable
{
    private readonly IInputService _input;
    private readonly IMapDataService _mapData;
    private readonly IUIConfigProvider _uiConfig;
    private readonly IUnitRepository _unitRepository;
    private readonly Canvas _targetUICanvas;
    private readonly IExplorationService _explorationService;
    private readonly GameLoop _gameLoop;
    private readonly ILogisticsService _logisticsService;
    private readonly IMapRaycastService _mapRaycastService;
    private readonly CameraController _cameraController;
    private readonly CardDragTargetMarkerController _targetMarker;

    private bool _isDraggingCard;
    private CardData _draggingCardData;
    private ICardDropHandler _draggingDropHandler;
    private HexCellData _lastDraggingHighlightCell;
    private bool _explorationPointerDown;
    private Vector3 _explorationPointerStart;
    private float _explorationPointerDownTime;
    private const float ExplorationTapMaxDistance = 20f;
    private const float ExplorationTapMaxDuration = 0.25f;

    /// <summary>本帧的按下是否被用于"收起提起态卡牌"。
    /// 该次点击只用于收起，不得再触发探索等游戏行为（探索消耗金币且不可撤销）。</summary>
    private bool _dismissedRaisedThisFrame;

    /// <summary>已计算提起态放置范围的那张卡（快照去重）。无提起卡时为 null。</summary>
    private CardController _lastRaisedPreviewCard;

    /// <summary>提起态放置范围（纯逻辑快照）：可放置格与不可放置格。无提起卡时为空。</summary>
    private readonly List<HexCellData> _raisedPlaceableCells = new List<HexCellData>();
    private readonly List<HexCellData> _raisedUnplaceableCells = new List<HexCellData>();

    [Inject] private HexHighlightRenderer _hexHighlightRenderer;

    [Inject]
    public PlayerInputHandler(
        IInputService input,
        IMapDataService mapData,
        IUIConfigProvider uiConfig,
        IUnitRepository unitRepository,
        [Inject(Id = "TargetUICanvas")] Canvas targetUICanvas,
        IExplorationService explorationService,
        GameLoop gameLoop,
        IMapRaycastService mapRaycastService,
        CameraController cameraController,
        CardDragTargetMarkerController targetMarker,
        [InjectOptional] ILogisticsService logisticsService
    )
    {
        _input = input;
        _mapData = mapData;
        _uiConfig = uiConfig;
        _unitRepository = unitRepository;
        _targetUICanvas = targetUICanvas;
        _explorationService = explorationService;
        _gameLoop = gameLoop;
        _mapRaycastService = mapRaycastService;
        _cameraController = cameraController;
        _targetMarker = targetMarker;
        _logisticsService = logisticsService;
    }

    public void Tick()
    {
        _dismissedRaisedThisFrame = false;
        HandleCardDragging();
        HandleRaisedCardDismiss();
        HandleRaisedCardPlacementPreview();
        HandleTileClickForExploration();
    }

    /// <summary>
    /// 【提起态】单击路线的退出入口：点击卡牌之外的任意位置 → 落下。
    /// 常规 PC 下退出由 CardController.OnPointerExit 负责，本方法对鼠标同样生效但无副作用
    /// （鼠标点卡外时指针早已离开卡面，提起态已由 Exit 清空，ActiveRaisedCard 为 null 直接返回）；
    /// 卡牌上的调试开关 _forceClickModeOnPC 打开后，PC 的退出就完全依赖本方法。
    ///
    /// 采用轮询而非全屏透明遮罩：遮罩会吞掉这一次点击，玩家"点地图放下卡"需点两次。
    /// </summary>
    private void HandleRaisedCardDismiss()
    {
        CardController raised = CardController.ActiveRaisedCard;
        if (raised == null) return;
        if (!_input.GetMouseButtonDown(0)) return;

        // 点在该卡自身上 → 交给 OnPointerClick 做 toggle，此处不介入，避免双重处理。
        if (GetCardUnderMouse() == raised) return;

        raised.LowerCard();
        _dismissedRaisedThisFrame = true;
    }

    // ---------- 卡牌拖拽高亮 ----------
    private void HandleCardDragging()
    {
        if (_input.GetMouseButtonDown(0))
        {
            CardController controller = GetCardUnderMouse();
            if (controller != null && controller.DropHandler != null)
            {
                _isDraggingCard = true;
                _draggingCardData = controller.Data;
                _draggingDropHandler = controller.DropHandler;
            }
        }
        if (_input.GetMouseButtonUp(0))
            CancelCardDragging();
        if (_isDraggingCard) HighlightGridOnMouseHover();
    }

    public void ClearCardDragHighlight()
    {
        _isDraggingCard = false;
        _draggingCardData = null;
        _draggingDropHandler = null;
        _hexHighlightRenderer.ClearChannel(HexHighlightChannel.CardPlacement);
        _lastDraggingHighlightCell = null;
        // 落点图标与连线计划 §5.1：图标与高亮共存亡；本方法覆盖松手 / 暂停 /
        // CardController.ClearHighlights 全部收尾路径，是唯一需要挂的清理点。
        _targetMarker.Clear();
    }

    private void CancelCardDragging() => ClearCardDragHighlight();

    /// <summary>
    /// 【UI-1.0】每帧先做一次地图射线，再算出“本帧的悬停格”（可能为 null）：
    /// 图标跟随射线命中点（SetTarget(isMapHit, hit.point)），与格高亮解耦；
    /// _lastDraggingHighlightCell 缓存只保留给昂贵的 SetHighlightedCells 网格重建，不再控制图标。
    /// 【方案甲】颜色由可放置性决定：可放置 = 金黄，不可放置 = 柔和红（合并“能否放置 + 哪格”语义）。
    /// 因此缓存键从“可放置格”改为“悬停格”——命中地图但不可放置的格也要给红光反馈，而非清空。
    /// 山格由 HexHighlightRenderer 的 IsBlockedByMountainGate 门禁过滤（不显示任何高亮，语义不变）。
    /// </summary>
    private void HighlightGridOnMouseHover()
    {
        Vector2 dragLogicPosition = CardController.GetCardDragLogicPosition(
            _input.MousePosition);

        HexCellData hoveredCell = null;
        bool isMapHit = _mapRaycastService.RaycastMap(dragLogicPosition, out RaycastHit hit,
                CardController.CardDragRaycastMaxDistance);          // ← §3.2 统一射程
        if (isMapHit)
        {
            hoveredCell = _mapData.GetCellByWorldPosition(hit.point);
        }

        // 图标：射线命中地形即显示（跟随 hit.point），不受「是否可放置」约束；连线随图标共存亡。
        _targetMarker.SetTarget(isMapHit, hit.point);

        // 高亮网格：维持「变化才重建」的缓存语义；颜色由可放置性决定（金/红）。
        if (hoveredCell != _lastDraggingHighlightCell)
        {
            if (hoveredCell != null)
            {
                Color highlightColor = CanHighlightCellForCard(hoveredCell)
                    ? HexHighlightRenderer.PlaceableGlowColor
                    : HexHighlightRenderer.UnplaceableGlowColor;
                _hexHighlightRenderer.SetHighlightedCells(
                    HexHighlightChannel.CardPlacement, new[] { hoveredCell }, highlightColor);
            }
            else
            {
                _hexHighlightRenderer.ClearChannel(HexHighlightChannel.CardPlacement);
            }
            _lastDraggingHighlightCell = hoveredCell;
        }
    }

    /// <summary>
    /// 统一"该卡能否部署到该格"资格查询（拖牌预览与提起态预览共用同一入口）：
    /// 普通卡 → CardPresenter.CanDeployTo（内部含地形/归属/后勤等，归属即隐含“已探索”）；
    /// 战术卡 → TacticalCardPresenter.CanDeployTo（任意有效地图格均可）。
    /// </summary>
    private static bool CanDeployToCell(ICardDropHandler dropHandler, CardData cardData, HexCellData cell)
    {
        return cell != null && dropHandler != null && dropHandler.CanDeployTo(cardData, cell);
    }

    /// <summary>拖牌悬停格的放置预览资格（复用 CanDeployToCell 统一规则）。</summary>
    private bool CanHighlightCellForCard(HexCellData cell)
    {
        return CanDeployToCell(_draggingDropHandler, _draggingCardData, cell);
    }

    // ---------- 拖拽态触点数据（纯提取，供战术卡影响范围遮罩读取） ----------

    /// <summary>
    /// 拖拽态下触点当前指向的地块（无命中 / 非拖拽态为 null）。纯数据，不产生任何视觉。
    /// 复用 HighlightGridOnMouseHover 每帧维护的缓存，与落点图标 / 单格高亮同源同帧。
    /// </summary>
    public HexCellData DraggingHoveredCell => _isDraggingCard ? _lastDraggingHighlightCell : null;

    /// <summary>
    /// 拖拽态下当前被拖卡牌的 DropHandler（非拖拽态为 null）。用于判断「拖的是不是战术卡」
    /// 并复用 ICardDropHandler.CanDeployTo 做合法性判定（与拖牌高亮同一入口）。
    /// </summary>
    public ICardDropHandler DraggingDropHandler => _isDraggingCard ? _draggingDropHandler : null;

    /// <summary>
    /// 拖拽态下当前被拖卡牌的 CardData（非拖拽态为 null）。与 DraggingDropHandler 同源同帧。
    /// </summary>
    public CardData DraggingCardData => _isDraggingCard ? _draggingCardData : null;

    // ---------- 提起态放置范围（纯逻辑，无任何视觉） ----------

    /// <summary>提起态放置范围快照（可放置格）。无提起卡或拖拽中为空；供后续视觉表达方案读取。</summary>
    public IReadOnlyList<HexCellData> RaisedPlaceableCells => _raisedPlaceableCells;

    /// <summary>提起态放置范围快照（不可放置格）。无提起卡或拖拽中为空；供后续视觉表达方案读取。</summary>
    public IReadOnlyList<HexCellData> RaisedUnplaceableCells => _raisedUnplaceableCells;

    /// <summary>
    /// 【提起态放置范围·纯逻辑】卡牌提起时，把全图格按"该卡能否部署"分成可放置 / 不可放置两批，
    /// 结果缓存到 RaisedPlaceableCells / RaisedUnplaceableCells，不产生任何视觉。
    /// 与拖牌预览共用同一资格入口（CanDeployToCell → ICardDropHandler.CanDeployTo）。
    /// 快照语义：只在"提起的卡发生变化"时重算；拖拽期间清空（提起态已让位于拖牌）。
    /// </summary>
    private void HandleRaisedCardPlacementPreview()
    {
        CardController raised = CardController.ActiveRaisedCard;

        if (raised == null || raised.Data == null || raised.DropHandler == null || _isDraggingCard)
        {
            ClearRaisedCardPlacementPreview();
            return;
        }

        if (raised == _lastRaisedPreviewCard) return;   // 同一张卡已计算，快照不变

        _lastRaisedPreviewCard = raised;

        List<HexCellData> allCells = _mapData.GetAllCells();
        if (allCells == null)
        {
            ClearRaisedCardPlacementPreview();
            return;
        }

        _raisedPlaceableCells.Clear();
        _raisedUnplaceableCells.Clear();
        foreach (HexCellData cell in allCells)
        {
            if (cell == null) continue;
            if (CanDeployToCell(raised.DropHandler, raised.Data, cell)) _raisedPlaceableCells.Add(cell);
            else _raisedUnplaceableCells.Add(cell);
        }
    }

    private void ClearRaisedCardPlacementPreview()
    {
        _lastRaisedPreviewCard = null;
        _raisedPlaceableCells.Clear();
        _raisedUnplaceableCells.Clear();
    }

    // ---------- 空桩（外部调用点保留引用，不删调用方）----------
    /// <summary>【批次 B】单位选择已移除，保留空桩兼容 GameStateMachine / UIController 调用。</summary>
    public void ForceDeselectUnit() { }

    // ---------- UI 阻挡检测辅助方法 ----------

    /// <summary>指针是否落在"会阻挡地图/单位交互"的 UI 上。</summary>
    private bool IsPointerOverBlockingUI()
    {
        if (!_input.IsPointerOverUI(null)) return false;

        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        if (eventSystem == null) return true;

        var pointerData = new UnityEngine.EventSystems.PointerEventData(eventSystem)
        {
            position = _input.MousePosition
        };
        var results = new List<UnityEngine.EventSystems.RaycastResult>();
        eventSystem.RaycastAll(pointerData, results);
        if (results.Count == 0) return true;

        foreach (var result in results)
        {
            if (result.gameObject == null) continue;
            if (IsWithinRuntimeUnitCanvas(result.gameObject.transform)) continue;
            if (IsBlockingUIObject(result.gameObject))
                return true;
        }

        return false;
    }

    private bool IsBlockingUIObject(GameObject gameObject)
    {
        float effectiveAlpha = 1f;
        Transform current = gameObject.transform;

        while (current != null)
        {
            var canvasGroup = current.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                if (!canvasGroup.blocksRaycasts) return false;
                effectiveAlpha *= canvasGroup.alpha;
            }

            current = current.parent;
        }

        if (effectiveAlpha <= 0.01f) return false;

        current = gameObject.transform;
        while (current != null)
        {

            var selectable = current.GetComponent<Selectable>();
            if (selectable != null && selectable.enabled && selectable.interactable)
                return true;

            var cardController = current.GetComponent<CardController>();
            if (cardController != null && cardController.enabled)
                return true;

            current = current.parent;
        }

        var graphic = gameObject.GetComponent<Graphic>();
        return graphic != null && graphic.raycastTarget && effectiveAlpha * graphic.color.a > 0.01f;
    }

    private bool IsWithinRuntimeUnitCanvas(Transform uiTransform)
    {
        var runtimeCanvases = _uiConfig?.RuntimeCanvases;
        if (runtimeCanvases == null || runtimeCanvases.Count == 0) return false;

        while (uiTransform != null)
        {
            for (int i = 0; i < runtimeCanvases.Count; i++)
            {
                var canvas = runtimeCanvases[i];
                if (canvas != null && canvas.transform == uiTransform)
                    return true;
            }
            uiTransform = uiTransform.parent;
        }
        return false;
    }

    private CardController GetCardUnderMouse()
    {
        if (!_input.IsPointerOverUI(_targetUICanvas)) return null;

        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        if (eventSystem == null) return null;

        var pointerData = new UnityEngine.EventSystems.PointerEventData(eventSystem)
        {
            position = _input.MousePosition
        };
        var results = new List<UnityEngine.EventSystems.RaycastResult>();
        eventSystem.RaycastAll(pointerData, results);
        foreach (var result in results)
        {
            CardController controller = result.gameObject.GetComponentInParent<CardController>();
            if (controller != null) return controller;
        }
        return null;
    }

    public void Dispose()
    {
        // 单位移除事件监听已移除（OnUnitRemoved 不再需要）
    }

    // ---------- 探索：点击未探索格触发探索 ----------
    private void HandleTileClickForExploration()
    {
        if (_gameLoop != null && _gameLoop.IsPaused) return;
        if (_isDraggingCard)
        {
            _explorationPointerDown = false;
            return;
        }

        if (_input.IsMultiTouch)
        {
            _explorationPointerDown = false;
            return;
        }

        if (_input.GetMouseButtonDown(0))
        {
            // 为收起提起态卡牌而点的这一下，不参与探索判定：
            // 否则点在"未探索且相邻己方"的地块上会顺带触发一次非预期探索（消耗金币且不可撤销）。
            _explorationPointerDown = !IsPointerOverBlockingUI() && !_dismissedRaisedThisFrame;
            _explorationPointerStart = _input.MousePosition;
            _explorationPointerDownTime = Time.unscaledTime;
        }

        if (!_input.GetMouseButtonUp(0)) return;
        if (!_explorationPointerDown)
        {
            _explorationPointerDown = false;
            return;
        }

        Vector3 releasePosition = _input.MousePosition;
        bool isTap = (releasePosition - _explorationPointerStart).sqrMagnitude <= ExplorationTapMaxDistance * ExplorationTapMaxDistance
            && Time.unscaledTime - _explorationPointerDownTime <= ExplorationTapMaxDuration;
        _explorationPointerDown = false;
        if (!isTap) return;

        if (!_mapRaycastService.RaycastMap(
                releasePosition,
                _cameraController.TargetCameraPosition,
                out RaycastHit hit))
            return;

        var cell = _mapData.GetCellByWorldPosition(hit.point);
        if (cell == null || cell.IsExplored || !HasExploredPlayerNeighbor(cell)) return;

        var result = _explorationService.TryExplore(cell, 0);
        if (result != ExploreResult.Success)
        {
            Debug.Log($"[探索] 地块 {cell.HexCoordinate} 探索失败: {result}");
        }
    }

    /// <summary>
    /// 与 CostLabelRenderer.HasExploredNeighbor(playerIndex:0) 条件完全一致：
    /// 邻接至少一个已探索且归属玩家(index=0)的格子。
    /// </summary>
    private bool HasExploredPlayerNeighbor(HexCellData cell)
    {
        for (int i = 0; i < 6; i++)
        {
            var n = _mapData.GetNeighbor(cell, (Enums.HexDirection)i);
            if (n == null || n.Player_City_Index.Key != 0) continue;
            if (_logisticsService == null || _logisticsService.IsLogisticsConnected(n, 0))
                return true;
        }
        return false;
    }
}
