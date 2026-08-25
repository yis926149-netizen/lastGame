using System.Collections.Generic;
using UnityEngine;

//****************************************
// 功能说明：远程兵种策略。
//   攻击范围 range >= 1（BasicAttackRange）。
//   CanAttack：射程环内有敌方单位/建筑时为 true。
//   DoCombat：CombatResolver 瞬间结算 + PlayRangedAttackAnim（原地播放，无冲刺）+ MarkAttacked。
//   ChooseNextPath：目标在射程内返回 null；否则返回完整路径（截至进入射程处）；
//                   目标被水域隔绝时趋近最近岸格（到位后可隔海射击），完全无目标时随机游走。
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

        Vector3? directionHint = brain.FindApproximateDirectionToHiddenBuilding();
        if (directionHint.HasValue && movement.CalculateMinMovementCostBetweenTwoHexes(
                allPoints, startHex, directionHint.Value,
                Enums.MovementPurpose.MoveToDestination, out _, out List<Vector3> directionPath)
            && directionPath != null && directionPath.Count > 0)
        {
            return new List<Vector3> { directionPath[0] };
        }

        // 【竞技场-阶段二】索敌链第二优先级：敌方单位 > 宝箱 > 敌方建筑（玩法文档 §4.2）
        Vector3? targetHex = brain.FindNearestEnemy() ?? brain.FindNearestChest() ?? brain.FindNearestEnemyBuilding();
        if (!targetHex.HasValue)
        {
            // 无可达目标：可能是被水域隔绝（趋近最近岸格，到位后可隔海射击），
            // 也可能真的没有目标（随机游走）。二者由 ChooseFallbackPath 区分。
            return brain.ChooseFallbackPath(allPoints, startHex);
        }

        int attackRange = GetEffectiveRange(brain, startHex);
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

        // 目标不可达（或已到位置）→ 若目标被水域隔绝则趋近最近岸格，否则随机游走
        return brain.ChooseFallbackPath(allPoints, startHex);
    }

    public bool CanAttack(UnitBrainBase brain)
    {
        if (brain?.Owner?.model == null || brain.MapData == null) return false;

        bool isPlayer = brain.Owner.model.CompareTag("PlayerUnit");
        string enemyUnitTag = isPlayer ? "EnemyUnit" : "PlayerUnit";
        string enemyBuildingTag = isPlayer ? "EnemyBuilding" : "PlayerBuilding";

        Vector3 selfHex = brain.MapData.WorldToHexCoordinate(brain.Owner.model.transform.position);
        int attackRange = GetEffectiveRange(brain, selfHex);

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
            if (IsAttackableBuilding(b, enemyBuildingTag)) return true;
        }

        return false;
    }

    public void DoCombat(UnitBrainBase brain)
    {
        if (brain?.Owner?.model == null || brain.MapData == null) return;

        bool isPlayer = brain.Owner.model.CompareTag("PlayerUnit");
        string enemyUnitTag = isPlayer ? "EnemyUnit" : "PlayerUnit";
        string enemyBuildingTag = isPlayer ? "EnemyBuilding" : "PlayerBuilding";

        Vector3 selfHex = brain.MapData.WorldToHexCoordinate(brain.Owner.model.transform.position);
        int attackRange = GetEffectiveRange(brain, selfHex);

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
            if (IsAttackableBuilding(b, enemyBuildingTag) && dist < bestDist)
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

    private static int GetEffectiveRange(UnitBrainBase brain, Vector3 selfHex)
    {
        if (brain?.Owner?.unitData == null || brain.MapData == null)
            return 1;

        int baseRange = brain.Owner.unitData.BasicAttackRange;
        HexCellData cell = brain.MapData.GetCell(selfHex);
        if (cell == null) return baseRange;

        return WaterLevelConfig.ClassifyHeight(cell.Height) == 2 ? baseRange + BattleFormulaRule.HighGroundRangeBonus : baseRange;
    }

    private static float HexDistance(Vector3 a, Vector3 b)
    {
        return (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z)) * 0.5f;
    }

    private static bool IsAttackableBuilding(GameObject building, string enemyBuildingTag)
    {
        if (building == null ||
            (!building.CompareTag(enemyBuildingTag) && !building.CompareTag("NeutralBuilding")))
        {
            return false;
        }

        var publicBuilding = building.GetComponent<PublicBuildingBase>();
        return publicBuilding == null ||
               publicBuilding.CurrentDiscoveryState == PublicBuildingBase.DiscoveryState.Revealed;
    }
}
