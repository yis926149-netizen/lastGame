using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

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

    private readonly Dictionary<Vector3, GameObject> _activeLabels = new Dictionary<Vector3, GameObject>();
    private readonly Dictionary<Vector3, Vector3> _labelWorldPositions = new Dictionary<Vector3, Vector3>();
    private readonly Dictionary<GameObject, Vector3> _labelBaseScales = new Dictionary<GameObject, Vector3>();
    private readonly Stack<GameObject> _pool = new Stack<GameObject>();

    public void Initialize(IMapDataService mapData, GoldWallet goldWallet, GameObject labelPrefab, Canvas parentCanvas, IExplorationService explorationService, MapVisualEventSO mapVisualEvent)
    {
        _mapData = mapData;
        _goldWallet = goldWallet;
        _labelPrefab = labelPrefab;
        _parentCanvas = parentCanvas;
        _canvasRect = parentCanvas != null ? parentCanvas.GetComponent<RectTransform>() : null;
        _explorationService = explorationService;
        _mapVisualEvent = mapVisualEvent;

        _labelContainer = parentCanvas?.transform.Find("CostLabelContainer");
        if (_labelContainer == null && parentCanvas != null)
        {
            var go = new GameObject("CostLabelContainer", typeof(RectTransform));
            go.transform.SetParent(parentCanvas.transform, false);
            go.transform.SetAsFirstSibling();
            _labelContainer = go.transform;
        }

        _camera = Camera.main;

        if (_mapVisualEvent != null)
            _mapVisualEvent.OnMapVisualChanged.AddListener(RefreshLabels);
        if (_goldWallet != null)
            _goldWallet.OnGoldChanged += OnGoldChanged;

        Debug.Log($"[CostLabelRenderer] Initialized. Canvas: {parentCanvas?.name}");
        RefreshLabels();
    }

    private void OnDestroy()
    {
        if (_mapVisualEvent != null)
            _mapVisualEvent.OnMapVisualChanged.RemoveListener(RefreshLabels);
        if (_goldWallet != null)
            _goldWallet.OnGoldChanged -= OnGoldChanged;
        foreach (var kv in _activeLabels)
            if (kv.Value != null) Destroy(kv.Value);
        while (_pool.Count > 0)
            Destroy(_pool.Pop());
    }

    private void OnGoldChanged(int newGold)
    {
        bool canAfford = newGold >= _goldWallet.ExplorationCost;
        foreach (var kv in _activeLabels)
        {
            var label = kv.Value;
            if (label == null) continue;
            var cg = label.GetComponent<CanvasGroup>();
            if (cg == null) cg = label.AddComponent<CanvasGroup>();
            cg.alpha = canAfford ? 1f : 0.35f;
            var button = label.GetComponent<Button>();
            if (button != null) button.interactable = canAfford;
        }
    }

    private void LateUpdate()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera == null || _parentCanvas == null || _canvasRect == null) return;

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
                Vector2 localPos;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, screenPos,
                    _parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _camera,
                    out localPos);
                rt.anchoredPosition = localPos;
            }
        }
    }

    private void RefreshLabels()
    {
        if (_mapData == null || _goldWallet == null || _labelPrefab == null) return;
        var allCells = _mapData.GetAllCells();
        if (allCells == null) return;

        bool canAfford = _goldWallet.Gold >= _goldWallet.ExplorationCost;
        var toRemove = new List<Vector3>(_activeLabels.Keys);
        int createdCount = 0;

        foreach (var cell in allCells)
        {
            if (cell == null || cell.IsExplored || cell.IsUnexplorable || cell.HexType == Enums.HexType.LakeOrSea) continue;
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
            }

            var text = label.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = $"{_goldWallet.ExplorationCost}";
            }

            var cg = label.GetComponent<CanvasGroup>();
            if (cg == null) cg = label.AddComponent<CanvasGroup>();
            cg.alpha = canAfford ? 1f : 0.35f;

            // Button 点击探索
            var button = label.GetComponent<Button>();
            if (button == null) button = label.AddComponent<Button>();
            button.onClick.RemoveAllListeners();

            if (canAfford)
            {
                var capturedCell = cell;
                button.onClick.AddListener(() =>
                {
                    _explorationService.TryExplore(capturedCell);
                    RefreshLabels();
                });

                var t = label.transform;
                t.DOKill();
                if (!_labelBaseScales.TryGetValue(label, out var baseScale))
                {
                    baseScale = t.localScale;
                    _labelBaseScales[label] = baseScale;
                }
                t.DOScale(baseScale * 1.12f, 0.6f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
            }
            else
            {
                label.transform.DOKill();
                if (_labelBaseScales.TryGetValue(label, out var baseScale))
                    label.transform.localScale = baseScale;
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
                label.transform.DOKill();
                _labelBaseScales.Remove(label);
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
            if (n != null && n.IsExplored && n.Player_City_Index.Key == playerIndex)
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
