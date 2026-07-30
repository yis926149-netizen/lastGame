using System.Collections.Generic;
using UnityEngine;

//****************************************
// 功能说明：AI 阵营单位 Brain。
//   【探索重构-阶段6】AI 与玩家一致（全知索敌），不再使用 AIFogService。
//
// 【批次 B】FindNearestEnemy/Building 接入实际逻辑；TryFoundCity 实现。
//****************************************

public class AIUnitBrain : UnitBrainBase
{
    private IUnitRepository _unitRepository;
    private AIEntityFactory _factory;
    private UnitRemovalService _unitRemovalService;
    private int _aiIndex = 1;

    public void Initialize(CharacterData owner, IUnitStrategy strategy,
                           IMapDataService mapDataService,
                           IUnitRepository unitRepository,
                           UnitMovementSystem movementSystem,
                           CombatResolver combatResolver = null,
                           AIEntityFactory factory = null,
                           UnitRemovalService unitRemovalService = null,
                           PublicBuildingMarkerManager markerManager = null,
                           int aiIndex = 1)
    {
        Owner = owner;
        activeStrategy = strategy;
        MapData = mapDataService;
        Movement = movementSystem;
        Combat = combatResolver;
        _unitRepository = unitRepository;
        _factory = factory;
        _unitRemovalService = unitRemovalService;
        SetPublicBuildingMarkerManager(markerManager);
        _aiIndex = aiIndex;
    }

    // ── 目标查询 ────────────────────────────────────────────

    public override Vector3? FindNearestEnemy()
    {
        if (Owner?.model == null || MapData == null || Movement == null) return null;

        List<Vector3> allPoints = new List<Vector3>(MapData.GetAllHexCoordinates());
        Vector3 startHex = Owner.unitMovementController?.CurrentHexCoordinate ?? MapData.WorldToHexCoordinate(Owner.model.transform.position);
        if (startHex == default) return null;

        Vector3? best = null;
        float bestCost = float.MaxValue;

        foreach (var cd in _unitRepository.AllPlayerUnits.Values)
        {
            if (cd?.model == null || cd.currentHp <= 0) continue;

            var enemyMC = cd.unitMovementController;
            if (enemyMC == null) continue;
            Vector3 endHex = enemyMC.CurrentHexCoordinate;

            if (HexDistance(startHex, endHex) >= bestCost) continue;

            if (Movement.CalculateMinMovementCostBetweenTwoHexes(
                    allPoints, startHex, endHex,
                    Enums.MovementPurpose.MoveToAttack, out float cost, out _)
                && cost < bestCost)
            {
                bestCost = cost;
                best = endHex;
            }
        }

        return best;
    }

    public override Vector3? FindNearestEnemyBuilding()
    {
        if (Owner?.model == null || MapData == null || Movement == null) return null;

        List<Vector3> allPoints = new List<Vector3>(MapData.GetAllHexCoordinates());
        Vector3 startHex = Owner.unitMovementController?.CurrentHexCoordinate ?? MapData.WorldToHexCoordinate(Owner.model.transform.position);
        if (startHex == default) return null;

        Vector3? best = null;
        float bestCost = float.MaxValue;

        foreach (var cell in MapData.GetAllCells())
        {
            HexCellData bCell = cell;
            // 【探索重构-阶段6】AI 与玩家一致（全知索敌），不再受 AIFogService 视野限制
            if (bCell == null) continue;

            GameObject building = bCell.BulidingTypeOnHex_Building.Value;
            if (building == null) continue;

            var publicBuilding = building.GetComponent<PublicBuildingBase>();
            if (publicBuilding != null &&
                publicBuilding.CurrentDiscoveryState == PublicBuildingBase.DiscoveryState.Hidden)
            {
                continue;
            }

            // 【公共建筑系统-决策#36】敌方建筑 + 中立公共建筑都可攻击（"先遇到先打"）
            bool isEnemy = building.CompareTag("PlayerBuilding");
            bool isNeutral = building.CompareTag("NeutralBuilding");
            if (!isEnemy && !isNeutral) continue;

            Vector3 endHex = bCell.HexCoordinate;

            if (HexDistance(startHex, endHex) >= bestCost) continue;

            if (Movement.CalculateMinMovementCostBetweenTwoHexes(
                    allPoints, startHex, endHex,
                    Enums.MovementPurpose.MoveToAttack, out float cost, out _)
                && cost < bestCost)
            {
                bestCost = cost;
                best = endHex;
            }
        }

        return best;
    }

    // ── 移民建城（已移除）──────────────

    public override bool TryFoundCity()
    {
        // 【探索重构-阶段5.5】建新城功能移除，AI 始终返回 false。
        return false;
    }

    private bool IsValidAICityCell(HexCellData cell, GameObject settlerObj)
    {
        // 【探索重构-阶段5.5】建城检查已废弃。
        return false;
    }
}
