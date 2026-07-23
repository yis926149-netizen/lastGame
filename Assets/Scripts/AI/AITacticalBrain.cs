using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

//****************************************
//功能说明：AI 战术脑。负责 AI 回合内单个单位的行动决策：开箱、移民建城、目标获取（受 AI 逻辑迷雾约束）、
//         追击/攻击、无目标时前沿游走。逻辑与拆分前 AIManager 的战术相关方法一致。
//         注：AIIndex 暂固定 1；Tier 3 多阵营化时改为按 aiIndex 参数化。
//****************************************

public class AITacticalBrain
{
    private const int AIIndex = 1;

    private readonly IMapDataService _mapDataService;
    private readonly UnitMovementSystem _movementSystem;
    private readonly IUnitRepository _unitRepository;
    private readonly AIFogService _aiFog;
    private readonly AIEntityFactory _factory;
    private readonly UnitRemovalService _unitRemovalService;
    private readonly AIRandomProvider _rng;

    public AITacticalBrain(
        IMapDataService mapDataService,
        UnitMovementSystem movementSystem,
        IUnitRepository unitRepository,
        AIFogService aiFog,
        AIEntityFactory factory,
        UnitRemovalService unitRemovalService,
        AIRandomProvider rng)
    {
        _mapDataService = mapDataService;
        _movementSystem = movementSystem;
        _unitRepository = unitRepository;
        _aiFog = aiFog;
        _factory = factory;
        _unitRemovalService = unitRemovalService;
        _rng = rng;
    }

    private System.Random Random => _rng.Random;

    /// <summary>
    /// 处理单个单位的回合：寻找最近玩家 -> 若可见则移动到攻击位置并攻击 -> 否则前沿游走。
    /// </summary>
    public IEnumerator HandleSingleUnitTurn(CharacterData cd)
    {
        var umc = cd.unitMovementController;
        if (umc == null || cd.model == null) yield break;

        TryReapChest(cd);

        if (cd.UnitID == 0)
        {
            yield return HandleSettlerTurn(cd);
            yield break;
        }

        List<Vector3> allPoints = new List<Vector3>(_mapDataService.GetAllHexCoordinates());
        Vector3 startHex = _mapDataService.WorldToHexCoordinate(cd.model.transform.position);

        // AI 逻辑迷雾（A）：每单位决策前现算本阵营当前可见集合。
        // （B）只把"当前可见"的玩家单位/建筑列为候选目标——迷雾外的敌人 AI 看不到、不追。
        HashSet<HexCellData> vision = _aiFog.ComputeVisible(AIIndex);

        // 寻找最近目标（玩家单位 + 玩家建筑，均需在视野内）
        CharacterData nearestPlayer = null;
        GameObject nearestPlayerBuilding = null;
        float nearestCost = float.MaxValue;
        foreach (var p in _unitRepository.AllPlayerUnits.Values)
        {
            if (p?.model == null || p.currentHp <= 0) continue;
            HexCellData pCell = _mapDataService.GetCellByWorldPosition(p.model.transform.position);
            if (pCell == null || !vision.Contains(pCell)) continue; // 视野外：看不到
            Vector3 endHex = _mapDataService.WorldToHexCoordinate(p.model.transform.position);
            if (_movementSystem.CalculateMinMovementCostBetweenTwoHexes(allPoints, startHex, endHex, Enums.MovementPurpose.MoveToAttack, out float cost, out _) && cost < nearestCost)
            {
                nearestCost = cost;
                nearestPlayer = p;
            }
        }

        GameObject[] playerBuildings = GameObject.FindGameObjectsWithTag("PlayerBuilding");
        foreach (var building in playerBuildings)
        {
            if (building == null) continue;
            HexCellData bCell = _mapDataService.GetCellByWorldPosition(building.transform.position);
            if (bCell == null || !vision.Contains(bCell)) continue; // 视野外：看不到（B：静态建筑也不给记忆）
            Vector3 endHex = _mapDataService.WorldToHexCoordinate(building.transform.position);
            if (_movementSystem.CalculateMinMovementCostBetweenTwoHexes(allPoints, startHex, endHex, Enums.MovementPurpose.MoveToAttack, out float cost, out _) && cost < nearestCost)
            {
                nearestCost = cost;
                nearestPlayer = null;
                nearestPlayerBuilding = building;
            }
        }

        GameObject target = nearestPlayer != null ? nearestPlayer.model : nearestPlayerBuilding;
        bool actionStarted = false;
        if (target != null)
        {
            if (nearestPlayer != null)
            {
                umc.attackedUnit = nearestPlayer.model;
                umc.attackTarget = "PlayerUnit";
            }
            else
            {
                umc.attackedUnit = nearestPlayerBuilding;
                umc.attackTarget = "PlayerBuilding";
            }

            Vector3 targetHex = _mapDataService.WorldToHexCoordinate(target.transform.position);
            int attackRange = cd.unitData?.BasicAttackRange ?? 1;
            if (attackRange > 1)
            {
                actionStarted = HexDistance(startHex, targetHex) <= attackRange && umc.TryStartRangedAttack(target);
            }
            else if (nearestCost <= umc.currentMovementPoints + 1f)
            {
                umc.MoveTo(targetHex, Enums.MovementPurpose.MoveToAttack);
                actionStarted = umc.IsBusy;
            }

            if (actionStarted)
            {
                while (umc.IsBusy)
                    yield return null;
                yield return new WaitForSeconds(0.2f);
                yield break;
            }
        }

        {
            // 前沿游走（C）：无可见目标时，偏向"附近未探索格最多"的可达格，模拟侦察而非原地打转。
            List<Vector3> reachable = _movementSystem.GetAllReachableHexesFromStartHex(allPoints, startHex, umc.currentMovementPoints);
            reachable.RemoveAll(v => v == startHex);
            if (reachable.Count > 0)
            {
                Vector3 chosen = ChooseFrontierTarget(reachable);
                umc.MoveTo(chosen, Enums.MovementPurpose.None);
                while (umc.isMoving)
                    yield return null;
                yield return new WaitForSeconds(0.05f);
            }
        }
    }

    /// <summary>
    /// 前沿打分（C）：候选可达格附近 scanRadius 圈内、该 AI 从未探索过的格子越多，分越高；
    /// 并列随机；若全为 0（周围都探索过）退回纯随机。使 AI 倾向走向迷雾边缘/未知区域。
    /// </summary>
    private Vector3 ChooseFrontierTarget(List<Vector3> reachable)
    {
        const int scanRadius = 2;
        int bestScore = -1;
        List<Vector3> best = new List<Vector3>();
        var neighborhood = new HashSet<HexCellData>();

        foreach (var v in reachable)
        {
            HexCellData c = _mapDataService.GetCell(v);
            if (c == null) continue;

            neighborhood.Clear();
            FieldOfViewService.CollectVisibleCells(_mapDataService, c, scanRadius, neighborhood);

            int score = 0;
            foreach (var n in neighborhood)
                if (_aiFog.IsUnexplored(AIIndex, n)) score++;

            if (score > bestScore)
            {
                bestScore = score;
                best.Clear();
                best.Add(v);
            }
            else if (score == bestScore)
            {
                best.Add(v);
            }
        }

        if (best.Count == 0) return reachable[Random.Next(reachable.Count)];
        return best[Random.Next(best.Count)];
    }

    private static float HexDistance(Vector3 a, Vector3 b)
    {
        return (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z)) * 0.5f;
    }

    private IEnumerator HandleSettlerTurn(CharacterData cd)
    {
        if (TryFoundCityWithSettler(cd))
        {
            yield break;
        }

        var umc = cd.unitMovementController;
        List<Vector3> allPoints = new List<Vector3>(_mapDataService.GetAllHexCoordinates());
        Vector3 startHex = _mapDataService.WorldToHexCoordinate(cd.model.transform.position);
        List<Vector3> reachable = _movementSystem.GetAllReachableHexesFromStartHex(allPoints, startHex, umc.currentMovementPoints);

        List<Vector3> cityTargets = reachable
            .Where(v =>
            {
                HexCellData c = _mapDataService.GetCell(v);
                return IsValidCityCell(c, cd.model) && c.Player_City_Index.Key == -1;
            })
            .ToList();

        if (cityTargets.Count == 0)
        {
            cityTargets = reachable
                .Where(v => IsValidCityCell(_mapDataService.GetCell(v), cd.model))
                .ToList();
        }

        if (cityTargets.Count > 0)
        {
            Vector3 chosen = cityTargets[Random.Next(cityTargets.Count)];
            umc.MoveTo(chosen, Enums.MovementPurpose.None);
            while (umc.isMoving)
            {
                yield return null;
            }
        }

        TryFoundCityWithSettler(cd);
    }

    private bool TryFoundCityWithSettler(CharacterData settlerData)
    {
        if (settlerData == null || settlerData.model == null) return false;

        HexCellData cell = _mapDataService.GetCellByWorldPosition(settlerData.model.transform.position);
        if (!IsValidCityCell(cell, settlerData.model)) return false;

        _factory.GenerateCity(cell.RealCenterWorldCoordinate);
        _unitRemovalService.RemoveUnit(settlerData.model);
        return true;
    }

    private bool IsValidCityCell(HexCellData cell, GameObject settlerObj)
    {
        if (cell == null) return false;
        if (cell.HexType == Enums.HexType.LakeOrSea) return false;
        if (cell.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding) return false;
        if (cell.Player_City_Index.Key != -1 && cell.Player_City_Index.Key != AIIndex) return false;
        if (!cell.IsHaveUnit()) return true;

        GameObject occupiedUnit = cell.GetUnit();
        return occupiedUnit == settlerObj;
    }

    private void TryReapChest(CharacterData cd)
    {
        if (cd == null || cd.model == null) return;

        HexCellData h = _mapDataService.GetCellByWorldPosition(cd.model.transform.position);
        if (h == null) return;
        if (h.GetResource() != Enums.ResourceType.Chest) return;

        h.ReapResource();
        if (h.resourceModel != null)
        {
            Object.Destroy(h.resourceModel);
            h.resourceModel = null;
        }

        // 科技/文化系统已移除：AI 收割宝箱不再获得科技/文化点数。
    }
}
