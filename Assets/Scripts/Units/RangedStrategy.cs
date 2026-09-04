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
// 【性能】CanAttack / DoCombat 的射程内选目标统一走 FindNearestTargetInRange 邻居环遍历，
//         不再逐帧扫全图。
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
        // 直接用缓存表：寻路只读 allPoints，不需要防御性拷贝
        List<Vector3> allPoints = mapData.GetAllHexCoordinates();
        Vector3 startHex = brain.SelfHexCoordinate;

        Vector3? directionHint = brain.FindApproximateDirectionToHiddenBuilding();
        if (directionHint.HasValue && movement.CalculateMinMovementCostBetweenTwoHexes(
                allPoints, startHex, directionHint.Value,
                Enums.MovementPurpose.MoveToDestination, brain.FactionId, out _, out List<Vector3> directionPath)
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
                Enums.MovementPurpose.MoveToAttack, brain.FactionId, out _, out List<Vector3> path)
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

        Vector3 selfHex = brain.SelfHexCoordinate;
        return FindNearestTargetInRange(brain, selfHex, out _, out _);
    }

    public void DoCombat(UnitBrainBase brain)
    {
        if (brain?.Owner?.model == null || brain.MapData == null) return;

        Vector3 selfHex = brain.SelfHexCoordinate;
        if (!FindNearestTargetInRange(brain, selfHex, out GameObject bestTarget, out bool bestIsUnit)) return;

        if (bestTarget == null) return;

        var umc = brain.Owner.unitMovementController;
        if (umc == null) return;

        // 播放远程动画（原地，无冲刺）
        umc.PlayRangedAttackAnim(bestTarget);

        // 箭矢飞行表现：实例化 arrow 预制体沿弧线飞向目标，箭到达目的地时才结算伤害（对齐箭塔）
        var rangedShooter = umc.GetComponent<UnitRangedShooter>();
        if (rangedShooter == null)
            rangedShooter = umc.gameObject.AddComponent<UnitRangedShooter>();
        float speedMultiplier = brain.GameLoop != null ? brain.GameLoop.SpeedMultiplier : 1f;
        rangedShooter.ShootDelayed(bestTarget, () =>
        {
            if (brain == null || brain.Combat == null || brain.Owner == null || bestTarget == null) return;

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
        });

        // 启动攻速冷却
        brain.MarkAttacked();
    }

    /// <summary>
    /// 从自身格向外按半径 1..attackRange 逐环遍历，返回射程内最近的可攻击目标。
    /// 【性能】旧实现扫全图（O(格数)，约 600）再按距离过滤；现在只访问射程环内的
    /// 3*R*(R+1) 格，每格一次字典查询，与地图大小无关。
    /// 同一环内优先敌方单位、其次可攻击建筑（旧实现按格子枚举顺序决定，无稳定语义）。
    /// </summary>
    private static bool FindNearestTargetInRange(UnitBrainBase brain, Vector3 selfHex,
        out GameObject bestTarget, out bool bestIsUnit)
    {
        bestTarget = null;
        bestIsUnit = false;

        bool isPlayer = brain.Owner.model.CompareTag("PlayerUnit");
        string enemyUnitTag = isPlayer ? "EnemyUnit" : "PlayerUnit";
        string enemyBuildingTag = isPlayer ? "EnemyBuilding" : "PlayerBuilding";

        int attackRange = GetEffectiveRange(brain, selfHex);
        var mapData = brain.MapData;

        for (int radius = 1; radius <= attackRange; radius++)
        {
            GameObject ringBuilding = null;

            // 环遍历：从正西方向第 radius 格出发，沿六个方向各走 radius 步绕行一周
            Vector3 hex = selfHex + CubeDirections[4] * radius;
            for (int dir = 0; dir < 6; dir++)
            {
                for (int step = 0; step < radius; step++)
                {
                    HexCellData cell = mapData.GetCell(hex);
                    hex += CubeDirections[dir];
                    if (cell == null) continue;

                    // 【多单位落点】枚举格内全部站位单位。
                    foreach (GameObject u in cell.GetStandingUnits())
                    {
                        if (u != null && u.CompareTag(enemyUnitTag))
                        {
                            bestTarget = u;
                            bestIsUnit = true;
                            return true;
                        }
                    }

                    if (ringBuilding == null)
                    {
                        GameObject b = cell.BulidingTypeOnHex_Building.Value;
                        if (IsAttackableBuilding(b, enemyBuildingTag)) ringBuilding = b;
                    }
                }
            }

            if (ringBuilding != null)
            {
                bestTarget = ringBuilding;
                bestIsUnit = false;
                return true;
            }
        }

        return false;
    }

    // 立方坐标六方向，顺序须与 HexMapService.GetNeighbor 的 NE/E/SE/SW/W/NW 一致（环遍历依赖其循环性）
    private static readonly Vector3[] CubeDirections =
    {
        new Vector3(0, -1, 1),  // NE
        new Vector3(1, -1, 0),  // E
        new Vector3(1, 0, -1),  // SE
        new Vector3(0, 1, -1),  // SW
        new Vector3(-1, 1, 0),  // W
        new Vector3(-1, 0, 1),  // NW
    };

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
