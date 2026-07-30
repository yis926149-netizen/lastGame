using System.Collections.Generic;
using UnityEngine;

//****************************************
// 功能说明：近战兵种策略。
//   攻击范围 range = 0（走到目标相邻格后进入同格攻击）。
//   CanAttack：相邻格内有敌方单位/建筑时为 true。
//   DoCombat：CombatResolver 瞬间结算伤害 + PlayAttackAnim 表现动画 + MarkAttacked 开始冷却。
//   ChooseNextPath：朝最近可见目标返回完整路径；无目标时前沿游走（单步路径）。
//
// 【批次 D】DoCombat 改用 CombatResolver + PlayAttackAnim，移除旧 MoveToAttack 路径。
//****************************************

public class MeleeStrategy : IUnitStrategy
{
    public List<Vector3> ChooseNextPath(UnitBrainBase brain)
    {
        if (brain?.Owner?.model == null || brain.MapData == null || brain.Movement == null)
            return null;

        var mapData = brain.MapData;
        var movement = brain.Movement;
        List<Vector3> allPoints = new List<Vector3>(mapData.GetAllHexCoordinates());
        Vector3 startHex = mapData.WorldToHexCoordinate(brain.Owner.model.transform.position);

        Vector3? directionHint = brain.FindApproximateDirectionToHiddenBuilding();
        if (directionHint.HasValue && movement.CalculateMinMovementCostBetweenTwoHexes(
                allPoints, startHex, directionHint.Value,
                Enums.MovementPurpose.MoveToDestination, out _, out List<Vector3> directionPath)
            && directionPath != null && directionPath.Count > 0)
        {
            return new List<Vector3> { directionPath[0] };
        }

        Vector3? targetHex = brain.FindNearestEnemy() ?? brain.FindNearestEnemyBuilding();

        if (targetHex.HasValue)
        {
            if (movement.CalculateMinMovementCostBetweenTwoHexes(
                    allPoints, startHex, targetHex.Value,
                    Enums.MovementPurpose.MoveToAttack, out _, out List<Vector3> path)
                && path != null && path.Count > 0)
            {
                return path;  // 返回完整路径
            }
        }

        // 无目标时前沿游走：返回单步路径
        Vector3? frontierStep = ChooseFrontierStep(brain, allPoints, startHex);
        if (frontierStep.HasValue)
            return new List<Vector3> { frontierStep.Value };
        
        return null;
    }

    public bool CanAttack(UnitBrainBase brain)
    {
        if (brain?.Owner?.model == null || brain.MapData == null) return false;

        HexCellData selfCell = brain.MapData.GetCellByWorldPosition(brain.Owner.model.transform.position);
        if (selfCell == null) return false;

        bool isPlayer = brain.Owner.model.CompareTag("PlayerUnit");

        for (int i = 0; i < 6; i++)
        {
            HexCellData neighbor = brain.MapData.GetNeighbor(selfCell, (Enums.HexDirection)i);
            if (neighbor == null) continue;
            if (TargetTagInCell(neighbor, isPlayer) != null) return true;
        }

        return false;
    }

    public void DoCombat(UnitBrainBase brain)
    {
        if (brain?.Owner?.model == null || brain.MapData == null) return;

        HexCellData selfCell = brain.MapData.GetCellByWorldPosition(brain.Owner.model.transform.position);
        if (selfCell == null) return;

        var umc = brain.Owner.unitMovementController;
        if (umc == null) return;

        bool isPlayer = brain.Owner.model.CompareTag("PlayerUnit");

        for (int i = 0; i < 6; i++)
        {
            HexCellData neighbor = brain.MapData.GetNeighbor(selfCell, (Enums.HexDirection)i);
            if (neighbor == null) continue;

            var (target, tag) = TargetInCell(neighbor, isPlayer);
            if (target == null) continue;

            // 【批次 D】瞬间伤害结算
            if (brain.Combat != null)
            {
                var targetUmc = target.GetComponent<UnitMovementController>();
                var targetBuilding = target.GetComponent<BuildingBase>();

                if (targetUmc?.characterData != null)
                    brain.Combat.Resolve(brain.Owner, targetUmc.characterData);
                else if (targetBuilding?.buildingData != null)
                    brain.Combat.ResolveBuilding(brain.Owner, targetBuilding);
            }

            // 播放动画表现（冲刺+音效，不含伤害）
            umc.PlayAttackAnim(target);

            // 启动攻速冷却
            brain.MarkAttacked();
            return;
        }
    }

    private static string TargetTagInCell(HexCellData cell, bool selfIsPlayer)
    {
        return TargetInCell(cell, selfIsPlayer).tag;
    }

    private static (GameObject target, string tag) TargetInCell(HexCellData cell, bool selfIsPlayer)
    {
        string enemyUnitTag = selfIsPlayer ? "EnemyUnit" : "PlayerUnit";
        string enemyBuildingTag = selfIsPlayer ? "EnemyBuilding" : "PlayerBuilding";

        if (cell.IsHaveUnit())
        {
            GameObject unitInCell = cell.GetUnit();
            if (unitInCell != null && unitInCell.CompareTag(enemyUnitTag))
                return (unitInCell, enemyUnitTag);
        }

        GameObject buildingInCell = cell.BulidingTypeOnHex_Building.Value;
        if (buildingInCell != null &&
            (buildingInCell.CompareTag(enemyBuildingTag) || buildingInCell.CompareTag("NeutralBuilding")))
        {
            var publicBuilding = buildingInCell.GetComponent<PublicBuildingBase>();
            if (publicBuilding != null &&
                publicBuilding.CurrentDiscoveryState == PublicBuildingBase.DiscoveryState.Hidden)
            {
                return (null, null);
            }

            return (buildingInCell, buildingInCell.tag);
        }

        return (null, null);
    }

    private Vector3? ChooseFrontierStep(UnitBrainBase brain, List<Vector3> allPoints, Vector3 startHex)
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
}
