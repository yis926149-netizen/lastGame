using System.Collections.Generic;
using UnityEngine;

//****************************************
// 功能说明：远程兵种策略。
//   攻击范围 range >= 1（BasicAttackRange）。
//   CanAttack：射程环内有敌方单位/建筑时为 true。
//   DoCombat：CombatResolver 瞬间结算 + PlayRangedAttackAnim（原地播放，无冲刺）+ MarkAttacked。
//   ChooseNextPath：目标在射程内返回 null；否则返回完整路径（截至进入射程处）。
//
// 【批次 D】DoCombat 改用 CombatResolver + PlayRangedAttackAnim。
//****************************************

public class RangedStrategy : IUnitStrategy
{
    public List<Vector3> ChooseNextPath(UnitBrainBase brain)
    {
        if (brain?.Owner?.model == null || brain.MapData == null || brain.Movement == null)
            return null;

        // CanAttack 已由 OnStepFinished 检查，此处进入说明 CanAttack == false，无需重复调用

        var mapData = brain.MapData;
        var movement = brain.Movement;
        List<Vector3> allPoints = new List<Vector3>(mapData.GetAllHexCoordinates());
        Vector3 startHex = mapData.WorldToHexCoordinate(brain.Owner.model.transform.position);

        Vector3? targetHex = brain.FindNearestEnemy() ?? brain.FindNearestEnemyBuilding();
        if (!targetHex.HasValue)
        {
            // 无目标时前沿游走：返回单步路径
            Vector3? frontierStep = ChooseFrontierStep(brain, allPoints, startHex);
            if (frontierStep.HasValue)
                return new List<Vector3> { frontierStep.Value };
            return null;
        }

        int attackRange = brain.Owner.unitData?.BasicAttackRange ?? 1;
        float dist = HexDistance(startHex, targetHex.Value);
        if (dist <= attackRange) return null;  // 已在射程内，不需要移动

        if (movement.CalculateMinMovementCostBetweenTwoHexes(
                allPoints, startHex, targetHex.Value,
                Enums.MovementPurpose.MoveToAttack, out _, out List<Vector3> path)
            && path != null && path.Count > 0)
        {
            // 只走到射程够得着目标的位置：截取路径到距目标 attackRange 格为止
            int stopIdx = path.Count - 1;
            for (int i = 0; i < path.Count; i++)
            {
                if (HexDistance(path[i], targetHex.Value) <= attackRange)
                {
                    stopIdx = i;
                    break;
                }
            }
            return path.GetRange(0, stopIdx + 1);
        }

        {
            Vector3? frontierStep = ChooseFrontierStep(brain, allPoints, startHex);
            if (frontierStep.HasValue)
                return new List<Vector3> { frontierStep.Value };
            return null;
        }
    }

    private static Vector3? ChooseFrontierStep(UnitBrainBase brain, List<Vector3> allPoints, Vector3 startHex)
    {
        // 【探索重构】无索敌目标时随机移动到临近可达格
        float budget = brain.Owner?.unitMovementController?.currentMovementPoints ?? 1f;
        if (budget <= 0) budget = 1f;

        var reachable = brain.Movement.GetAllReachableHexesFromStartHex(allPoints, startHex, budget);
        reachable.RemoveAll(v => v == startHex);
        if (reachable.Count == 0) return null;

        var chosen = reachable[Random.Range(0, reachable.Count)];
        if (brain.Movement.CalculateMinMovementCostBetweenTwoHexes(
                allPoints, startHex, chosen,
                Enums.MovementPurpose.MoveToDestination, out _, out var path)
            && path != null && path.Count > 0)
        {
            return path[0];
        }
        return null;
    }

    public bool CanAttack(UnitBrainBase brain)
    {
        if (brain?.Owner?.model == null || brain.MapData == null) return false;

        int attackRange = brain.Owner.unitData?.BasicAttackRange ?? 1;
        bool isPlayer = brain.Owner.model.CompareTag("PlayerUnit");
        string enemyUnitTag = isPlayer ? "EnemyUnit" : "PlayerUnit";
        string enemyBuildingTag = isPlayer ? "EnemyBuilding" : "PlayerBuilding";

        Vector3 selfHex = brain.MapData.WorldToHexCoordinate(brain.Owner.model.transform.position);

        foreach (var cell in brain.MapData.GetAllCells())
        {
            float dist = HexDistance(selfHex, cell.HexCoordinate);
            if (dist > attackRange || dist < 0.1f) continue;

            if (cell.IsHaveUnit())
            {
                GameObject u = cell.GetUnit();
                if (u != null && u.CompareTag(enemyUnitTag)) return true;
            }

            GameObject b = cell.BulidingTypeOnHex_Building.Value;
            if (b != null && (b.CompareTag(enemyBuildingTag) || b.CompareTag("NeutralBuilding"))) return true;
        }

        return false;
    }

    public void DoCombat(UnitBrainBase brain)
    {
        if (brain?.Owner?.model == null || brain.MapData == null) return;

        int attackRange = brain.Owner.unitData?.BasicAttackRange ?? 1;
        bool isPlayer = brain.Owner.model.CompareTag("PlayerUnit");
        string enemyUnitTag = isPlayer ? "EnemyUnit" : "PlayerUnit";
        string enemyBuildingTag = isPlayer ? "EnemyBuilding" : "PlayerBuilding";

        Vector3 selfHex = brain.MapData.WorldToHexCoordinate(brain.Owner.model.transform.position);

        float bestDist = float.MaxValue;
        GameObject bestTarget = null;
        bool bestIsUnit = false;

        foreach (var cell in brain.MapData.GetAllCells())
        {
            float dist = HexDistance(selfHex, cell.HexCoordinate);
            if (dist > attackRange || dist < 0.1f) continue;

            if (cell.IsHaveUnit())
            {
                GameObject u = cell.GetUnit();
                if (u != null && u.CompareTag(enemyUnitTag) && dist < bestDist)
                {
                    bestDist = dist;
                    bestTarget = u;
                    bestIsUnit = true;
                    continue;
                }
            }

            GameObject b = cell.BulidingTypeOnHex_Building.Value;
            if (b != null &&
                (b.CompareTag(enemyBuildingTag) || b.CompareTag("NeutralBuilding")) &&
                dist < bestDist)
            {
                bestDist = dist;
                bestTarget = b;
                bestIsUnit = false;
            }
        }

        if (bestTarget == null) return;

        var umc = brain.Owner.unitMovementController;
        if (umc == null) return;

        // 【批次 D】瞬间伤害结算
        if (brain.Combat != null)
        {
            if (bestIsUnit)
            {
                var targetUmc = bestTarget.GetComponent<UnitMovementController>();
                if (targetUmc?.characterData != null)
                    brain.Combat.Resolve(brain.Owner, targetUmc.characterData);
            }
            else
            {
                var targetBuilding = bestTarget.GetComponent<BuildingBase>();
                if (targetBuilding?.buildingData != null)
                    brain.Combat.ResolveBuilding(brain.Owner, targetBuilding);
            }
        }

        // 播放远程动画（原地，无冲刺）
        umc.PlayRangedAttackAnim(bestTarget);

        // 启动攻速冷却
        brain.MarkAttacked();
    }

    private static float HexDistance(Vector3 a, Vector3 b)
    {
        return (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z)) * 0.5f;
    }
}
