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

    // 正在移动的单位列表
    private List<MovingUnit> _movingUnits = new List<MovingUnit>();
    private readonly Dictionary<Vector3, IUnitMovement> _reservedDestinations = new Dictionary<Vector3, IUnitMovement>();
    private List<Vector3> _cachedAllPoints;

    public UnitMovementSystem(IMapDataService mapDataService, MapVisualEventSO mapVisualEvent, GameLoop gameLoop, [InjectOptional] ILogisticsService logisticsService = null, [InjectOptional] IMapInteractionGate interactionGate = null)
    {
        _mapDataService = mapDataService;
        _mapVisualEvent = mapVisualEvent;
        _gameLoop = gameLoop;
        _logisticsService = logisticsService;
        _interactionGate = interactionGate;
    }

    public IReadOnlyList<Vector3> AllHexCoordinates
    {
        get
        {
            if (_cachedAllPoints == null)
                _cachedAllPoints = _mapDataService.GetAllHexCoordinates();
            return _cachedAllPoints;
        }
    }

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

        if (_reservedDestinations.TryGetValue(destinationHex, out var reservingUnit) && reservingUnit != unit)
        {
            return false;
        }

        var destinationCell = _mapDataService.GetCell(destinationHex);
        if (destinationCell == null || (destinationCell.IsHaveUnit() && destinationCell.GetUnit() != unit.gameObject))
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
            new List<Vector3>(AllHexCoordinates),
            startHex,
            destinationHex,
            purpose,
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
        float computedSpeed = Mathf.Max(1f, baseMovementPoints * 10f);

        var movingUnit = new MovingUnit
        {
            Unit = unit,
            Path = actualPath,
            CurrentPathIndex = 0,
            Purpose = purpose,
            StartHex = startHex,
            DestinationHex = destinationHex,
            StartRemainingMovement = unit.RemainingMovement,
            MoveSpeed = computedSpeed
        };
        _reservedDestinations[destinationHex] = unit;
        _movingUnits.Add(movingUnit);

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
        return _reservedDestinations.ContainsKey(hexCoordinate);
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

        List<Vector3> allPoints = new List<Vector3>(_mapDataService.GetAllHexCoordinates());
        float bestCost = float.MaxValue;
        bool found = false;
        HexCellData targetCell = _mapDataService.GetCell(targetHex);
        if (targetCell == null) return false;

        for (int i = 0; i < 6; i++)
        {
            HexCellData neighbor = _mapDataService.GetNeighbor(targetCell, (Enums.HexDirection)i);
            if (neighbor == null || !CanEnterCell(neighbor, unit.gameObject, false)) continue;
            if (_reservedDestinations.TryGetValue(neighbor.HexCoordinate, out var reservingUnit) && reservingUnit != unit) continue;

            if (CalculateMinMovementCostBetweenTwoHexes(
                allPoints,
                unit.CurrentHexCoordinate,
                neighbor.HexCoordinate,
                Enums.MovementPurpose.MoveToDestination,
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
        HexCellData startCell = _mapDataService.GetCell(movingUnit.StartHex);
        HexCellData destinationCell = _mapDataService.GetCell(movingUnit.DestinationHex);

        if (startCell != null && startCell.GetUnit() == movingUnit.Unit.gameObject && startCell != destinationCell)
        {
            startCell.SetHaveUnit(false, null);
            // 【批次 D】同步释放旧格的 Occupant
            if (startCell.GetOccupant() == movingUnit.Unit.gameObject)
                startCell.SetOccupant(null);
        }
        destinationCell?.SetHaveUnit(true, movingUnit.Unit.gameObject);
        // 【批次 D】同步写入新格的 Occupant
        destinationCell?.SetOccupant(movingUnit.Unit.gameObject);
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
        HexCellData startCell = _mapDataService.GetCell(movingUnit.StartHex);
        if (startCell == null || (startCell.IsHaveUnit() && startCell.GetUnit() != movingUnit.Unit.gameObject)) return;

        movingUnit.Unit.gameObject.transform.position = startCell.RealCenterWorldCoordinate;
        movingUnit.Unit.RemainingMovement = movingUnit.StartRemainingMovement;
        startCell.SetHaveUnit(true, movingUnit.Unit.gameObject);
        // 【批次 D】恢复 Occupant
        startCell.SetOccupant(movingUnit.Unit.gameObject);
    }

    private void ReleaseReservation(MovingUnit movingUnit)
    {
        if (_reservedDestinations.TryGetValue(movingUnit.DestinationHex, out var unit) && unit == movingUnit.Unit)
        {
            _reservedDestinations.Remove(movingUnit.DestinationHex);
        }
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
    /// 弹射：把站立在不可通行格上的单位迁到最近的"可通行且无占用"格（6 向 BFS）。
    /// 纯位置迁移——不触发 TryCaptureEnemyCell、不写归属（决策 B）。
    /// 原子迁移四套占用状态：HaveUnit / Occupant / 攻击槽（本格）/ 目的地预占。
    /// </summary>
    public void EjectUnitsFromImpassableCells(IReadOnlyCollection<HexCellData> cells)
    {
        if (cells == null) return;

        foreach (HexCellData cell in cells)
        {
            if (cell == null || cell.movementCost != float.MaxValue) continue;

            GameObject unit = cell.GetOccupant() ?? cell.GetUnit();
            if (unit == null) continue;

            if (FindNearestFreePassableCell(cell, unit, out HexCellData target))
            {
                MigrateOccupancy(cell, target, unit);
            }
            else
            {
                // 无可用落点（极端情况）：仍释放占位，避免"格内双单位"读错状态
                cell.SetHaveUnit(false, null);
                if (cell.GetOccupant() == unit) cell.SetOccupant(null);
                cell.ReleaseAttackerSlots(unit);
                Debug.LogWarning($"[UnitMovementSystem] Eject: 找不到可弹射落点，单位 {unit.name} 释放占用（无归属迁移）。");
            }
        }
    }

    /// <summary>6 向 BFS：从 cell 出发找最近"可通行（movementCost &lt; MaxValue）且无占用"格。</summary>
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
                    !neighbor.IsHaveUnit() &&
                    !neighbor.HasOccupant())
                {
                    result = neighbor;
                    return true;
                }
                queue.Enqueue(neighbor);
            }
        }
        return false;
    }

    /// <summary>把单位从 source 原子迁移到 target：占用状态 + transform.position。</summary>
    private void MigrateOccupancy(HexCellData source, HexCellData target, GameObject unit)
    {
        source.SetHaveUnit(false, null);
        if (source.GetOccupant() == unit) source.SetOccupant(null);
        source.ReleaseAttackerSlots(unit);

        target.SetHaveUnit(true, unit);
        target.SetOccupant(unit);

        unit.transform.position = target.RealCenterWorldCoordinate;
    }

    /// <summary>变化格上的站立单位吸附到新 RealCenterWorldCoordinate（移动中单位自然逐点跟随，跳过）。</summary>
    public void RefreshStandingUnitPositions(IReadOnlyCollection<HexCellData> changedCells)
    {
        if (changedCells == null) return;

        foreach (HexCellData cell in changedCells)
        {
            if (cell == null || cell.movementCost == float.MaxValue) continue;

            GameObject unit = cell.GetOccupant() ?? cell.GetUnit();
            if (unit == null) continue;
            if (IsUnitMoving(unit)) continue;

            unit.transform.position = cell.RealCenterWorldCoordinate;
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
        const float rotationSpeed = 5f;
        Transform trans = mu.Unit.gameObject.transform;

        if (mu.CurrentPathIndex >= mu.Path.Count)
            return true;

        Vector3 targetPos = _mapDataService.GetCell(mu.Path[mu.CurrentPathIndex]).RealCenterWorldCoordinate;
        float distance = Vector3.Distance(trans.position, targetPos);

        if (distance < 0.1f)
        {
            // 吸附到当前路径点中心，避免浮点漂移
            trans.position = targetPos;

            // 【实时化】移动力配额已废除，不再扣减 RemainingMovement，也不再因耗尽而截断路径。

            // 移动到下一个节点
            mu.CurrentPathIndex++;

            if (mu.CurrentPathIndex >= mu.Path.Count)
                return true;

            targetPos = _mapDataService.GetCell(mu.Path[mu.CurrentPathIndex]).RealCenterWorldCoordinate;
        }

        // 使用 MoveTowards：单帧步长若大于剩余距离，会精确落到目标点，避免越过目标后在阈值外来回振荡（导致永远无法完成移动）
        float step = moveSpeed * Time.deltaTime;
        Vector3 direction = targetPos - trans.position;
        trans.position = Vector3.MoveTowards(trans.position, targetPos, step);

        if (direction != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
            trans.rotation = Quaternion.Slerp(trans.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }

        return false;
    }

    /* 一、求两点间最小移动力消耗 - 正权无向图求最短路径
    初始设置：
    1.设置全点列表 List<Vector3> allPoints - 储存全部点
    2.设置一个字典 Dictionary<point,pre> point_pre - 保存每个点及其前驱 - 初始化为空
    3.设置一个字典 Dictionary<Vector3,float> Point_minCost - 保存每个点及其到达花费 - 初始化：起点到起点花费为0,即(起点,0). 到其他点花费为float.MaxValue,即(allPoints[i],float.MaxValue)
    4.设置一个优先队列 PriorityQueue<KeyValuePair<Vector3,float>, float> candidates - PriorityQueue<KeyValuePair<点,到达时花费>, 到达时花费(优先级)>
     - 起点入队(初始唯一候选).
    5.设置已访列表 List<Vector3> processedNodes - 保存已访问的点

    流程：(获取新点 - 全局比较 - 选择新点)
    0.若起点 == 终点,进入7.

    1.检查candidates.Count
    若不为0：进入2.
    若为0：进入6.
    2.candidates元素出队获取点A. 
    若 Point_minCost[A.key] < A.value,则回到1.的开头
    3.获取点A的全部邻接点及其花费 Dictionary<Vector3,float> neighbor_Cost
        若A的邻接点不在allPoints内，则剔除出neighbor_Cost
    4.Point_minCost[K] = (neighbor_Cost[K] + Point_minCost[A.key]) < Point_minCost[K]?(neighbor_Cost[K] + Point_minCost[A.key]):Point_minCost[K]
             若(neighbor_Cost[K] + Point_minCost[A.key]) < Point_minCost[K]时：point_pre[neighbor_Cost的K值] = A.key
    5.将neighbor_Cost中的元素,构造((neighbor_Cost的K值,neighbor_Cost[k] + Point_minCost[A.key]),neighbor_Cost[k] + Point_minCost[A.key])全部加入candidates

    6.停止迭代. 
    if(Point_minCost[终点] != float.MaxValue){
        输出Point_minCost[终点]
        回溯的最短路径
    }
    else{
        输出-1
        最短路径为null
        Debug.Log("目标点不可达")
    }
    7.停止迭代. 输出0,最短路径为null,Debug.Log("起点即为目标点,无需移动")
    */
    public bool CalculateMinMovementCostBetweenTwoHexes(
        List<Vector3> allPoints,    //全部点列表
        Vector3 startHexCoordinate, //起点
        Vector3 endHexCoordinate,   //终点
        Enums.MovementPurpose movementPurpose, //移动目的 
        out float totalCost,
        out List<Vector3> shortestPath
        )
    {
        //初始设置：
        //1.设置全点列表 List<Vector3> allPoints -储存全部点
        // allPoints 转 HashSet，使后续邻居有效性判断从 O(N) 线性查找降为 O(1)
        HashSet<Vector3> allPointsSet = new HashSet<Vector3>(allPoints);
        //2.设置一个字典 Dictionary<point, pre> point_pre -保存每个点及其前驱 - 初始化为空
        Dictionary<Vector3, Vector3> point_pre = new Dictionary<Vector3, Vector3>();
        Vector3 over = new Vector3(-111111111111111, -111111111111111111, -11111111111111111);
        point_pre.Add(startHexCoordinate, over);
        //3.设置一个字典 Dictionary<Vector3, float> Point_minCost -保存每个点及其到达花费 
        Dictionary<Vector3, float> Point_minCost = new Dictionary<Vector3, float>();
        //初始化：起点到起点花费为0,即(起点, 0).到其他点花费为float.MaxValue,即(allPoints[i], float.MaxValue)
        foreach (Vector3 point in allPoints)
        {
            if (point == startHexCoordinate)
            {
                Point_minCost.Add(startHexCoordinate, 0);
                continue;
            }
            Point_minCost.Add(point, float.MaxValue);
        }
        //4.设置一个优先队列 PriorityQueue<KeyValuePair<Vector3, float>, float> candidates -PriorityQueue < KeyValuePair<点, 到达时花费>, 到达时花费(优先级) >
        MinPriorityQueue candidates = new MinPriorityQueue();
        //起点入队(初始唯一候选).
        KeyValuePair<Vector3, float> startKeyValue = new KeyValuePair<Vector3, float>(startHexCoordinate, 0);
        candidates.Enqueue(startKeyValue, 0);
        //5.设置已访列表 HashSet<Vector3> processedNodes - 保存已访问的点 - 用HashSet提高查找效率
        HashSet<Vector3> processedNodes = new HashSet<Vector3>();

        //流程：(获取新点 - 全局比较 - 选择新点)
        while (true)
        {
            //repeatTimes++;
            //0.若起点 == 终点,进入7.
            if (startHexCoordinate == endHexCoordinate)
            {
                totalCost = 0;
                shortestPath = null;
                //Debug.Log("起点即为目标点,无需移动");
                return true;
            }

            //1.检查candidates.Count
            //若不为0：进入2.
            //若为0：进入6.
            if (candidates.Count == 0)
            {
                //6.停止迭代.
                if (Point_minCost[endHexCoordinate] != float.MaxValue)
                {
                    //输出花费
                    totalCost = Point_minCost[endHexCoordinate];
                    //回溯最短路径
                    List<Vector3> VisitedPoint_minCostKeysList = new List<Vector3>(Point_minCost.Keys);
                    List<float> VisitedPoint_minCostValueList = new List<float>(Point_minCost.Values);

                    //测试 - 输出全部VisitedPoint_minCost
                    for (int i = 0; i < Point_minCost.Count; i++)
                    {
                        int g = _mapDataService.GetCell(VisitedPoint_minCostKeysList[i]).GenerateOrder;
                        //Debug.Log($"第{g}个地块：总最小花费是{VisitedPoint_minCostValueList[i]}");
                    }

                    shortestPath = new List<Vector3>();
                    Vector3 indexPoint = endHexCoordinate;
                    shortestPath.Add(endHexCoordinate);
                    while (point_pre[indexPoint] != over)
                    {
                        shortestPath.Add(point_pre[indexPoint]);
                        indexPoint = point_pre[indexPoint];
                    }
                    shortestPath.Reverse();
                    shortestPath.RemoveAt(0);

                    // 攻击移动时截断最后一个格子
                    /*
                    if (movementPurpose == Enums.MovementPurpose.MoveToAttack && shortestPath.Count > 1)
                    {
                        shortestPath.RemoveAt(shortestPath.Count - 1);
                    }
                    */
                    return true;  
                }
                else
                {
                    totalCost = -1;
                    shortestPath = null;
                    return false;
                }

            }

            //2.candidates元素出队获取点A.
            //若 Point_minCost[A.key] < A.value,则回到1.的开头
            KeyValuePair<Vector3, float> A = new KeyValuePair<Vector3, float>();
            while (candidates.Count > 0)
            {
                A = candidates.Dequeue();
                if (processedNodes.Contains(A.Key)) continue; // 跳过已处理节点
                if (!(Point_minCost[A.Key] < A.Value)) break;
            }
            if (processedNodes.Contains(A.Key)) continue; // 再次检查，避免空队列情况
            processedNodes.Add(A.Key); // 标记为已处理

            //3.获取点A的全部邻接点及其花费 Dictionary<Vector3, float> neighbor_Cost
            Vector3? allowedBlockedTarget = movementPurpose == Enums.MovementPurpose.MoveToAttack
                ? endHexCoordinate
                : (Vector3?)null;
            Dictionary<Vector3, float> neighbor_Cost = GetAllNeighborsAndCosts(A.Key, _mapDataService, allowedBlockedTarget);
            //若A的邻接点不在allPoints内，则剔除出neighbor_Cost
            List<Vector3> neighbor_CostKeysList = new List<Vector3>(neighbor_Cost.Keys);
            List<Vector3> toRemove = new List<Vector3>();
            foreach (var key in neighbor_Cost.Keys)
            {
                if (!allPointsSet.Contains(key))
                {
                    toRemove.Add(key);
                }
            }
            foreach (var key in toRemove)
            {
                neighbor_Cost.Remove(key);
            }
            //获取剔除后,有效邻居的Keys
            neighbor_CostKeysList = new List<Vector3>(neighbor_Cost.Keys);

            //4.Point_minCost[K] = (neighbor_Cost[K] + Point_minCost[A.key]) < Point_minCost[K] ? (neighbor_Cost[K] + Point_minCost[A.key]) : Point_minCost[K]            
            float ownCost = Point_minCost[A.Key];
            for (int i = neighbor_CostKeysList.Count - 1; i >= 0; i--)
            {
                Vector3 index = neighbor_CostKeysList[i];
                float newCost = neighbor_Cost[index] + ownCost;
                float oldCost = Point_minCost[index];

                Point_minCost[index] = newCost < oldCost ? newCost : Point_minCost[index];
                if (newCost < oldCost)
                {
                    //若(neighbor_Cost[K] + Point_minCost[A.key]) < Point_minCost[K]时：point_pre[neighbor_Cost的K值] = A.key
                    point_pre[index] = A.Key;
                }
            }

            //5.将neighbor_Cost中的元素,构造((neighbor_Cost的K值, neighbor_Cost[k] + Point_minCost[A.key]), neighbor_Cost[k] + Point_minCost[A.key])全部加入candidates
            for (int i = 0; i < neighbor_Cost.Count; i++)
            {
                KeyValuePair<Vector3, float> keyValue = new KeyValuePair<Vector3, float>(
                    neighbor_CostKeysList[i],
                    neighbor_Cost[neighbor_CostKeysList[i]] + ownCost
                );
                candidates.Enqueue(keyValue, neighbor_Cost[neighbor_CostKeysList[i]] + ownCost);
            }
        }

    }


    /*二、正权无向图，在花费固定且非负的情况下，求所有从起点开始能够到达的点 - 返回的是六边形坐标
    初始设置：
    1.设置全点列表 List<Vector3> allPoints - 储存全部点
    3.设置一个字典 Dictionary<point,pre> point_pre - 保存每个点及其前驱 - 初始化为空
    4.设置一个字典 Dictionary<Vector3,float> Point_minCost - 保存每个点及其到达花费 - 初始化：起点到起点花费为0,即(起点,0). 到其他点花费为float.MaxValue,即(allPoints[i],float.MaxValue)
    5.设置一个优先队列 PriorityQueue<KeyValuePair<Vector3,float>, float> candidates - PriorityQueue<KeyValuePair<点,到达时花费>, 到达时花费(优先级)>
     - 起点入队(初始唯一候选).


    流程：(获取新点 - 全局比较 - 选择新点)
    1.检查candidates.Count
    若不为0：进入2.
    若为0：进入6.
    2.candidates元素出队获取点A. 
    若 Point_minCost[A.key] < A.value,则回到1.的开头
    3.获取点A的全部邻接点及其花费 Dictionary<Vector3,float> neighbor_Cost
        若A的邻接点不在allPoints内，则剔除出neighbor_Cost
    4.Point_minCost[K] = (neighbor_Cost[K] + Point_minCost[A.key]) < Point_minCost[K]?(neighbor_Cost[K] + Point_minCost[A.key]):Point_minCost[K]
             若(neighbor_Cost[K] + Point_minCost[A.key]) < Point_minCost[K]时：point_pre[neighbor_Cost的K值] = A.key
    5.将neighbor_Cost中的元素,构造((neighbor_Cost的K值,neighbor_Cost[k] + Point_minCost[A.key]),neighbor_Cost[k] + Point_minCost[A.key])全部加入candidates

    6.停止迭代. 
    输出 Point_minCost.value < 花费 的Point_minCost.key
    */
    public List<Vector3> GetAllReachableHexesFromStartHex(List<Vector3> allPoints, Vector3 startHexCoordinate, float totalCost)
    {
        //初始设置：
        //1.设置全点列表 List<Vector3> allPoints -储存全部点
        // allPoints 转 HashSet，使后续邻居有效性判断从 O(N) 线性查找降为 O(1)
        HashSet<Vector3> allPointsSet = new HashSet<Vector3>(allPoints);
        //2.设置一个字典 Dictionary<point, pre> point_pre -保存每个点及其前驱 - 初始化为空
        Dictionary<Vector3, Vector3> point_pre = new Dictionary<Vector3, Vector3>();
        Vector3 over = new Vector3(-111111111111111, -111111111111111111, -11111111111111111);
        point_pre.Add(startHexCoordinate, over);
        //3.设置一个字典 Dictionary<Vector3, float> Point_minCost -保存每个点及其到达花费 
        Dictionary<Vector3, float> Point_minCost = new Dictionary<Vector3, float>();
        //初始化：起点到起点花费为0,即(起点, 0).到其他点花费为float.MaxValue,即(allPoints[i], float.MaxValue)
        foreach (Vector3 point in allPoints)
        {
            if (point == startHexCoordinate)
            {
                Point_minCost.Add(startHexCoordinate, 0);
                continue;
            }
            Point_minCost.Add(point, float.MaxValue);
        }
        //4.设置一个优先队列 PriorityQueue<KeyValuePair<Vector3, float>, float> candidates -PriorityQueue < KeyValuePair<点, 到达时花费>, 到达时花费(优先级) >
        MinPriorityQueue candidates = new MinPriorityQueue();
        //起点入队(初始唯一候选).
        KeyValuePair<Vector3, float> startKeyValue = new KeyValuePair<Vector3, float>(startHexCoordinate, 0);
        candidates.Enqueue(startKeyValue, 0);
        //5.设置已访列表 HashSet<Vector3> processedNodes - 保存已访问的点 - 用HashSet提高查找效率
        HashSet<Vector3> processedNodes = new HashSet<Vector3>();

        //流程：(获取新点 - 全局比较 - 选择新点)
        while (true)
        {
            //1.检查candidates.Count
            //若不为0：进入2.
            //若为0：进入6.
            if (candidates.Count == 0)
            {
                //6.停止迭代 - 输出 Point_minCost.value < 花费 的Point_minCost.key
                List<float> Point_minCostValuesList = new List<float>(Point_minCost.Values);
                List<Vector3> Point_minCostKeysList = new List<Vector3>(Point_minCost.Keys);
                List<Vector3> reachableHexes = new List<Vector3>();

                for (int i = 0; i < Point_minCostKeysList.Count; i++)
                {
                    if (Point_minCostValuesList[i] <= totalCost)
                    {
                        reachableHexes.Add(Point_minCostKeysList[i]);
                    }
                }
                return reachableHexes;
            }

            //2.candidates元素出队获取点A.
            //若 Point_minCost[A.key] < A.value,则回到1.的开头
            KeyValuePair<Vector3, float> A = new KeyValuePair<Vector3, float>();
            while (candidates.Count > 0)
            {
                A = candidates.Dequeue();
                if (processedNodes.Contains(A.Key)) continue; // 跳过已处理节点
                if (!(Point_minCost[A.Key] < A.Value)) break;
            }
            if (processedNodes.Contains(A.Key)) continue; // 再次检查，避免空队列情况
            processedNodes.Add(A.Key); // 标记为已处理

            //3.获取点A的全部邻接点及其花费 Dictionary<Vector3, float> neighbor_Cost
            Dictionary<Vector3, float> neighbor_Cost = GetAllNeighborsAndCosts(A.Key, _mapDataService, null);
            //若A的邻接点不在allPoints内，则剔除出neighbor_Cost
            List<Vector3> neighbor_CostKeysList = new List<Vector3>(neighbor_Cost.Keys);
            List<Vector3> toRemove = new List<Vector3>();
            foreach (var key in neighbor_Cost.Keys)
            {
                if (!allPointsSet.Contains(key))
                {
                    toRemove.Add(key);
                }
            }
            foreach (var key in toRemove)
            {
                neighbor_Cost.Remove(key);
            }
            //获取剔除后,有效邻居的Keys
            neighbor_CostKeysList = new List<Vector3>(neighbor_Cost.Keys);

            //4.Point_minCost[K] = (neighbor_Cost[K] + Point_minCost[A.key]) < Point_minCost[K] ? (neighbor_Cost[K] + Point_minCost[A.key]) : Point_minCost[K]            
            float ownCost = Point_minCost[A.Key];
            for (int i = neighbor_CostKeysList.Count - 1; i >= 0; i--)
            {
                Vector3 index = neighbor_CostKeysList[i];
                float newCost = neighbor_Cost[index] + ownCost;
                float oldCost = Point_minCost[index];

                Point_minCost[index] = newCost < oldCost ? newCost : Point_minCost[index];
                if (newCost < oldCost)
                {
                    //若(neighbor_Cost[K] + Point_minCost[A.key]) < Point_minCost[K]时：point_pre[neighbor_Cost的K值] = A.key
                    point_pre[index] = A.Key;
                }
            }

            //5.将neighbor_Cost中的元素,构造((neighbor_Cost的K值, neighbor_Cost[k] + Point_minCost[A.key]), neighbor_Cost[k] + Point_minCost[A.key])全部加入candidates
            for (int i = 0; i < neighbor_Cost.Count; i++)
            {
                KeyValuePair<Vector3, float> keyValue = new KeyValuePair<Vector3, float>(
                    neighbor_CostKeysList[i],
                    neighbor_Cost[neighbor_CostKeysList[i]] + ownCost
                );
                candidates.Enqueue(keyValue, neighbor_Cost[neighbor_CostKeysList[i]] + ownCost);
            }
        }
    }

    //获取主体(默认为起点)的全部邻接点及其花费
    private Dictionary<Vector3, float> GetAllNeighborsAndCosts(Vector3 self, IMapDataService _mapDataService, Vector3? targetHex = null)
    {
        Dictionary<Vector3, float> d = new Dictionary<Vector3, float>();

        Enums.HexDirection[] hexDirections = { Enums.HexDirection.NE, Enums.HexDirection.E, Enums.HexDirection.SE, Enums.HexDirection.SW, Enums.HexDirection.W, Enums.HexDirection.NW };
        foreach (Enums.HexDirection h in hexDirections)
        {
            var neighborCell = _mapDataService.GetNeighbor(_mapDataService.GetCell(self), h);

            // 不存在邻居
            if (neighborCell == null) continue;

            bool isTarget = targetHex.HasValue && neighborCell.HexCoordinate == targetHex.Value;
            if (!CanEnterCell(neighborCell, null, isTarget))
            {
                if (isTarget)
                {
                    d.Add(neighborCell.HexCoordinate, 1f);
                }
                continue;
            }

            d.Add(neighborCell.HexCoordinate, neighborCell.movementCost);
        }

        return d;
    }

    private static bool CanEnterCell(HexCellData cell, GameObject movingUnit, bool allowOccupiedTarget)
    {
        if (cell == null || cell.movementCost < 0f || float.IsNaN(cell.movementCost) || float.IsInfinity(cell.movementCost) || cell.movementCost == float.MaxValue)
        {
            return false;
        }

        if (!cell.IsHaveUnit()) return true;
        return cell.GetUnit() == movingUnit || allowOccupiedTarget;
    }

    // 内部数据结构
    private class MovingUnit
    {
        public IUnitMovement Unit;
        public List<Vector3> Path;
        public int CurrentPathIndex;
        public Enums.MovementPurpose Purpose;
        public Vector3 StartHex;
        public Vector3 DestinationHex;
        public float StartRemainingMovement;
        public float MoveSpeed;       // 实时移动速度（世界单位/秒），基于 UnitData.MovementPoints * 10
    }
}
