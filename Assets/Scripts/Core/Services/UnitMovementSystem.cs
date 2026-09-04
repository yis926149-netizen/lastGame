// UnitMovementSystem.cs
using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class UnitMovementSystem : ITickable
{
    private readonly IMapDataService _mapDataService;
    private readonly MapVisualEventSO _mapVisualEvent;  // 用于触发视觉更新
    private readonly GameLoop _gameLoop;                // 用于暂停检查（批次 C）
    private readonly ILogisticsService _logisticsService; // 用于占领后重算后勤
    private readonly IMapInteractionGate _interactionGate; // 动态地图-阶段二：事务/动画期间交互锁
    private readonly IMapVisibilityResolver _visibilityResolver; // 全局迷雾/临时点亮判定

    // 正在移动的单位列表
    private List<MovingUnit> _movingUnits = new List<MovingUnit>();
    // 格级预留查询（任一槽被预留即视为该格已预留）；槽级状态存于 HexCellData.UnitSlots.ReservedBy
    private readonly HashSet<Vector3> _reservedCells = new HashSet<Vector3>();

    // ── 【阶段 3】表现层避让参数（纯表现，不影响逻辑格/槽位/占用）────────
    // 错峰出发：同批移动单位 0~0.2s 递增延迟，错开「同槽位号汇合/交叉路口」的瞬时重合。
    private const float MoveStaggerMaxSeconds = 0.2f;
    // 成对分离：距离 < SeparationRadius 的单位对各自横向推开，每帧推开量 clamp 到 MaxSeparationPush。
    private const float SeparationRadius = 0.6f;
    private const float MaxSeparationPush = 0.2f;

    public UnitMovementSystem(IMapDataService mapDataService, MapVisualEventSO mapVisualEvent, GameLoop gameLoop, [InjectOptional] ILogisticsService logisticsService = null, [InjectOptional] IMapInteractionGate interactionGate = null, [InjectOptional] IMapVisibilityResolver visibilityResolver = null)
    {
        _mapDataService = mapDataService;
        _mapVisualEvent = mapVisualEvent;
        _gameLoop = gameLoop;
        _logisticsService = logisticsService;
        _interactionGate = interactionGate;
        _visibilityResolver = visibilityResolver;
    }

    /// <summary>
    /// 全图六边形坐标（缓存表，只读语义）。
    /// 直接把此列表传给寻路：寻路只读不改，且按引用缓存成员判定集，重复调用零分配。
    /// 不再本地二次缓存 —— HexMapService 内部已缓存，且它会在地图重建时失效自己的表；
    /// 本类若另存一份就会在重建后指向旧地图。调用方不得修改返回的列表。
    /// </summary>
    public List<Vector3> AllHexCoordinates => _mapDataService.GetAllHexCoordinates();

    /// <summary>
    /// 缩放 deltaTime（x2/x3 时放大，暂停时为 0）。
    /// 供表现层组件（UnitMovementController 攻击动画窗口等）消费，与移动步长同源。
    /// </summary>
    public float ScaledDeltaTime => _gameLoop != null ? _gameLoop.ScaledDeltaTime : Time.deltaTime;

    /// <summary>
    /// 当前速度档的动画倍率（暂停 0），供 Animator.speed 等表现层同步加速。
    /// </summary>
    public float AnimationSpeedMultiplier => _gameLoop != null ? _gameLoop.SpeedMultiplier : 1f;

    /// <summary>
    /// 请求单位移动
    /// </summary>
    public bool RequestMove(IUnitMovement unit, Vector3 targetHex, Enums.MovementPurpose purpose)
    {
        //Debug.Log($"[UnitMovementSystem] RequestMove: unit={unit.gameObject.name}, target={targetHex}, purpose={purpose}");

        if ((unit as UnityEngine.Object) == null || unit.gameObject == null)
        {
            return false;
        }

        // 【批次 B】移动力校验已移除——实时化后单位移动力由 GameLoop 驱动，无配额概念。
        // （原 RemainingMovement <= 0 判断删除）

        if (_movingUnits.Exists(mu => mu.Unit == unit))
        {
            return false;
        }

        Vector3 startHex = unit.CurrentHexCoordinate;
        Vector3 destinationHex = targetHex;

        if (purpose == Enums.MovementPurpose.MoveToAttack)
        {
            if (!TryFindAttackDestination(unit, targetHex, out destinationHex))
            {
                Debug.LogWarning("[UnitMovementSystem] RequestMove failed: no legal attack position.");
                return false;
            }
        }

        var destinationCell = _mapDataService.GetCell(destinationHex);
        if (destinationCell == null)
        {
            return false;
        }

        // 【动态地图-阶段二】交互锁：事务/动画期间受影响格禁止新移动请求（§12.6）
        if (_interactionGate != null && _interactionGate.IsLocked(destinationCell, MapInteractionType.Move))
        {
            return false;
        }

        // 1. 计算原始最短路径
        if (!CalculateMinMovementCostBetweenTwoHexes(
            AllHexCoordinates,
            startHex,
            destinationHex,
            purpose,
            FactionOf(unit.gameObject),
            out float _,
            out List<Vector3> path))
        {
            Debug.LogWarning($"[UnitMovementSystem] RequestMove failed: cannot reach target.");
            return false;
        }

        // 2. 路径截断逻辑修复
        List<Vector3> actualPath = new List<Vector3>(path ?? new List<Vector3>());

        // 3. 处理“已在原地/已在邻位”的情况
        if (actualPath.Count == 0)
        {
            if (purpose == Enums.MovementPurpose.MoveToAttack)
            {
                // 如果是因为攻击截断导致路径为空，说明单位已处于攻击位置
                // 这里我们手动调用单位的完成回调，以便让它立即进入攻击序列
                Debug.Log("[UnitMovementSystem] Unit already at attack position, starting attack sequence.");
                unit.OnMoveFinished();
                return true;
            }
            else
            {
                Debug.LogWarning("[UnitMovementSystem] RequestMove rejected: already at destination.");
                return false;
            }
        }

        // 4. 计算实际花费并验证移动力
        float actualCost = 0f;
        foreach (var hexCoord in actualPath)
        {
            actualCost += _mapDataService.GetCell(hexCoord).movementCost;
        }

        // 【批次 B】移动力代价校验已移除——实时化后无配额限制。
        // （原 actualCost > unit.RemainingMovement 判断删除）

        // 5. 提交移动任务
        var umc = unit as UnitMovementController;
        float baseMovementPoints = umc?.characterData?.unitData?.MovementPoints ?? 3f;
        float speedMultiplier = umc?.characterData?.moveSpeedMultiplier ?? 1f;
        float computedSpeed = Mathf.Max(1f, baseMovementPoints * CoreGameplayConfigProvider.MovementSpeedPerPoint * speedMultiplier);

        // 起点站位：确保单位在起点格持有一个站位槽（未烘焙/旧逻辑格惰性创建并取中心槽）。
        HexCellData startCell = _mapDataService.GetCell(startHex);
        int startSlot = -1;
        if (startCell != null)
        {
            startSlot = startCell.UnitSlots?.GetStandingSlot(unit.gameObject) ?? -1;
            if (startSlot < 0)
            {
                Vector3 p = unit.gameObject.transform.position;
                startCell.TryClaimStandingUnit(unit.gameObject, p, p, false, out startSlot, out _);
            }
        }

        var movingUnit = new MovingUnit
        {
            Unit = unit,
            Path = actualPath,
            VisualSlots = new List<int>(),
            CurrentPathIndex = 0,
            Purpose = purpose,
            StartHex = startHex,
            StartSlot = startSlot,
            CurrentStandingHex = startHex,
            CurrentStandingSlot = startSlot,
            DestinationHex = destinationHex,
            DestinationSlot = -1,
            StartRemainingMovement = unit.RemainingMovement,
            MoveSpeed = computedSpeed,
            ReservedNodes = new List<KeyValuePair<Vector3, int>>()
        };

        // 【完整任务模式】原子预留整条路径的槽位：任一路径格无「站位+预留」空闲槽则整体失败，不产生半状态。
        if (!ReservePathSlots(movingUnit))
        {
            Debug.LogWarning("[UnitMovementSystem] RequestMove rejected: no free slot on path.");
            return false;
        }

        movingUnit.DestinationSlot = movingUnit.VisualSlots.Count > 0
            ? movingUnit.VisualSlots[movingUnit.VisualSlots.Count - 1]
            : -1;

        _movingUnits.Add(movingUnit);

        return true;
    }

    /// <summary>
    /// 为整条逻辑路径逐格预留视觉槽位（几何选点：取「前格中心→后格中心」连线最近的空槽）。
    /// 任一路径格失败则回滚已预留的槽位并返回 false。
    /// </summary>
    private bool ReservePathSlots(MovingUnit movingUnit)
    {
        GameObject unitGo = movingUnit.Unit?.gameObject;
        List<Vector3> path = movingUnit.Path;
        if (path == null || path.Count == 0) return true;

        Vector3 startCenter = unitGo != null
            ? unitGo.transform.position
            : (_mapDataService.GetCell(movingUnit.StartHex)?.RealCenterWorldCoordinate ?? Vector3.zero);

        for (int i = 0; i < path.Count; i++)
        {
            HexCellData cell = _mapDataService.GetCell(path[i]);
            if (cell == null) { ReleaseReservation(movingUnit); return false; }

            // 未烘焙格：退回旧单单位语义——已被其他单位占用则预留失败。
            if (cell.UnitSlots == null)
            {
                if (cell.IsHaveUnit() && !cell.HasStandingUnit(unitGo)) { ReleaseReservation(movingUnit); return false; }
                cell.EnsureUnitSlots();
            }

            Vector3 from = (i == 0)
                ? startCenter
                : (_mapDataService.GetCell(path[i - 1])?.RealCenterWorldCoordinate ?? cell.RealCenterWorldCoordinate);

            Vector3 to;
            if (i + 1 < path.Count)
            {
                to = _mapDataService.GetCell(path[i + 1])?.RealCenterWorldCoordinate ?? cell.RealCenterWorldCoordinate;
            }
            else
            {
                Vector3 cur = cell.RealCenterWorldCoordinate;
                to = cur + (cur - from);
            }

            if (!cell.UnitSlots.TryReserveSlot(movingUnit.Unit, from, to, cell.RealCenterWorldCoordinate, out int slotId, out _))
            {
                ReleaseReservation(movingUnit);
                return false;
            }

            movingUnit.VisualSlots.Add(slotId);
            movingUnit.ReservedNodes.Add(new KeyValuePair<Vector3, int>(path[i], slotId));
            _reservedCells.Add(path[i]);
        }
        return true;
    }

    public void CancelMove(IUnitMovement unit)
    {
        if (unit == null) return;

        for (int i = _movingUnits.Count - 1; i >= 0; i--)
        {
            var movingUnit = _movingUnits[i];
            if (movingUnit.Unit != unit) continue;

            RestoreStartCell(movingUnit);
            ReleaseReservation(movingUnit);
            _movingUnits.RemoveAt(i);
        }
    }

    public bool IsDestinationReserved(Vector3 hexCoordinate)
    {
        return _reservedCells.Contains(hexCoordinate);
    }

    // Zenject 每帧调用（Tick）
    public void Tick()
    {
        // 【批次 C】暂停时停止所有移动动画
        if (_gameLoop != null && _gameLoop.IsPaused) return;

        for (int i = _movingUnits.Count - 1; i >= 0; i--)
        {
            var mu = _movingUnits[i];
            if ((mu.Unit as UnityEngine.Object) == null || mu.Unit.gameObject == null)
            {
                ReleaseReservation(mu);
                _movingUnits.RemoveAt(i);
                continue;
            }

            bool finished = UpdateMovement(mu);
            if (finished)
            {
                CommitDestination(mu);
                TryCaptureEnemyCell(mu);
                _movingUnits.RemoveAt(i);
                mu.Unit.OnMoveFinished();
            }
        }
    }

    private bool TryFindAttackDestination(IUnitMovement unit, Vector3 targetHex, out Vector3 destinationHex)
    {
        destinationHex = unit.CurrentHexCoordinate;
        if (HexDistance(unit.CurrentHexCoordinate, targetHex) <= 1f)
        {
            return true;
        }

        List<Vector3> allPoints = AllHexCoordinates;
        float bestCost = float.MaxValue;
        bool found = false;
        HexCellData targetCell = _mapDataService.GetCell(targetHex);
        if (targetCell == null) return false;

        for (int i = 0; i < 6; i++)
        {
            HexCellData neighbor = _mapDataService.GetNeighbor(targetCell, (Enums.HexDirection)i);
            if (neighbor == null || !CanEnterCell(neighbor, unit.gameObject, false, FactionOf(unit.gameObject))) continue;
            if (!neighbor.HasFreeSlotForReservation()) continue;

            if (CalculateMinMovementCostBetweenTwoHexes(
                allPoints,
                unit.CurrentHexCoordinate,
                neighbor.HexCoordinate,
                Enums.MovementPurpose.MoveToDestination,
                FactionOf(unit.gameObject),
                out float cost,
                out _) && cost <= unit.RemainingMovement && cost < bestCost)
            {
                bestCost = cost;
                destinationHex = neighbor.HexCoordinate;
                found = true;
            }
        }

        return found;
    }

    private void CommitDestination(MovingUnit movingUnit)
    {
        // 最终抵达格的站位已在 CommitPathNode 中完成（释放旧格站位、把预留槽提升为站位槽），
        // 此处只做防御性收尾：释放该任务可能残留的任何预留（正常情况下应为空）。
        ReleaseReservation(movingUnit);
    }

    private void TryCaptureEnemyCell(MovingUnit movingUnit)
    {
        if (_logisticsService == null) return;

        HexCellData destinationCell = _mapDataService.GetCell(movingUnit.DestinationHex);
        if (destinationCell == null) return;

        int cellOwner = destinationCell.Player_City_Index.Key;
        // 【断供方案-阶段3/决策10】占领只对阵营 0/1 有效；
        // 中立（Key<0）与公共建筑伪阵营（Key>=2）豁免。
        if (cellOwner < 0 || cellOwner >= 2) return;

        GameObject unit = movingUnit.Unit.gameObject;
        if (unit == null) return;
        var controller = unit.GetComponent<UnitMovementController>();
        if (controller == null) return;

        int attackerFaction = controller.PlayerIndex;
        if (cellOwner == attackerFaction) return;

        var buildingEntry = destinationCell.BulidingTypeOnHex_Building;
        GameObject capturedBuilding = null;
        if (buildingEntry.Key != Enums.BulidingType.NoBuilding && buildingEntry.Value != null)
        {
            var buildingBase = buildingEntry.Value.GetComponent<BuildingBase>();
            // 【断供方案-阶段3/决策1a】仅功能正常（供应畅通）的建筑阻挡占领；
            // 失能建筑不阻挡——占领继续，建筑随格易主（不摧毁、HP 回满）。
            if (buildingBase != null &&
                buildingBase.Player_City_Index.Key == cellOwner &&
                buildingBase.IsFunctional)
            {
                return;
            }
            capturedBuilding = buildingEntry.Value;
        }

        _logisticsService.TransferOwner(destinationCell, attackerFaction);

        // 失能建筑随格易主；公共建筑走 OnCaptured 全量（内部再重算一次，覆盖外一环归属）
        if (capturedBuilding != null)
            BuildingTransferService.TransferBuilding(capturedBuilding, attackerFaction, triggerRecalculate: true);
    }

    private void RestoreStartCell(MovingUnit movingUnit)
    {
        GameObject unitGo = movingUnit.Unit?.gameObject;
        if (unitGo == null) return;

        // 释放当前实际站位格（可能是起点，也可能是已 commit 的中间格）。
        HexCellData currentCell = _mapDataService.GetCell(movingUnit.CurrentStandingHex);
        HexCellData startCell = _mapDataService.GetCell(movingUnit.StartHex);
        if (currentCell != null && currentCell != startCell)
            currentCell.ReleaseStandingUnit(unitGo);

        if (startCell != null)
        {
            // 站回起点槽（占用指定 StartSlot；槽被占则退回中心槽）。
            Vector3 pos = startCell.RealCenterWorldCoordinate;
            if (startCell.UnitSlots != null)
            {
                if (!startCell.UnitSlots.TryAcquireStandingSlotAt(unitGo, movingUnit.StartSlot, startCell.RealCenterWorldCoordinate, out pos))
                    startCell.UnitSlots.TryAcquireStandingSlot(unitGo, startCell.RealCenterWorldCoordinate, startCell.RealCenterWorldCoordinate, out _, out pos, startCell.RealCenterWorldCoordinate, preferLine: false);
            }
            unitGo.transform.position = pos;
            // 【卡顿分析·第八节】回退起点是跨格改写，缓存必须作废
            movingUnit.Unit?.InvalidateHexCoordinateCache();

            // 同步旧字段（primary owner）
            if (!startCell.IsHaveUnit()) startCell.SetHaveUnit(true, unitGo);
            if (startCell.GetOccupant() == null) startCell.SetOccupant(unitGo);
        }

        movingUnit.Unit.RemainingMovement = movingUnit.StartRemainingMovement;
    }

    private void ReleaseReservation(MovingUnit movingUnit)
    {
        if (movingUnit?.ReservedNodes == null) return;
        foreach (KeyValuePair<Vector3, int> node in movingUnit.ReservedNodes)
        {
            HexCellData cell = _mapDataService.GetCell(node.Key);
            cell?.UnitSlots?.ReleaseReservation(node.Value);
            if (cell == null || cell.UnitSlots == null || !cell.UnitSlots.HasAnyReservation())
                _reservedCells.Remove(node.Key);
        }
        movingUnit.ReservedNodes.Clear();
    }

    public void ReleaseReservationByUnit(GameObject unit)
    {
        if (unit == null) return;
        for (int i = _movingUnits.Count - 1; i >= 0; i--)
        {
            if (_movingUnits[i].Unit.gameObject == unit)
            {
                var mu = _movingUnits[i];
                ReleaseReservation(mu);
                _movingUnits.RemoveAt(i);
            }
        }
    }

    // ── 【动态地图-阶段二】地块变化联动（§12.4）──────────────────────

    /// <summary>
    /// 取消路径途经"已不可通行格"（movementCost == MaxValue）的移动任务。
    /// 被取消单位经 RestoreStartCell 回到起点格并恢复占用状态；
    /// 若起点格同样不可通行，由 EjectUnitsFromImpassableCells 兜底弹射。
    /// </summary>
    public void CancelMovesIntersecting(IReadOnlyCollection<HexCellData> blockedCells)
    {
        if (blockedCells == null || blockedCells.Count == 0) return;

        var blockedSet = new HashSet<Vector3>();
        foreach (HexCellData cell in blockedCells)
        {
            if (cell != null && cell.movementCost == float.MaxValue)
                blockedSet.Add(cell.HexCoordinate);
        }
        if (blockedSet.Count == 0) return;

        for (int i = _movingUnits.Count - 1; i >= 0; i--)
        {
            MovingUnit mu = _movingUnits[i];
            if (!PathIntersects(mu.Path, blockedSet)) continue;

            RestoreStartCell(mu);
            ReleaseReservation(mu);
            _movingUnits.RemoveAt(i);

            // 通知 UnitMovementController 重置 isMoving，否则动画会卡在行走状态
            var ctrl = mu.Unit?.gameObject?.GetComponent<UnitMovementController>();
            if (ctrl != null)
            {
                ctrl.isMoving = false;
                ctrl.movementPurpose = Enums.MovementPurpose.None;
            }
        }
    }

    private static bool PathIntersects(List<Vector3> path, HashSet<Vector3> blockedSet)
    {
        if (path == null) return false;
        foreach (Vector3 coord in path)
        {
            if (blockedSet.Contains(coord)) return true;
        }
        return false;
    }

    /// <summary>
    /// 弹射：把站立在不可通行格上的【所有】单位迁到最近的"可通行且有自由站位槽"格（6 向 BFS）。
    /// 纯位置迁移——不触发 TryCaptureEnemyCell、不写归属（决策 B）。
    /// 原子迁移：站位槽 + HaveUnit/Occupant + 攻击槽（本格）。
    /// </summary>
    public void EjectUnitsFromImpassableCells(IReadOnlyCollection<HexCellData> cells)
    {
        if (cells == null) return;

        foreach (HexCellData cell in cells)
        {
            if (cell == null || cell.movementCost != float.MaxValue) continue;

            List<GameObject> units = cell.GetStandingUnits();
            foreach (GameObject unit in units)
            {
                if (unit == null) continue;

                if (FindNearestFreePassableCell(cell, unit, out HexCellData target))
                {
                    MigrateOccupancy(cell, target, unit);
                }
                else
                {
                    // 无可用落点（极端情况）：仍释放占位，避免"格内多单位"读错状态
                    cell.ReleaseStandingUnit(unit);
                    cell.ReleaseAttackerSlots(unit);
                    Debug.LogWarning($"[UnitMovementSystem] Eject: 找不到可弹射落点，单位 {unit.name} 释放占用（无归属迁移）。");
                }
            }
        }
    }

    /// <summary>6 向 BFS：从 cell 出发找最近"可通行（movementCost &lt; MaxValue）且仍有自由站位槽"格。</summary>
    private bool FindNearestFreePassableCell(HexCellData from, GameObject unit, out HexCellData result)
    {
        result = null;
        var visited = new HashSet<Vector3>();
        var queue = new Queue<HexCellData>();
        visited.Add(from.HexCoordinate);
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            HexCellData current = queue.Dequeue();
            for (int d = 0; d < 6; d++)
            {
                HexCellData neighbor = _mapDataService.GetNeighbor(current, (Enums.HexDirection)d);
                if (neighbor == null || !visited.Add(neighbor.HexCoordinate)) continue;

                if (neighbor.movementCost < float.MaxValue &&
                    IsFogFree(neighbor, FactionOf(unit)) &&
                    neighbor.HasFreeStandingSlot())
                {
                    result = neighbor;
                    return true;
                }
                queue.Enqueue(neighbor);
            }
        }
        return false;
    }

    /// <summary>把单位从 source 原子迁移到 target：站位槽 + 旧字段 + transform.position。</summary>
    private void MigrateOccupancy(HexCellData source, HexCellData target, GameObject unit)
    {
        source.ReleaseStandingUnit(unit);
        source.ReleaseAttackerSlots(unit);

        Vector3 from = source.RealCenterWorldCoordinate;
        Vector3 to = target.RealCenterWorldCoordinate;
        if (target.TryClaimStandingUnit(unit, from, to, preferLine: true, out _, out Vector3 pos))
        {
            unit.transform.position = pos;
        }
        else
        {
            unit.transform.position = target.RealCenterWorldCoordinate;
        }

        // 【卡顿分析·第八节】迁移是跨格改写且不经 OnMoveFinished，缓存必须作废
        unit.GetComponent<UnitMovementController>()?.InvalidateHexCoordinateCache();
    }

    /// <summary>变化格上的站立单位吸附到各自槽位（Y 跟随新 RealCenterWorldCoordinate，XZ 保留槽位偏移；移动中单位跳过）。</summary>
    public void RefreshStandingUnitPositions(IReadOnlyCollection<HexCellData> changedCells)
    {
        if (changedCells == null) return;

        foreach (HexCellData cell in changedCells)
        {
            if (cell == null || cell.movementCost == float.MaxValue) continue;

            List<GameObject> units = cell.GetStandingUnits();
            foreach (GameObject unit in units)
            {
                if (unit == null || IsUnitMoving(unit)) continue;

                Vector3 pos = cell.RealCenterWorldCoordinate;
                if (cell.UnitSlots != null)
                {
                    int slotId = cell.UnitSlots.GetStandingSlot(unit);
                    if (slotId >= 0)
                        pos = cell.UnitSlots.GetWorldPosition(slotId, cell.RealCenterWorldCoordinate);
                }
                unit.transform.position = pos;
            }
        }
    }

    /// <summary>查询单位是否处于移动任务中（阶段四：动画期间视觉高度跟随跳过移动中单位，§12.5）。</summary>
    public bool IsUnitMoving(GameObject unit)
    {
        if (unit == null) return false;
        foreach (MovingUnit mu in _movingUnits)
        {
            if (mu.Unit != null && mu.Unit.gameObject == unit) return true;
        }
        return false;
    }

    private static float HexDistance(Vector3 a, Vector3 b)
    {
        return (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z)) * 0.5f;
    }

    // 单步移动更新
    /// <summary>
    /// 单步移动更新
    /// </summary>
    /// <param name="mu">正在移动的单位数据</param>
    /// <returns>true 表示移动完成（到达终点或移动力耗尽），false 表示仍在移动中</returns>
    private bool UpdateMovement(MovingUnit mu)
    {
        float moveSpeed = mu.MoveSpeed;
        float rotationSpeed = CoreGameplayConfigProvider.UnitRotationSpeed;
        Transform trans = mu.Unit.gameObject.transform;

        if (mu.CurrentPathIndex >= mu.Path.Count)
            return true;

        Vector3 targetPos = GetPathNodeWorldPosition(mu, mu.CurrentPathIndex);
        float distance = Vector3.Distance(trans.position, targetPos);

        if (distance < 0.1f)
        {
            // 吸附到当前路径点的槽位世界坐标，避免浮点漂移
            trans.position = targetPos;

            // 抵达当前节点：站位从上一格迁移到本格（把预留槽提升为站位槽）。
            CommitPathNode(mu, mu.CurrentPathIndex);

            // 【实时化】移动力配额已废除，不再扣减 RemainingMovement，也不再因耗尽而截断路径。

            // 移动到下一个节点
            mu.CurrentPathIndex++;

            if (mu.CurrentPathIndex >= mu.Path.Count)
                return true;

            targetPos = GetPathNodeWorldPosition(mu, mu.CurrentPathIndex);
        }

        // 使用 MoveTowards：单帧步长若大于剩余距离，会精确落到目标点，避免越过目标后在阈值外来回振荡（导致永远无法完成移动）
        float deltaTime = _gameLoop != null ? _gameLoop.ScaledDeltaTime : Time.deltaTime;
        float step = moveSpeed * deltaTime;
        Vector3 direction = targetPos - trans.position;
        trans.position = Vector3.MoveTowards(trans.position, targetPos, step);

        if (direction != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
            trans.rotation = Quaternion.Slerp(trans.rotation, targetRot, rotationSpeed * deltaTime);
        }

        return false;
    }

    /// <summary>取路径节点 i 的槽位世界坐标（未烘焙格退回格心）。</summary>
    private Vector3 GetPathNodeWorldPosition(MovingUnit mu, int pathIndex)
    {
        HexCellData cell = _mapDataService.GetCell(mu.Path[pathIndex]);
        if (cell == null) return mu.Unit.gameObject.transform.position;
        if (cell.UnitSlots == null || pathIndex < 0 || pathIndex >= mu.VisualSlots.Count)
            return cell.RealCenterWorldCoordinate;
        return cell.UnitSlots.GetWorldPosition(mu.VisualSlots[pathIndex], cell.RealCenterWorldCoordinate);
    }

    /// <summary>抵达路径节点 pathIndex：释放上一格站位，把该节点预留槽提升为站位槽，并同步旧字段。</summary>
    private void CommitPathNode(MovingUnit mu, int pathIndex)
    {
        GameObject unitGo = mu.Unit?.gameObject;
        if (unitGo == null) return;

        // 释放上一格站位（起点或上一个已 commit 的中间格）
        HexCellData prevCell = _mapDataService.GetCell(mu.CurrentStandingHex);
        HexCellData curCell = _mapDataService.GetCell(mu.Path[pathIndex]);
        if (prevCell != null && prevCell != curCell)
            prevCell.ReleaseStandingUnit(unitGo);

        if (curCell == null) return;

        int slotId = pathIndex < mu.VisualSlots.Count ? mu.VisualSlots[pathIndex] : -1;
        if (curCell.UnitSlots != null)
        {
            curCell.UnitSlots.PromoteReservationToStanding(mu.Unit, slotId, unitGo, curCell.RealCenterWorldCoordinate, out _);
        }

        // 同步旧字段（primary owner）
        if (!curCell.IsHaveUnit()) curCell.SetHaveUnit(true, unitGo);
        if (curCell.GetOccupant() == null) curCell.SetOccupant(unitGo);

        // 释放本节点的预留（已提升为站位）
        if (curCell.UnitSlots != null) curCell.UnitSlots.ReleaseReservation(slotId);
        if (curCell.UnitSlots == null || !curCell.UnitSlots.HasAnyReservation())
            _reservedCells.Remove(mu.Path[pathIndex]);

        mu.CurrentStandingHex = mu.Path[pathIndex];
        mu.CurrentStandingSlot = slotId;
    }

    /* 一、求两点间最小移动力消耗 - 正权无向图求最短路径（Dijkstra）

    【性能重写】原实现每次调用都：
      - 用 allPoints 建一个全图 HashSet + 两个全图 Dictionary，并做 600 次 MaxValue 初始化；
      - 没有目标提前退出，即使终点就在隔壁也要把全图展开完；
      - 成功后还跑一个「已注释掉 Debug.Log」的遗留循环（两次全图 List 拷贝 + 600 次 GetCell）；
      - 每展开一个节点就 new 一个邻居 Dictionary + 2~3 个临时 List。
    现在改为：
      - 复用实例级缓冲区（字典/堆/已访集合），只 Clear 不重建；
      - 代价字典惰性写入（未访问 == 不在字典 == 无穷大），去掉全图初始化；
      - allPoints 成员集按引用缓存（调用方传缓存列表即全程命中）；
      - 出队即终点 → 立即回溯返回；
      - 邻居展开写进定长栈上缓冲区，零分配。

    流程：出队最小代价节点 → 松弛其邻居 → 重复，直到取到终点或队列耗尽。
    */
    // ── 寻路复用缓冲区（主线程单线程使用；两个搜索互不嵌套调用，可安全共用）──
    private readonly Dictionary<Vector3, Vector3> _pfPrev = new Dictionary<Vector3, Vector3>();
    private readonly Dictionary<Vector3, float> _pfCost = new Dictionary<Vector3, float>();
    private readonly HashSet<Vector3> _pfProcessed = new HashSet<Vector3>();
    private readonly MinPriorityQueue _pfQueue = new MinPriorityQueue();
    private readonly Vector3[] _pfNeighborHex = new Vector3[6];
    private readonly float[] _pfNeighborCost = new float[6];

    // allPoints 成员判定集：按传入列表的引用缓存，调用方传 IMapDataService 的缓存列表时全程命中。
    // 附带 Count 校验，捕捉「同一列表被原地增删」的情形（地图重建走的是换引用，同样会失效）。
    private HashSet<Vector3> _pfAllPointsSet;
    private List<Vector3> _pfAllPointsSource;
    private int _pfAllPointsCount;

    private HashSet<Vector3> GetAllPointsSet(List<Vector3> allPoints)
    {
        if (ReferenceEquals(_pfAllPointsSource, allPoints) &&
            _pfAllPointsSet != null &&
            _pfAllPointsCount == allPoints.Count)
        {
            return _pfAllPointsSet;
        }

        _pfAllPointsSet = new HashSet<Vector3>(allPoints);
        _pfAllPointsSource = allPoints;
        _pfAllPointsCount = allPoints.Count;
        return _pfAllPointsSet;
    }

    /// <summary>已知代价，未访问节点视为无穷大。</summary>
    private float CostOf(Vector3 hex)
    {
        return _pfCost.TryGetValue(hex, out float c) ? c : float.MaxValue;
    }

    public bool CalculateMinMovementCostBetweenTwoHexes(
        List<Vector3> allPoints,    //全部点列表
        Vector3 startHexCoordinate, //起点
        Vector3 endHexCoordinate,   //终点
        Enums.MovementPurpose movementPurpose, //移动目的
        int factionId,              //寻路单位的阵营（迷雾判定按此阵营查可见性）
        out float totalCost,
        out List<Vector3> shortestPath
        )
    {
        // 0.起点即终点：无需搜索
        if (startHexCoordinate == endHexCoordinate)
        {
            totalCost = 0;
            shortestPath = null;
            //Debug.Log("起点即为目标点,无需移动");
            return true;
        }

        if (allPoints == null || allPoints.Count == 0)
        {
            totalCost = -1;
            shortestPath = null;
            return false;
        }

        HashSet<Vector3> allPointsSet = GetAllPointsSet(allPoints);

        // 复用缓冲区：只清空，不重建
        _pfPrev.Clear();
        _pfCost.Clear();
        _pfProcessed.Clear();
        _pfQueue.Clear();

        _pfCost[startHexCoordinate] = 0f;
        _pfQueue.Enqueue(startHexCoordinate, 0f);

        // 攻击移动允许把「被占据的终点」当作可进入目标
        Vector3? allowedBlockedTarget = movementPurpose == Enums.MovementPurpose.MoveToAttack
            ? endHexCoordinate
            : (Vector3?)null;

        while (_pfQueue.TryDequeue(out Vector3 current, out float currentPriority))
        {
            // 陈旧副本（无 decrease-key，同一节点可能多次入队）：跳过
            if (_pfProcessed.Contains(current)) continue;
            if (currentPriority > CostOf(current)) continue;
            _pfProcessed.Add(current);

            // 【提前退出】最小堆保证出队即最终代价，取到终点就不必再展开剩余全图
            if (current == endHexCoordinate)
            {
                totalCost = _pfCost[current];
                shortestPath = BuildPath(startHexCoordinate, endHexCoordinate);
                return true;
            }

            float ownCost = _pfCost[current];
            int n = ExpandNeighbors(current, factionId, allowedBlockedTarget);
            for (int i = 0; i < n; i++)
            {
                Vector3 next = _pfNeighborHex[i];
                if (!allPointsSet.Contains(next)) continue;   // 邻居不在搜索域内
                if (_pfProcessed.Contains(next)) continue;

                float newCost = ownCost + _pfNeighborCost[i];
                if (newCost >= CostOf(next)) continue;

                _pfCost[next] = newCost;
                _pfPrev[next] = current;
                _pfQueue.Enqueue(next, newCost);
            }
        }

        // 队列耗尽仍未取到终点 == 不可达
        totalCost = -1;
        shortestPath = null;
        return false;
    }

    /// <summary>按 <see cref="_pfPrev"/> 回溯 start→end 的路径。返回值**不含起点**，含终点。</summary>
    private List<Vector3> BuildPath(Vector3 startHex, Vector3 endHex)
    {
        List<Vector3> path = new List<Vector3>();
        Vector3 cursor = endHex;
        while (cursor != startHex)
        {
            path.Add(cursor);
            if (!_pfPrev.TryGetValue(cursor, out cursor)) break;  // 防御：前驱链断裂
        }
        path.Reverse();
        return path;
    }


    /*二、正权无向图，在花费固定且非负的情况下，求所有从起点开始能够到达的点 - 返回的是六边形坐标

    【性能重写】与上面的两点寻路同源，共用复用缓冲区与零分配邻居展开。
    另外增加**预算剪枝**：代价已超过 totalCost 的节点不再入队/展开 —— 最小堆保证一旦
    出队代价超预算，后续全部超预算，可直接停止。游走（半径 3）这类小预算调用因此
    从「全图洪泛后过滤」降为「只展开半径内的几十格」。

    返回顺序为 Dijkstra 发现顺序（按代价递增），确定且可复现；
    注意与旧实现（allPoints 原序）不同 —— PickRandomWanderTarget 的随机取样因此会落到
    不同的格上，但仍是同种子同结果，可复现性不受影响。
    */
    public List<Vector3> GetAllReachableHexesFromStartHex(List<Vector3> allPoints, Vector3 startHexCoordinate, float totalCost, int factionId)
    {
        List<Vector3> reachableHexes = new List<Vector3>();
        if (allPoints == null || allPoints.Count == 0) return reachableHexes;

        HashSet<Vector3> allPointsSet = GetAllPointsSet(allPoints);
        if (!allPointsSet.Contains(startHexCoordinate)) return reachableHexes;

        _pfPrev.Clear();
        _pfCost.Clear();
        _pfProcessed.Clear();
        _pfQueue.Clear();

        _pfCost[startHexCoordinate] = 0f;
        _pfQueue.Enqueue(startHexCoordinate, 0f);

        while (_pfQueue.TryDequeue(out Vector3 current, out float currentPriority))
        {
            if (_pfProcessed.Contains(current)) continue;
            if (currentPriority > CostOf(current)) continue;

            // 最小堆出队即代价单调不减：首个超预算节点之后不可能再有合格节点
            if (currentPriority > totalCost) break;

            _pfProcessed.Add(current);
            reachableHexes.Add(current);

            float ownCost = currentPriority;
            int n = ExpandNeighbors(current, factionId, null);
            for (int i = 0; i < n; i++)
            {
                Vector3 next = _pfNeighborHex[i];
                if (!allPointsSet.Contains(next)) continue;
                if (_pfProcessed.Contains(next)) continue;

                float newCost = ownCost + _pfNeighborCost[i];
                if (newCost > totalCost) continue;            // 预算剪枝
                if (newCost >= CostOf(next)) continue;

                _pfCost[next] = newCost;
                _pfPrev[next] = current;
                _pfQueue.Enqueue(next, newCost);
            }
        }

        return reachableHexes;
    }

    /// <summary>
    /// 展开 <paramref name="self"/> 的 6 个邻居，写入 <see cref="_pfNeighborHex"/> / <see cref="_pfNeighborCost"/>，
    /// 返回有效邻居数量。零分配（原 GetAllNeighborsAndCosts 每次 new 一个 Dictionary）。
    /// </summary>
    private int ExpandNeighbors(Vector3 self, int factionId, Vector3? targetHex)
    {
        int count = 0;
        HexCellData selfCell = _mapDataService.GetCell(self);
        if (selfCell == null) return 0;

        for (int dir = 0; dir < 6; dir++)   // NE, E, SE, SW, W, NW —— 与 Enums.HexDirection 前 6 项一致
        {
            HexCellData neighborCell = _mapDataService.GetNeighbor(selfCell, (Enums.HexDirection)dir);

            // 不存在邻居
            if (neighborCell == null) continue;

            bool isTarget = targetHex.HasValue && neighborCell.HexCoordinate == targetHex.Value;
            if (!CanEnterCell(neighborCell, null, isTarget, factionId))
            {
                // 攻击移动的终点格即使不可进入也要可达（走到即开打），代价按 1 计
                if (isTarget)
                {
                    _pfNeighborHex[count] = neighborCell.HexCoordinate;
                    _pfNeighborCost[count] = 1f;
                    count++;
                }
                continue;
            }

            _pfNeighborHex[count] = neighborCell.HexCoordinate;
            _pfNeighborCost[count] = neighborCell.movementCost;
            count++;
        }

        return count;
    }

    /// <summary>从单位 GameObject 解析阵营 id（玩家 0 / AI 1…）。优先 UnitMovementController.PlayerIndex，缺失时退回 tag 判定。</summary>
    private static int FactionOf(GameObject unit)
    {
        if (unit == null) return 0;
        var umc = unit.GetComponent<UnitMovementController>();
        if (umc != null && umc.PlayerIndex >= 0) return umc.PlayerIndex;
        return unit.CompareTag("PlayerUnit") ? 0 : 1;
    }

    /// <summary>全局迷雾判定：按 <paramref name="factionId"/> 阵营查可见性（含竞技场 lease 等临时点亮），
    /// resolver 缺失时退回“任一阵营已探索”。修复：旧实现硬编码 faction 0，导致 AI（faction 1）寻路被玩家迷雾卡住。</summary>
    private bool IsFogFree(HexCellData cell, int factionId)
    {
        return _visibilityResolver != null
            ? _visibilityResolver.IsVisibleToFaction(cell, factionId)
            : cell.IsExploredByAnyFaction;
    }

    private bool CanEnterCell(HexCellData cell, GameObject movingUnit, bool allowOccupiedTarget, int factionId)
    {
        if (cell == null || cell.movementCost < 0f || float.IsNaN(cell.movementCost) || float.IsInfinity(cell.movementCost) || cell.movementCost == float.MaxValue)
        {
            return false;
        }

        // 山体资格必须独立于缓存的 movementCost 判断；显式代价不能覆盖有效山体的不可通行规则。
        if (MountainCellRule.IsEffectiveMountainCell(cell))
        {
            return false;
        }

        // 【迷雾决定进入】有迷雾不可进、无迷雾可进；探索只是解锁迷雾的一种手段。
        if (!IsFogFree(cell, factionId))
        {
            return false;
        }

        // 【多单位】通行改按有效容量判断：仍有自由站位槽即可进；单位自身所在格 / 攻击目标格例外。
        if (movingUnit != null && cell.HasStandingUnit(movingUnit)) return true;
        if (allowOccupiedTarget) return true;
        return cell.HasFreeStandingSlot();
    }

    // 内部数据结构
    private class MovingUnit
    {
        public IUnitMovement Unit;
        public List<Vector3> Path;                 // 逻辑路径（不含起点）
        public List<int> VisualSlots;              // 每个 Path[i] 对应的视觉槽位 id
        public int CurrentPathIndex;               // 下一个待抵达的路径节点
        public Enums.MovementPurpose Purpose;
        public Vector3 StartHex;
        public int StartSlot;                      // 起点站位槽（取消时恢复）
        public Vector3 CurrentStandingHex;         // 单位当前逻辑站位格（随逐格 commit 更新）
        public int CurrentStandingSlot;
        public Vector3 DestinationHex;
        public int DestinationSlot;
        public float StartRemainingMovement;
        public float MoveSpeed;                    // 实时移动速度（世界单位/秒），基于 UnitData.MovementPoints * 10
        public List<KeyValuePair<Vector3, int>> ReservedNodes; // 本任务预留的全部 (cell, slotId)
    }
}
