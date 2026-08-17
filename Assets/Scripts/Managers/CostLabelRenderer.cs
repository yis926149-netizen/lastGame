using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

/// <summary>
/// 探索费用标签渲染器：在未探索且可探索（邻接已探索区）的地块上方显示费用。
/// 使用 Screen Space - Overlay Canvas，通过 WorldToScreenPoint 将世界坐标映射到屏幕坐标，
/// 标签大小恒定，不受相机距离影响。
/// </summary>
public class CostLabelRenderer : MonoBehaviour
{
    private IMapDataService _mapData;
    private GoldWallet _goldWallet;
    private IExplorationService _explorationService;
    private MapVisualEventSO _mapVisualEvent;
    private GameObject _labelPrefab;
    private Camera _camera;
    private Canvas _parentCanvas;
    private RectTransform _canvasRect;
    private Transform _labelContainer;
    private RectTransform _containerRect;  // 用于坐标映射，与 label 的实际父级一致

    private readonly Dictionary<Vector3, GameObject> _activeLabels = new Dictionary<Vector3, GameObject>();
    private readonly Dictionary<Vector3, Vector3> _labelWorldPositions = new Dictionary<Vector3, Vector3>();
    private readonly Stack<GameObject> _pool = new Stack<GameObject>();

    private ILogisticsService _logisticsService;
    private IExplorationCostProvider _costProvider;

    public void Initialize(IMapDataService mapData, IExplorationCostProvider costProvider, GoldWallet goldWallet, GameObject labelPrefab, Canvas parentCanvas, IExplorationService explorationService, MapVisualEventSO mapVisualEvent, ILogisticsService logisticsService = null)
    {
        _mapData = mapData;
        _costProvider = costProvider;
        _goldWallet = goldWallet;
        _labelPrefab = labelPrefab;
        _parentCanvas = parentCanvas;
        _canvasRect = parentCanvas != null ? parentCanvas.GetComponent<RectTransform>() : null;
        _explorationService = explorationService;
        _mapVisualEvent = mapVisualEvent;
        _logisticsService = logisticsService;

        _labelContainer = parentCanvas?.transform.Find("CostLabelContainer");
        if (_labelContainer == null && parentCanvas != null)
        {
            var go = new GameObject("CostLabelContainer", typeof(RectTransform));
            go.transform.SetParent(parentCanvas.transform, false);
            // 让容器完全撑满 Canvas，保证坐标空间与 Canvas 一致
            var crt = go.GetComponent<RectTransform>();
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            go.transform.SetAsFirstSibling();
            _labelContainer = go.transform;
        }
        _containerRect = _labelContainer?.GetComponent<RectTransform>();

        _camera = Camera.main;

        if (_mapVisualEvent != null)
            _mapVisualEvent.OnMapVisualChanged.AddListener(RefreshLabels);
        if (_goldWallet != null)
            _goldWallet.OnGoldChanged += OnGoldChanged;
        if (_explorationService != null)
            _explorationService.CellExplored += OnCellExplored;
        if (_logisticsService != null)
            _logisticsService.LogisticsChanged += OnLogisticsChanged;

        Debug.Log($"[CostLabelRenderer] Initialized. Canvas: {parentCanvas?.name}");
        RefreshLabels();
    }

    private void OnDestroy()
    {
        if (_mapVisualEvent != null)
            _mapVisualEvent.OnMapVisualChanged.RemoveListener(RefreshLabels);
        if (_goldWallet != null)
            _goldWallet.OnGoldChanged -= OnGoldChanged;
        if (_explorationService != null)
            _explorationService.CellExplored -= OnCellExplored;
        if (_logisticsService != null)
            _logisticsService.LogisticsChanged -= OnLogisticsChanged;
        foreach (var kv in _activeLabels)
            if (kv.Value != null) Destroy(kv.Value);
        while (_pool.Count > 0)
            Destroy(_pool.Pop());
    }

    private void OnGoldChanged(int newGold)
    {
        foreach (var kv in _activeLabels)
        {
            var label = kv.Value;
            if (label == null) continue;

            bool canAfford = false;
            if (_mapData != null && _costProvider != null)
            {
                HexCellData cell = _mapData.GetCell(kv.Key);
                canAfford = cell != null && newGold >= _costProvider.GetCost(cell).Amount;
            }
            var cg = label.GetComponent<CanvasGroup>();
            if (cg == null) cg = label.AddComponent<CanvasGroup>();
            cg.alpha = canAfford ? 1f : 0.35f;
            var button = label.GetComponent<Button>();
            if (button != null) button.interactable = canAfford;
        }
    }

    private void OnCellExplored(HexCellData cell)
    {
        RefreshLabels();
    }

    private void OnLogisticsChanged()
    {
        RefreshLabels();
    }

    private void LateUpdate()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera == null || _parentCanvas == null || _containerRect == null) return;

        foreach (var kv in _activeLabels)
        {
            var label = kv.Value;
            if (label == null) continue;

            var rt = label.GetComponent<RectTransform>();
            if (rt == null) continue;

            Vector3 worldPos = _labelWorldPositions[kv.Key] + Vector3.up * 1.2f;
            Vector3 screenPos = _camera.WorldToScreenPoint(worldPos);

            if (screenPos.z < 0)
            {
                // 屏幕外扔到不可见角落，不断 active（否则会杀 DOTween）
                rt.anchoredPosition = new Vector2(-10000, -10000);
            }
            else
            {
                // 必须用 label 的直接父级 RectTransform 做转换，而非 Canvas 本身
                // 否则容器尺寸与 Canvas 不一致时坐标系错位，label 位置错乱
                Vector2 localPos;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _containerRect, screenPos,
                    _parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _camera,
                    out localPos);
                rt.anchoredPosition = localPos;
            }
        }
    }

    private void RefreshLabels()
    {
        if (_mapData == null || _goldWallet == null || _costProvider == null || _labelPrefab == null) return;
        var allCells = _mapData.GetAllCells();
        if (allCells == null) return;

        var toRemove = new List<Vector3>(_activeLabels.Keys);
        int createdCount = 0;

        foreach (var cell in allCells)
        {
            if (cell == null || cell.IsExplored || cell.IsUnexplorable || cell.HexType == Enums.HexType.LakeOrSea) continue;
            // 【程序化山脉-阶段6.8】有效山格不显示探索费用标签/可交互 marker（决策 ⑩）：
            // 山格免雾但不可探索，费用标签会误导玩家以为可点击探索；山格探索由探索系统
            // 统一处理，不通过标签入口。
            if (MountainCellRule.IsEffectiveMountainCell(cell)) continue;
            if (!HasExploredNeighbor(cell, playerIndex: 0)) continue;

            toRemove.Remove(cell.HexCoordinate);

            if (!_activeLabels.TryGetValue(cell.HexCoordinate, out var label))
            {
                label = GetOrCreateLabel(cell.RealCenterWorldCoordinate);
                _activeLabels[cell.HexCoordinate] = label;
                _labelWorldPositions[cell.HexCoordinate] = cell.RealCenterWorldCoordinate;
                createdCount++;
            }
            else
            {
                label.SetActive(true);
                // 【动态地图-阶段二】重显时刷新世界位置：地块高度变化后标签跟随新 RealCenterWorldCoordinate
                // （原实现只 SetActive(true)，高度变化后标签停留在旧 Y）
                _labelWorldPositions[cell.HexCoordinate] = cell.RealCenterWorldCoordinate;
            }

            // 【探索费用按奖励类型】每个标签按地块自身奖励类型显示并判断可负担性
            int cost = _costProvider.GetCost(cell).Amount;
            bool canAfford = _goldWallet.Gold >= cost;

            var text = label.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = cost.ToString();
            }

            // 【探索奖励预生成】按地块预生成的奖励类型切换第二个子物体（Type）的图标
            var uiController = label.GetComponent<UIController>();
            if (uiController != null)
            {
                ExplorationRewardConfigSO.ExplorationRewardType rewardType =
                    cell.ExplorationReward != null
                        ? cell.ExplorationReward.RewardType
                        : ExplorationRewardConfigSO.ExplorationRewardType.None;
                uiController.SetRewardTypeIcon(rewardType);
            }

            var cg = label.GetComponent<CanvasGroup>();
            if (cg == null) cg = label.AddComponent<CanvasGroup>();
            cg.alpha = canAfford ? 1f : 0.35f;

            var button = label.GetComponent<Button>();
            if (button == null) button = label.AddComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.interactable = canAfford;

            if (canAfford)
            {
                var capturedCell = cell;
                button.onClick.AddListener(() =>
                {
                    _explorationService.TryExplore(capturedCell, 0);
                    RefreshLabels();
                });
            }
        }

        if (createdCount > 0)
            Debug.Log($"[CostLabelRenderer] RefreshLabels: created {createdCount}, total active {_activeLabels.Count}");

        foreach (var coord in toRemove)
        {
            if (_activeLabels.TryGetValue(coord, out var label))
            {
                var btn = label.GetComponent<Button>();
                if (btn != null) btn.onClick.RemoveAllListeners();
                label.SetActive(false);
                _pool.Push(label);
                _activeLabels.Remove(coord);
                _labelWorldPositions.Remove(coord);
            }
        }
    }

    private bool HasExploredNeighbor(HexCellData cell, int playerIndex)
    {
        for (int i = 0; i < 6; i++)
        {
            var n = _mapData.GetNeighbor(cell, (Enums.HexDirection)i);
            if (n == null || n.Player_City_Index.Key != playerIndex) continue;
            if (_logisticsService == null || _logisticsService.IsLogisticsConnected(n, playerIndex))
                return true;
        }
        return false;
    }

    private GameObject GetOrCreateLabel(Vector3 worldPos)
    {
        GameObject go;
        if (_pool.Count > 0)
        {
            go = _pool.Pop();
            go.SetActive(true);
            return go;
        }

        go = Instantiate(_labelPrefab, _labelContainer != null ? _labelContainer : transform);

        var rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        return go;
    }
}
