using System.Collections.Generic;
using UnityEngine;

//****************************************
// 功能说明：近战兵种策略。
//   攻击范围 range = 0（走到目标相邻格后进入同格攻击）。
//   CanAttack：相邻格内有敌方单位/建筑时为 true。
//   DoCombat：CombatResolver 瞬间结算伤害 + PlayAttackAnim 表现动画 + MarkAttacked 开始冷却。
//   ChooseNextPath：
//     1. 警戒范围（3格）内有敌方单位 → 追击
//     2. 无近敌 → 向最近敌方建筑（主城优先）行军
//     3. 无法到达任何建筑 → 隔海趋近最近岸格（海边驻扎）；完全无目标则随机游走
//
// 【批次 D】DoCombat 改用 CombatResolver + PlayAttackAnim，移除旧 MoveToAttack 路径。
//****************************************

public class MeleeStrategy : IUnitStrategy
{
    // 步兵警戒范围（格）。Excel 唯一主源（BattleFormulaRule）。
    private int AlertRange => BattleFormulaRule.MeleeAlertRange;

    public List<Vector3> ChooseNextPath(UnitBrainBase brain)
    {
        if (brain?.Owner?.model == null || brain.MapData == null || brain.Movement == null)
            return null;

        var mapData = brain.MapData;
        var movement = brain.Movement;
        // 直接用缓存表：寻路只读 allPoints，不需要防御性拷贝
        List<Vector3> allPoints = mapData.GetAllHexCoordinates();
        Vector3 startHex = mapData.WorldToHexCoordinate(brain.Owner.model.transform.position);

        // 1. 公共建筑方向提示（已发现但未占领）
        Vector3? directionHint = brain.FindApproximateDirectionToHiddenBuilding();
        if (directionHint.HasValue && movement.CalculateMinMovementCostBetweenTwoHexes(
                allPoints, startHex, directionHint.Value,
                Enums.MovementPurpose.MoveToDestination, brain.FactionId, out _, out List<Vector3> directionPath)
            && directionPath != null && directionPath.Count > 0)
        {
            return new List<Vector3> { directionPath[0] };
        }

        // 2. 警戒范围（3格）内有任何敌方目标（单位/宝箱/建筑）→ 追击最近者
        //    （索敌链第二优先级：敌方单位 > 宝箱 > 敌方建筑，玩法文档 §4.2）
        //    三个查询各只执行一次，第 3/4 步复用同一结果：
        //    旧实现 FindNearestChest / FindNearestEnemyBuilding 各被调用两次，
        //    每次调用都是多候选全图寻路 —— 本决策内输入不变，结果无需重算。
        Vector3? nearestEnemy = brain.FindNearestEnemy();
        Vector3? nearestChest = brain.FindNearestChest();
        Vector3? nearestBuilding = brain.FindNearestEnemyBuilding();

        Vector3? bestAlertTarget = null;
        float bestAlertDist = float.MaxValue;

        if (nearestEnemy.HasValue)
        {
            float dist = HexDistance(startHex, nearestEnemy.Value);
            if (dist <= AlertRange && dist < bestAlertDist)
            {
                bestAlertDist = dist;
                bestAlertTarget = nearestEnemy;
            }
        }

        if (nearestChest.HasValue)
        {
            float dist = HexDistance(startHex, nearestChest.Value);
            if (dist <= AlertRange && dist < bestAlertDist)
            {
                bestAlertDist = dist;
                bestAlertTarget = nearestChest;
            }
        }

        if (nearestBuilding.HasValue)
        {
            float dist = HexDistance(startHex, nearestBuilding.Value);
            if (dist <= AlertRange && dist < bestAlertDist)
            {
                bestAlertDist = dist;
                bestAlertTarget = nearestBuilding;
            }
        }

        if (bestAlertTarget.HasValue)
        {
            if (movement.CalculateMinMovementCostBetweenTwoHexes(
                    allPoints, startHex, bestAlertTarget.Value,
                    Enums.MovementPurpose.MoveToAttack, brain.FactionId, out _, out List<Vector3> path)
                && path != null && path.Count > 0)
            {
                return path;
            }
        }

        // 3. 无警戒范围内目标 → 向宝箱行军（宝箱 > 敌方建筑）
        if (nearestChest.HasValue)
        {
            if (movement.CalculateMinMovementCostBetweenTwoHexes(
                    allPoints, startHex, nearestChest.Value,
                    Enums.MovementPurpose.MoveToAttack, brain.FactionId, out _, out List<Vector3> chestPath)
                && chestPath != null && chestPath.Count > 0)
            {
                return chestPath;
            }
        }

        // 4. 无宝箱/无法到达 → 向最近敌方建筑（主城等）行军
        if (nearestBuilding.HasValue)
        {
            if (movement.CalculateMinMovementCostBetweenTwoHexes(
                    allPoints, startHex, nearestBuilding.Value,
                    Enums.MovementPurpose.MoveToAttack, brain.FactionId, out _, out List<Vector3> marchPath)
                && marchPath != null && marchPath.Count > 0)
            {
                return marchPath;
            }
        }

        // 5. 兜底：先隔海趋近（目标被隔绝 → 走到最接近目标的地块，近战海边驻扎），
        //    无任何目标 → 在可移动范围内随机游走（不再原地站桩）。
        //    ChooseFallbackPath 内部已隔离这两级：有隔绝目标时绝不落入游走，避免近战离开海岸。
        return brain.ChooseFallbackPath(allPoints, startHex);
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

    private static float HexDistance(Vector3 a, Vector3 b)
    {
        return (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z)) * 0.5f;
    }

    private static string TargetTagInCell(HexCellData cell, bool selfIsPlayer)
    {
        return TargetInCell(cell, selfIsPlayer).tag;
    }

    private static (GameObject target, string tag) TargetInCell(HexCellData cell, bool selfIsPlayer)
    {
        string enemyUnitTag = selfIsPlayer ? "EnemyUnit" : "PlayerUnit";
        string enemyBuildingTag = selfIsPlayer ? "EnemyBuilding" : "PlayerBuilding";

        // 【多单位落点】枚举格内全部站位单位。
        foreach (GameObject unitInCell in cell.GetStandingUnits())
        {
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
}
