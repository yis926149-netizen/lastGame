// UnitMovementSystem.cs
using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class UnitMovementSystem : ITickable
{
    private readonly IMapDataService _mapDataService;
    private readonly MapVisualEventSO _mapVisualEvent;  // 用于触发视觉更新

    // 正在移动的单位列表
    private List<MovingUnit> _movingUnits = new List<MovingUnit>();

    public UnitMovementSystem(IMapDataService mapDataService, MapVisualEventSO mapVisualEvent)
    {
        _mapDataService = mapDataService;
        _mapVisualEvent = mapVisualEvent;
    }

    /// <summary>
    /// 请求单位移动
    /// </summary>
    public bool RequestMove(IUnitMovement unit, Vector3 targetHex, Enums.MovementPurpose purpose)
    {
        //Debug.Log($"[UnitMovementSystem] RequestMove: unit={unit.gameObject.name}, target={targetHex}, purpose={purpose}");

        if (unit.RemainingMovement <= 0)
        {
            Debug.LogWarning("[UnitMovementSystem] RequestMove rejected: no movement points.");
            return false;
        }

        // 1. 计算原始最短路径
        if (!CalculateMinMovementCostBetweenTwoHexes(
            new List<Vector3>(_mapDataService.GetAllHexCoordinates()),
            unit.CurrentHexCoordinate,
            targetHex,
            purpose,
            out float _,
            out List<Vector3> path))
        {
            Debug.LogWarning($"[UnitMovementSystem] RequestMove failed: cannot reach target.");
            return false;
        }

        // 2. 路径截断逻辑修复
        List<Vector3> actualPath = new List<Vector3>(path ?? new List<Vector3>());

        if (purpose == Enums.MovementPurpose.MoveToAttack)
        {
            // 只要是攻击移动，且路径里有目标点，就必须移除最后一位（敌方地块）
            if (actualPath.Count > 0)
            {
                actualPath.RemoveAt(actualPath.Count - 1);
            }
        }

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

        if (actualCost > unit.RemainingMovement)
        {
            Debug.LogWarning($"[UnitMovementSystem] RequestMove rejected: cost {actualCost} > remaining {unit.RemainingMovement}.");
            return false;
        }

        // 5. 提交移动任务
        var startCell = _mapDataService.GetCell(unit.CurrentHexCoordinate);
        startCell?.SetHaveUnit(false, unit.gameObject);

        var movingUnit = new MovingUnit
        {
            Unit = unit,
            Path = actualPath,
            CurrentPathIndex = 0,
            Purpose = purpose
        };
        _movingUnits.Add(movingUnit);

        return true;
    }

    // Zenject 每帧调用（Tick）
    public void Tick()
    {
        //Debug.Log($"[UnitMovementSystem] Tick called, moving units count: {_movingUnits.Count}");

        for (int i = _movingUnits.Count - 1; i >= 0; i--)
        {
            var mu = _movingUnits[i];
            if (mu.Unit == null || mu.Unit.gameObject == null)
            {
                _movingUnits.RemoveAt(i);
                continue;
            }

            bool finished = UpdateMovement(mu);
            if (finished)
            {
                // 移动完成，回调单位
                mu.Unit.OnMoveFinished();
                _movingUnits.RemoveAt(i);
            }
        }
    }

    // 单步移动更新
    /// <summary>
    /// 单步移动更新
    /// </summary>
    /// <param name="mu">正在移动的单位数据</param>
    /// <returns>true 表示移动完成（到达终点或移动力耗尽），false 表示仍在移动中</returns>
    private bool UpdateMovement(MovingUnit mu)
    {
        const float moveSpeed = 20f;
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

            // 扣除当前格子的移动力（所有经过的格子都要扣）
            float cost = _mapDataService.GetCell(mu.Path[mu.CurrentPathIndex]).movementCost;
            mu.Unit.RemainingMovement -= cost;
            //Debug.Log($"[UpdateMovement] 扣除移动力 {cost}，剩余 {mu.Unit.RemainingMovement}");

            // 移动力耗尽，停止在此格子，标记完成
            if (mu.Unit.RemainingMovement <= 0)
            {
                mu.CurrentPathIndex = mu.Path.Count;
                return true;
            }

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
                    Debug.Log("目标点不可达");
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
            Dictionary<Vector3, float> neighbor_Cost = GetAllNeighborsAndCosts(A.Key, _mapDataService, endHexCoordinate);
            //若A的邻接点不在allPoints内，则剔除出neighbor_Cost
            List<Vector3> neighbor_CostKeysList = new List<Vector3>(neighbor_Cost.Keys);
            List<Vector3> toRemove = new List<Vector3>();
            foreach (var key in neighbor_Cost.Keys)
            {
                if (!allPoints.Contains(key))
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
                if (!allPoints.Contains(key))
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

            // 邻居不可通行
            if (neighborCell.movementCost == -1)
            {
                // 核心修复：如果这个不可通行的格子正是我们要寻找的终点，破例允许加入（赋予基础花费1让路径连通）
                if (targetHex.HasValue && neighborCell.HexCoordinate == targetHex.Value)
                {
                    d.Add(neighborCell.HexCoordinate, 1f);
                }
                continue;
            }

            d.Add(neighborCell.HexCoordinate, neighborCell.movementCost);
        }

        return d;
    }

    // 内部数据结构
    private class MovingUnit
    {
        public IUnitMovement Unit;
        public List<Vector3> Path;
        public int CurrentPathIndex;
        public Enums.MovementPurpose Purpose;
    }
}