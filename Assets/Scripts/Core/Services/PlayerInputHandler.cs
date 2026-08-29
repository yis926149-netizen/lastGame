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

    private bool _isDraggingCard;
    private CardData _draggingCardData;
    private ICardDropHandler _draggingDropHandler;
    private HexCellData _lastDraggingHighlightCell;
    private bool _explorationPointerDown;
    private Vector3 _explorationPointerStart;
    private float _explorationPointerDownTime;
    private const float ExplorationTapMaxDistance = 20f;
    private const float ExplorationTapMaxDuration = 0.25f;

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
        _logisticsService = logisticsService;
    }

    public void Tick()
    {
        HandleCardDragging();
        HandleTileClickForExploration();
    }

    // ---------- 卡牌拖拽高亮 ----------
    private void HandleCardDragging()
    {
        if (_gameLoop != null && _gameLoop.IsPaused)
        {
            CancelCardDragging();
            return;
        }

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
    }

    private void CancelCardDragging() => ClearCardDragHighlight();

    private void HighlightGridOnMouseHover()
    {
        Vector2 dragLogicPosition = CardController.GetCardDragLogicPosition(
            _input.MousePosition);

        if (_mapRaycastService.RaycastMap(dragLogicPosition, out RaycastHit hit))
        {
            var cell = _mapData.GetCellByWorldPosition(hit.point);
            if (cell != null && cell != _lastDraggingHighlightCell)
            {
                if (CanHighlightCellForCard(cell))
                {
                    _hexHighlightRenderer.SetHighlightedCells(HexHighlightChannel.CardPlacement, new[] { cell }, Color.yellow);
                    _lastDraggingHighlightCell = cell;
                }
                else
                {
                    _hexHighlightRenderer.ClearChannel(HexHighlightChannel.CardPlacement);
                    _lastDraggingHighlightCell = null;
                }
            }
        }
        else
        {
            _hexHighlightRenderer.ClearChannel(HexHighlightChannel.CardPlacement);
            _lastDraggingHighlightCell = null;
        }
    }

    /// <summary>
    /// 放置预览资格 = 该卡牌能否部署到该格（与确认路径共用同一规则）：
    /// 普通卡 → CardPresenter.CanDeployTo（内部含地形/归属/后勤等，归属即隐含“已探索”）；
    /// 战术卡 → TacticalCardPresenter.CanDeployTo（任意有效地图格均可）。
    /// </summary>
    private bool CanHighlightCellForCard(HexCellData cell)
    {
        if (cell == null || _draggingDropHandler == null) return false;
        return _draggingDropHandler.CanDeployTo(_draggingCardData, cell);
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
            _explorationPointerDown = !IsPointerOverBlockingUI();
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
