using System.Collections.Generic;
using System.Linq;
using UnityChan;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

public class PlayerInputHandler : ITickable
{
    private Material _pathMaterial;

    private readonly IInputService _input;
    private readonly IMapDataService _mapData;
    private readonly UnitMovementSystem _movementSystem;
    private readonly IGameStateMachine _gameState;
    private readonly IUIConfigProvider _uiConfig;
    private readonly CameraController _cameraController;
    private readonly IMeshGenerator _meshGenerator;
    private readonly UIManagerPresenter _uiPresenter;   // ??I????? UIManager
    private readonly Canvas _targetUICanvas;
    private readonly MapGenerator _mapGenerator;

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

    private bool _isInitialized;

    [Inject]
    public PlayerInputHandler(
        IInputService input,
        IMapDataService mapData,
        UnitMovementSystem movementSystem,
        IGameStateMachine gameState,
        IUIConfigProvider uiConfig,
        CameraController cameraController,
        IMeshGenerator meshGenerator,
        UIManagerPresenter uiPresenter,          // ??? Presenter
        [Inject(Id = "TargetUICanvas")] Canvas targetUICanvas,
        MapGenerator mapGenerator
    )
    {
        _input = input;
        _mapData = mapData;
        _movementSystem = movementSystem;
        _gameState = gameState;
        _uiConfig = uiConfig;
        _cameraController = cameraController;
        _meshGenerator = meshGenerator;
        _uiPresenter = uiPresenter;
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
        _cameraController.Tick();

        if (_gameState.CurrentPhase is not PlayerPhase)
            return;

        if (_input.GetMouseButtonDown(0) && !IsPointerOverAnyUI())
        {
            HandleLeftClick();
        }
        else if (_input.GetMouseButtonDown(1) && !IsPointerOverAnyUI())
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
        int unitLayerMask = LayerMask.GetMask("Default", "Units");
        if (_input.RaycastFromScreen(_input.MousePosition, out RaycastHit unitHit, 100f, unitLayerMask))
        {
            if (unitHit.transform.CompareTag("PlayerUnit"))
            {
                if (unitHit.transform.TryGetComponent<UnitMovementController>(out var controller) && !controller.CanBeSelected)
                    return;

                SelectUnit(unitHit.transform.gameObject);
                return;
            }
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

        int layerMask = LayerMask.GetMask("Default", "EnemyUnit", "EnemyBuilding");

        if (_input.RaycastFromScreen(_input.MousePosition, out RaycastHit hit, 100f, layerMask))
        {
            GameObject target = hit.transform.gameObject;
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
                        unitCtrl.attackedUnit = target;
                        unitCtrl.attackTarget = targetTag;
                        unitCtrl.isAttack = true;
                        unitCtrl.isRangedAttack = true;
                        unitCtrl.currentMovementPoints = 0;
                        DeselectUnit();
                    }
                    else
                    {
                        Debug.LogWarning($"[HandleRightClick] ?????????????: {distance}, ???: {attackRange}");
                    }
                }
                else
                {
                    // ?????????????????
                    var unitMovement = _selectedUnit.GetComponent<IUnitMovement>();
                    bool canAttack = false;
                    Vector3 destinationHex = targetHex;

                    if (_reachableHexes.Any(c => c.HexCoordinate == targetHex))
                    {
                        canAttack = true;
                        destinationHex = targetHex;
                    }
                    else
                    {
                        var validNeighbor = _reachableHexes.FirstOrDefault(c =>
                            GetHexDistance(c.HexCoordinate, targetHex) <= 1.05f);

                        if (validNeighbor != null)
                        {
                            canAttack = true;
                            destinationHex = validNeighbor.HexCoordinate;
                        }
                    }

                    if (canAttack)
                    {
                        unitCtrl.attackedUnit = target;
                        unitCtrl.attackTarget = targetTag;
                        unitMovement.MoveTo(destinationHex, Enums.MovementPurpose.MoveToAttack);
                        DeselectUnit();
                    }
                }
            }
        }
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
                unitCtrl.attackedUnit = target;
                unitCtrl.attackTarget = targetTag;
                unitCtrl.isAttack = true;
                unitCtrl.isRangedAttack = true;
            }
            else
            {
                unitCtrl.attackedUnit = target;
                unitCtrl.attackTarget = targetTag;
                unitMovement.MoveTo(destinationHex, Enums.MovementPurpose.MoveToAttack);
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
        if (_selectedUnit == null) return;

        HexCellData startCell = _mapData.GetCellByWorldPosition(startWorld);
        HexCellData endCell = _mapData.GetCellByWorldPosition(endWorld);
        if (startCell == null || endCell == null) return;

        float totalCost;
        List<Vector3> path;
        List<Vector3> allPoints = new List<Vector3>(_mapData.GetAllHexCoordinates());
        _movementSystem.CalculateMinMovementCostBetweenTwoHexes(allPoints, startCell.HexCoordinate, endCell.HexCoordinate, Enums.MovementPurpose.MoveToDestination, out totalCost, out path);
        if (path == null || path.Count == 0) return;

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
        if (_movementPathRenderer != null) _movementPathRenderer.enabled = false;
        HideMovementIndicator();
        _lastHoverCell = null;
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
        if (_input.GetMouseButtonDown(0) && IsMouseOverTargetUI()) _isDraggingCard = true;
        if (_input.GetMouseButtonUp(0))
        {
            _isDraggingCard = false;
            if (_lastDraggingHighlightCell != null)
            {
                _lastDraggingHighlightCell.GridMesh?.SetActive(false);
                _lastDraggingHighlightCell = null;
            }
        }
        if (_isDraggingCard) HighlightGridOnMouseHover();
    }

    private void HighlightGridOnMouseHover()
    {
        if (_input.RaycastFromScreen(_input.MousePosition, out RaycastHit hit, 100f, LayerMask.GetMask("Map")))
        {
            var cell = _mapData.GetCellByWorldPosition(hit.point);
            if (cell != null && cell != _lastDraggingHighlightCell)
            {
                if (_lastDraggingHighlightCell != null && _lastDraggingHighlightCell.GridMesh != null)
                    _lastDraggingHighlightCell.GridMesh.SetActive(false);

                if (cell.GridMesh != null) cell.GridMesh.SetActive(true);
                _lastDraggingHighlightCell = cell;
            }
        }
        else
        {
            if (_lastDraggingHighlightCell != null && _lastDraggingHighlightCell.GridMesh != null)
                _lastDraggingHighlightCell.GridMesh.SetActive(false);
            _lastDraggingHighlightCell = null;
        }
    }

    /// <summary>鼠标是否在任意 UI 上（用于屏蔽地图/单位点击与移动预览，避免与按钮等冲突）。</summary>
    private bool IsPointerOverAnyUI() => _input.IsPointerOverUI(null);

    /// <summary>仅在用于遮挡检测的 Canvas 上（如卡牌区），与 <see cref="IsPointerOverAnyUI"/> 区分。</summary>
    private bool IsMouseOverTargetUI() => _input.IsPointerOverUI(_targetUICanvas);

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

    // ---------- ??????????????????????????? ----------
    private float GetHexDistance(Vector3 hexA, Vector3 hexB)
    {
        return (Mathf.Abs(hexA.x - hexB.x) + Mathf.Abs(hexA.y - hexB.y) + Mathf.Abs(hexA.z - hexB.z)) / 2f;
    }
}