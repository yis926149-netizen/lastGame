using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

//****************************************
// 功能说明：玩家输入处理器。
//   【批次 B】移除单位选择/移动/攻击逻辑（约 400 行）。
//   保留：G 键切格网、拖牌高亮、UI 阻挡检测辅助方法。
//   ForceDeselectUnit / ClearCardDragHighlight 保留为空桩（外部调用点不删）。
//
// 卡牌拖拽可在任何时间使用（移除 CurrentPhase is not PlayerPhase 门控）。
//****************************************

public class PlayerInputHandler : ITickable, System.IDisposable
{
    private readonly IInputService _input;
    private readonly IMapDataService _mapData;
    private readonly IUIConfigProvider _uiConfig;
    private readonly MapGenerator _mapGenerator;
    private readonly IUnitRepository _unitRepository;
    private readonly Canvas _targetUICanvas;
    private readonly IExplorationService _explorationService;

    private bool _isDraggingCard;
    private HexCellData _lastDraggingHighlightCell;
    private bool _lastDraggingGridWasActive;

    [Inject]
    public PlayerInputHandler(
        IInputService input,
        IMapDataService mapData,
        IUIConfigProvider uiConfig,
        IUnitRepository unitRepository,
        [Inject(Id = "TargetUICanvas")] Canvas targetUICanvas,
        MapGenerator mapGenerator,
        IExplorationService explorationService
    )
    {
        _input = input;
        _mapData = mapData;
        _uiConfig = uiConfig;
        _unitRepository = unitRepository;
        _targetUICanvas = targetUICanvas;
        _mapGenerator = mapGenerator;
        _explorationService = explorationService;
    }

    public void Tick()
    {
        HandleGlobalInput();
        HandleCardDragging();
        HandleTileClickForExploration();
    }

    // ---------- G 键切格网 ----------
    private void HandleGlobalInput()
    {
        if (_input.GetKeyDown(KeyCode.G))
        {
            if (_mapGenerator.gridGameObject != null)
                _mapGenerator.gridGameObject.SetActive(!_mapGenerator.gridGameObject.activeSelf);
        }
    }

    // ---------- 卡牌拖拽高亮 ----------
    private void HandleCardDragging()
    {
        if (_input.GetMouseButtonDown(0) && IsMouseOverCard()) _isDraggingCard = true;
        if (_input.GetMouseButtonUp(0))
            CancelCardDragging();
        if (_isDraggingCard) HighlightGridOnMouseHover();
    }

    public void ClearCardDragHighlight()
    {
        _isDraggingCard = false;
        if (_lastDraggingHighlightCell == null) return;

        _lastDraggingHighlightCell.GridMesh?.SetActive(_lastDraggingGridWasActive);
        _lastDraggingHighlightCell = null;
    }

    private void CancelCardDragging() => ClearCardDragHighlight();

    private void HighlightGridOnMouseHover()
    {
        if (_input.RaycastFromScreen(_input.MousePosition, out RaycastHit hit, 100f, LayerMask.GetMask("Map")))
        {
            var cell = _mapData.GetCellByWorldPosition(hit.point);
            if (cell != null && cell != _lastDraggingHighlightCell)
            {
                if (_lastDraggingHighlightCell != null && _lastDraggingHighlightCell.GridMesh != null)
                    _lastDraggingHighlightCell.GridMesh.SetActive(_lastDraggingGridWasActive);

                if (cell.IsExplored)
                {
                    _lastDraggingGridWasActive = cell.GridMesh != null && cell.GridMesh.activeSelf;
                    if (cell.GridMesh != null) cell.GridMesh.SetActive(true);
                    _lastDraggingHighlightCell = cell;
                }
                else
                {
                    _lastDraggingHighlightCell = null;
                }
            }
        }
        else
        {
            if (_lastDraggingHighlightCell != null && _lastDraggingHighlightCell.GridMesh != null)
                _lastDraggingHighlightCell.GridMesh.SetActive(_lastDraggingGridWasActive);
            _lastDraggingHighlightCell = null;
        }
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
            if (!IsWithinRuntimeUnitCanvas(result.gameObject.transform))
                return true;
        }

        return false;
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

    private bool IsMouseOverCard()
    {
        if (!_input.IsPointerOverUI(_targetUICanvas)) return false;

        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        if (eventSystem == null) return false;

        var pointerData = new UnityEngine.EventSystems.PointerEventData(eventSystem)
        {
            position = _input.MousePosition
        };
        var results = new List<UnityEngine.EventSystems.RaycastResult>();
        eventSystem.RaycastAll(pointerData, results);
        return results.Any(result => result.gameObject.GetComponentInParent<CardController>() != null);
    }

    public void Dispose()
    {
        // 单位移除事件监听已移除（OnUnitRemoved 不再需要）
    }

    // ---------- 探索：点击未探索格触发探索 ----------
    private void HandleTileClickForExploration()
    {
        // 只在非拖卡状态下响应点击
        if (_isDraggingCard) return;
        if (!_input.GetMouseButtonDown(0)) return;
        // 排除 UI 阻挡
        if (_input.IsPointerOverUI(null)) return;

        // 从屏幕位置射线拾取地图格
        if (!_input.RaycastFromScreen(_input.MousePosition, out RaycastHit hit, 100f, LayerMask.GetMask("Map")))
            return;

        var cell = _mapData.GetCellByWorldPosition(hit.point);
        if (cell == null) return;

        // 未探索格才尝试探索
        if (cell.IsExplored) return;

        // 尝试探索
        _explorationService.TryExplore(cell);
    }
}
