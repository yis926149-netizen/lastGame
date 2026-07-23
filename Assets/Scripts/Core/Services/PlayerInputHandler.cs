using System.Collections.Generic;
using System.Linq;
using UnityChan;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

public class PlayerInputHandler : ITickable, System.IDisposable
{
    private Material _pathMaterial;

    private readonly IInputService _input;
    private readonly IMapDataService _mapData;
    private readonly UnitMovementSystem _movementSystem;
    private readonly IGameStateMachine _gameState;
    private readonly IUIConfigProvider _uiConfig;
    private readonly IMeshGenerator _meshGenerator;
    private readonly UIManagerPresenter _uiPresenter;   // ??I????? UIManager
    private readonly Canvas _targetUICanvas;
    private readonly MapGenerator _mapGenerator;
    private readonly IUnitRepository _unitRepository;

    // ???????
    private readonly Color _defaultMovementColor = new Color(1f, 0.84f, 0f, 0.7f); // (1, 0.84, 0, 0.7)
    private readonly Color _attackRangeColor = Color.cyan;

    // ??????
    private GameObject _selectedUnit;
    private HexCellData _lastHoverCell;
    private GameObject _movementIndicator;
    private GameObject _movementPathObject;
    private MeshRenderer _movementPathRenderer;
    private List<HexCellData> _reachableHexes = new List<HexCellData>();
    private List<HexCellData> _attackableHexes = new List<HexCellData>(); // ??????????????
    private Color _originalGridColor;
    private List<GameObject> _allGrids;
    private bool _isDraggingCard;
    private bool _selectedUnitIsRanged;
    private HexCellData _lastDraggingHighlightCell;
    private bool _lastDraggingGridWasActive;

    private bool _isInitialized;

    [Inject]
    public PlayerInputHandler(
        IInputService input,
        IMapDataService mapData,
        UnitMovementSystem movementSystem,
        IGameStateMachine gameState,
        IUIConfigProvider uiConfig,
        IMeshGenerator meshGenerator,
        UIManagerPresenter uiPresenter,          // ??? Presenter
        IUnitRepository unitRepository,
        [Inject(Id = "TargetUICanvas")] Canvas targetUICanvas,
        MapGenerator mapGenerator
    )
    {
        _input = input;
        _mapData = mapData;
        _movementSystem = movementSystem;
        _gameState = gameState;
        _uiConfig = uiConfig;
        _meshGenerator = meshGenerator;
        _uiPresenter = uiPresenter;
        _unitRepository = unitRepository;
        unitRepository.OnPlayerUnitRemoved += OnUnitRemoved;
        unitRepository.OnEnemyUnitRemoved += OnUnitRemoved;
        _targetUICanvas = targetUICanvas;
        _mapGenerator = mapGenerator;
        Shader pathShader = Shader.Find("Custom/MovementPath")
            ?? Shader.Find("Unlit/Transparent")
            ?? Shader.Find("Hidden/InternalErrorShader");
        if (pathShader.name == "Hidden/InternalErrorShader")
            Debug.LogError("PlayerInputHandler: Custom/MovementPath missing from build. Add Assets/Shader/MovementPath.shader to Project Settings > Graphics > Always Included Shaders.");
        _pathMaterial = new Material(pathShader);

        _movementIndicator = Object.Instantiate(_uiConfig.GetMovementIndicatorPrefab());
        var parent = GameObject.Find("UIModel")?.transform.Find("Movement_Indicator");
        if (parent != null)
            _movementIndicator.transform.SetParent(parent);
        _movementIndicator.SetActive(false);
    }

    private void OnUnitRemoved(GameObject unit)
    {
        if (_selectedUnit == unit)
        {
            DeselectUnit();
        }
    }

    private void EnsureInitialized()
    {
        if (_isInitialized) return;

        var cells = _mapData.GetAllCells();
        if (cells != null && cells.Count > 0)
        {
            _allGrids = cells.Select(c => c.GridMesh).Where(g => g != null).ToList();
            if (_allGrids.Count > 0)
            {
                var renderer = _allGrids[0].GetComponent<Renderer>();
                if (renderer != null)
                    _originalGridColor = renderer.material.color;
            }
            _isInitialized = true;
        }
    }

    public void Tick()
    {
        EnsureInitialized();

        if (_gameState.CurrentPhase is not PlayerPhase)
        {
            CancelCardDragging();
            return;
        }

        if (_input.GetMouseButtonDown(0) && !IsPointerOverBlockingUI())
        {
            HandleLeftClick();
        }
        else if (_input.GetMouseButtonDown(1) && !IsPointerOverBlockingUI())
        {
            HandleRightClick();
        }

        if (_selectedUnit != null)
        {
            UpdateMovementPreview();
        }
        else
        {
            HideMovementPreview();
        }

        HandleGlobalInput();
        HandleCardDragging();
    }

    private void HandleLeftClick()
    {
        if (TryGetTaggedTarget(out GameObject unit, "PlayerUnit"))
        {
            var controller = unit.GetComponent<UnitMovementController>();
            if (controller != null && !controller.CanBeSelected)
                return;

            SelectUnit(unit);
            return;
        }

        if (_selectedUnit != null)
        {
            if (_input.RaycastFromScreen(_input.MousePosition, out RaycastHit mapHit, 100f, LayerMask.GetMask("Map")))
            {
                var targetCell = _mapData.GetCellByWorldPosition(mapHit.point);
                if (targetCell != null)
                {
                    var unitMovement = _selectedUnit.GetComponent<IUnitMovement>();
                    unitMovement.MoveTo(targetCell.HexCoordinate, Enums.MovementPurpose.MoveToDestination);
                    DeselectUnit();
                    return;
                }
            }
        }

        DeselectUnit();
    }

    private void HandleRightClick()
    {
        if (_selectedUnit == null) return;

        if (TryGetTaggedTarget(out GameObject target, "EnemyUnit", "EnemyBuilding"))
        {
            string targetTag = target.tag;

            var unitCtrl = _selectedUnit.GetComponent<UnitMovementController>();
            if (unitCtrl == null) return;

            if (targetTag == "EnemyUnit" || targetTag == "EnemyBuilding")
            {
                bool isRangedUnit = unitCtrl.isRangedAttack;
                int attackRange = 1; // ????????? 1

                if (unitCtrl.characterData != null)
                {
                    attackRange = unitCtrl.characterData.unitData.BasicAttackRange;
                    int id = unitCtrl.characterData.UnitID;
                    if (id == 3 || id == 5 || id == 9) isRangedUnit = true;
                }

                // ????????????????????????????
                Vector3 currentHex = _mapData.WorldToHexCoordinate(_selectedUnit.transform.position);
                Vector3 targetHex = _mapData.WorldToHexCoordinate(target.transform.position);
                float distance = GetHexDistance(currentHex, targetHex);

                if (isRangedUnit)
                {
                    // ??????????????????????????????????? 0.05 ???
                    if (distance <= attackRange + 0.05f)
                    {
                        if (unitCtrl.TryStartRangedAttack(target))
                        {
                            DeselectUnit();
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[HandleRightClick] ?????????????: {distance}, ???: {attackRange}");
                    }
                }
                else
                {
                    var unitMovement = _selectedUnit.GetComponent<IUnitMovement>();
                    unitCtrl.attackedUnit = target;
                    unitCtrl.attackTarget = targetTag;
                    unitMovement.MoveTo(targetHex, Enums.MovementPurpose.MoveToAttack);
                    DeselectUnit();
                }
            }
        }
    }

    private bool TryGetTaggedTarget(out GameObject target, params string[] targetTags)
    {
        RaycastHit[] hits = _input.RaycastAllFromScreen(_input.MousePosition, 100f, Physics.DefaultRaycastLayers);
        foreach (RaycastHit hit in hits)
        {
            Transform current = hit.transform;
            while (current != null)
            {
                if (targetTags.Contains(current.tag))
                {
                    target = current.gameObject;
                    return true;
                }
                current = current.parent;
            }
        }

        // 物理射线未命中时，回退到 UI 射线：单位/建筑头顶的 World Space 图标或血条
        // 会挡在模型前面。这些 UI 是单位物体的子级，从命中的 UI 向上遍历父节点
        // 即可定位到带对应 tag 的单位，使“点到图标”也等价于“点到单位”。
        if (TryGetTaggedTargetFromUI(out target, targetTags))
            return true;

        target = null;
        return false;
    }

    /// <summary>
    /// 对当前指针位置做 UI 射线，命中的 UI 若属于某个带目标 tag 的单位/建筑（其 World Space
    /// Canvas 是该物体的子级），则返回该单位/建筑 GameObject。
    /// </summary>
    private bool TryGetTaggedTargetFromUI(out GameObject target, params string[] targetTags)
    {
        target = null;

        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        if (eventSystem == null) return false;

        var pointerData = new UnityEngine.EventSystems.PointerEventData(eventSystem)
        {
            position = _input.MousePosition
        };
        var results = new List<UnityEngine.EventSystems.RaycastResult>();
        eventSystem.RaycastAll(pointerData, results);

        foreach (var result in results)
        {
            Transform current = result.gameObject != null ? result.gameObject.transform : null;
            while (current != null)
            {
                if (targetTags.Contains(current.tag))
                {
                    target = current.gameObject;
                    return true;
                }
                current = current.parent;
            }
        }

        return false;
    }

    // ---------- ??????? ----------
    private void SelectUnit(GameObject unit)
    {
        if (_selectedUnit == unit) return;

        DeselectUnit();
        _selectedUnit = unit;
        _uiPresenter.SelectUnit(unit);   // ?? Presenter ???????

        var controller = unit.GetComponent<UnitMovementController>();
        _selectedUnitIsRanged = false;
        int attackRange = 0;

        if (controller != null)
        {
            _selectedUnitIsRanged = controller.isRangedAttack;
            if (controller.characterData != null)
            {
                attackRange = controller.characterData.unitData.BasicAttackRange;
                int id = controller.characterData.UnitID;
                if (id == 3 || id == 5 || id == 9) _selectedUnitIsRanged = true;
            }
        }

        var unitMovement = unit.GetComponent<IUnitMovement>();
        var reachable = unitMovement.GetReachableHexes();

        // 1. ???????????????????
        ShowReachableHexes(reachable);

        // 2. ???????????????????????????????????
        if (_selectedUnitIsRanged && attackRange > 0)
        {
            ShowAttackRange(unit.transform.position, attackRange);
        }

        ShowEnemyIndicators(true);
    }

    private void DeselectUnit()
    {
        if (_selectedUnit == null) return;

        // ??????????????????????? + ??????????
        var affectedCells = _reachableHexes.Concat(_attackableHexes).Distinct();
        foreach (var cell in affectedCells)
        {
            if (cell.GridMesh != null)
            {
                cell.GridMesh.SetActive(false);
                var renderer = cell.GridMesh.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.color = _originalGridColor;
            }
        }

        _reachableHexes.Clear();
        _attackableHexes.Clear();
        _selectedUnit = null;
        _uiPresenter.DeselectUnit();     // ?? Presenter ??????
        _selectedUnitIsRanged = false;

        ShowEnemyIndicators(false);
        HideMovementPreview();
    }

    // ---------- ???????????/?????? ----------
    private void HandleUnitCommands(GameObject unit, Vector3 destinationHex, string targetTag, GameObject target)
    {
        var unitMovement = unit.GetComponent<IUnitMovement>();
        var unitCtrl = unit.GetComponent<UnitMovementController>();
        if (unitMovement == null || unitCtrl == null) return;

        if (target != null && (targetTag == "EnemyUnit" || targetTag == "EnemyBuilding"))
        {
            bool isRangedUnit = unitCtrl.isRangedAttack;
            if (unitCtrl.characterData != null)
            {
                int id = unitCtrl.characterData.UnitID;
                if (id == 3 || id == 5 || id == 9) isRangedUnit = true;
            }

            if (isRangedUnit)
            {
                unitCtrl.TryStartRangedAttack(target);
            }
            else
            {
                unitCtrl.attackedUnit = target;
                unitCtrl.attackTarget = targetTag;
                Vector3 targetHex = _mapData.WorldToHexCoordinate(target.transform.position);
                unitMovement.MoveTo(targetHex, Enums.MovementPurpose.MoveToAttack);
            }
        }
        else
        {
            unitMovement.MoveTo(destinationHex, Enums.MovementPurpose.MoveToDestination);
        }

        DeselectUnit();
    }

    // ---------- ?????????? ----------
    private void UpdateMovementPreview()
    {
        if (IsPointerOverAnyUI())
        {
            HideMovementPreview();
            return;
        }

        if (_input.RaycastFromScreen(_input.MousePosition, out RaycastHit hit, 100f, LayerMask.GetMask("Map")))
        {
            var hoverCell = _mapData.GetCellByWorldPosition(hit.point);
            if (hoverCell != null)
            {
                ShowMovementIndicator(hoverCell.RealCenterWorldCoordinate);
                if (hoverCell != _lastHoverCell)
                {
                    _lastHoverCell = hoverCell;
                    ShowMovementPath(_selectedUnit.transform.position, hoverCell.RealCenterWorldCoordinate);
                }
            }
            else HideMovementPreview();
        }
        else HideMovementPreview();
    }

    private void ShowMovementPath(Vector3 startWorld, Vector3 endWorld)
    {
        if (_selectedUnit == null)
        {
            HideMovementPath();
            return;
        }

        HexCellData startCell = _mapData.GetCellByWorldPosition(startWorld);
        HexCellData endCell = _mapData.GetCellByWorldPosition(endWorld);
        if (startCell == null || endCell == null)
        {
            HideMovementPath();
            return;
        }

        float totalCost;
        List<Vector3> path;
        List<Vector3> allPoints = new List<Vector3>(_mapData.GetAllHexCoordinates());
        _movementSystem.CalculateMinMovementCostBetweenTwoHexes(allPoints, startCell.HexCoordinate, endCell.HexCoordinate, Enums.MovementPurpose.MoveToDestination, out totalCost, out path);
        var movement = _selectedUnit.GetComponent<IUnitMovement>();
        if (path == null || path.Count == 0 || movement == null || totalCost > movement.RemainingMovement)
        {
            HideMovementPath();
            return;
        }

        var hexDict = _mapData.GetHexToCell();
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<int> triangles = new List<int>();

        vertices.AddRange(_meshGenerator.GetAdjacentHexLineVertices(startCell, hexDict[path[0]]));
        uv.AddRange(_meshGenerator.GetAdjacentHexLineUV());
        triangles.AddRange(_meshGenerator.GetAdjacentHexLineDrawOrder().Select(i => i));

        for (int i = 1; i < path.Count; i++)
        {
            vertices.AddRange(_meshGenerator.GetAdjacentHexLineVertices(hexDict[path[i - 1]], hexDict[path[i]]));
            uv.AddRange(_meshGenerator.GetAdjacentHexLineUV());
            int offset = i * 4;
            triangles.AddRange(_meshGenerator.GetAdjacentHexLineDrawOrder().Select(index => index + offset));
        }

        if (_movementPathObject == null)
        {
            _movementPathObject = new GameObject("MovementPath");
            var parent = GameObject.Find("UIModel")?.transform.Find("Movement_Indicator");
            if (parent != null) _movementPathObject.transform.SetParent(parent);
            _movementPathRenderer = _movementPathObject.AddComponent<MeshRenderer>();
            _movementPathObject.AddComponent<MeshFilter>();
        }

        MapController.CreatMesh(vertices.ToArray(), uv.ToArray(), triangles.ToArray(), _movementPathObject, _pathMaterial);
        _movementPathRenderer.enabled = true;
    }

    private void HideMovementPreview()
    {
        HideMovementPath();
        HideMovementIndicator();
        _lastHoverCell = null;
    }

    private void HideMovementPath()
    {
        if (_movementPathRenderer != null) _movementPathRenderer.enabled = false;
    }

    // ---------- ??????????????? ----------
    private void ShowMovementIndicator(Vector3 worldPos)
    {
        if (_movementIndicator == null) return;
        _movementIndicator.transform.position = worldPos + Vector3.up * 0.2f;
        _movementIndicator.SetActive(true);
    }

    private void HideMovementIndicator()
    {
        if (_movementIndicator != null) _movementIndicator.SetActive(false);
    }

    // ---------- ??????? ----------
    private void HandleGlobalInput()
    {
        if (_input.GetKeyDown(KeyCode.G))
        {
            if (_mapGenerator.gridGameObject != null)
                _mapGenerator.gridGameObject.SetActive(!_mapGenerator.gridGameObject.activeSelf);
        }
    }

    // ---------- ??????? ----------
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

    /// <summary>����Ƿ������� UI �ϣ��������ε�ͼ/��λ������ƶ�Ԥ���������밴ť�ȳ�ͻ����</summary>
    private bool IsPointerOverAnyUI() => _input.IsPointerOverUI(null);

    /// <summary>
    /// 指针是否落在“会阻挡地图/单位交互”的 UI 上（如卡牌、HUD 按钮、信息面板）。
    /// 单位/建筑头顶的 World Space 图标/血条虽然也是 UI，但它们属于单位本身，
    /// 点击时应等价于点击该单位，故不计入阻挡，避免“点到图标就没反应”。
    /// </summary>
    private bool IsPointerOverBlockingUI()
    {
        if (!IsPointerOverAnyUI()) return false;

        // 若指针下所有命中的 UI 都属于单位/建筑的运行时 World Space Canvas，则不阻挡。
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
                return true; // 命中了非单位 UI（HUD/卡牌等），视为阻挡
        }

        return false; // 命中的 UI 全部属于单位/建筑自身 Canvas，不阻挡
    }

    /// <summary>判断某 UI 变换是否处于某个运行时单位/建筑 Canvas 之下。</summary>
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

    /// <summary>���������ڵ����� Canvas �ϣ��翨���������� <see cref="IsPointerOverAnyUI"/> ���֡�</summary>
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

    // ---------- ?????????????????? ----------
    private void ShowReachableHexes(List<Vector3> hexes)
    {
        _reachableHexes.Clear();
        foreach (var hex in hexes)
        {
            var cell = _mapData.GetCell(hex);
            if (cell != null && cell.GridMesh != null)
            {
                cell.GridMesh.SetActive(true);
                var renderer = cell.GridMesh.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.color = _defaultMovementColor;
                _reachableHexes.Add(cell);
            }
        }
    }

    private void ShowAttackRange(Vector3 centerWorldPos, int range)
    {
        Vector3 centerHex = _mapData.WorldToHexCoordinate(centerWorldPos);
        var allCells = _mapData.GetAllCells();

        foreach (var cell in allCells)
        {
            float dist = GetHexDistance(centerHex, cell.HexCoordinate);
            if (dist <= range && dist > 0.1f)
            {
                if (cell.GridMesh != null)
                {
                    cell.GridMesh.SetActive(true);

                    if (!_reachableHexes.Contains(cell))
                    {
                        var renderer = cell.GridMesh.GetComponent<Renderer>();
                        if (renderer != null)
                            renderer.material.color = _attackRangeColor;
                    }

                    if (!_attackableHexes.Contains(cell))
                        _attackableHexes.Add(cell);
                }
            }
        }
    }

    private void ShowEnemyIndicators(bool show)
    {
        Transform indicatorParent = GameObject.Find("UIModel")?.transform.Find("EnemyUnit_Indicator");
        if (indicatorParent == null && show)
        {
            GameObject parent = new GameObject("EnemyUnit_Indicator");
            parent.transform.SetParent(GameObject.Find("UIModel")?.transform);
            indicatorParent = parent.transform;
        }

        if (indicatorParent == null) return;

        if (show)
        {
            foreach (Transform child in indicatorParent) Object.Destroy(child.gameObject);
            var enemies = GameObject.FindGameObjectsWithTag("EnemyUnit");
            var prefab = _uiConfig.GetEnemyUnitIndicatorPrefab();
            foreach (var enemy in enemies)
            {
                if (enemy == null) continue;

                // 迷雾过滤：只给玩家当前视野内(IsVisible)的敌方单位生成红圈，
                // 否则会标出并泄露迷雾外（记忆区/未探索）的敌人位置。
                HexCellData cell = _mapData.GetCellByWorldPosition(enemy.transform.position);
                if (cell == null || !cell.IsVisible) continue;

                var indicator = Object.Instantiate(prefab, indicatorParent);
                indicator.transform.position = enemy.transform.position + Vector3.up * 0.2f;
            }
        }
        else
        {
            foreach (Transform child in indicatorParent) Object.Destroy(child.gameObject);
        }
    }

    public void ForceDeselectUnit()
    {
        DeselectUnit();
    }

    public void Dispose()
    {
        _unitRepository.OnPlayerUnitRemoved -= OnUnitRemoved;
        _unitRepository.OnEnemyUnitRemoved -= OnUnitRemoved;

        if (_pathMaterial != null) Object.Destroy(_pathMaterial);
        if (_movementIndicator != null) Object.Destroy(_movementIndicator);
        if (_movementPathObject != null) Object.Destroy(_movementPathObject);
    }

    // ---------- ??????????????????????????? ----------
    private float GetHexDistance(Vector3 hexA, Vector3 hexB)
    {
        return (Mathf.Abs(hexA.x - hexB.x) + Mathf.Abs(hexA.y - hexB.y) + Mathf.Abs(hexA.z - hexB.z)) / 2f;
    }
}
