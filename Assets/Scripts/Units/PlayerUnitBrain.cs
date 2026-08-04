using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

//****************************************
// 功能说明：玩家阵营单位 Brain。
//   目标查询使用玩家迷雾（HexCellData.IsVisible）。
//   TryFoundCity：玩家移民建城，迁移自 UIController.CityBuilderSkill。
//
// 【批次 B】FindNearestEnemy/Building 接入实际逻辑；TryFoundCity 实现。
//****************************************

public class PlayerUnitBrain : UnitBrainBase
{
    private IUnitRepository _unitRepository;

    // 建城依赖（仅 UnitID == 0 的移民才用到）
    private DiContainer _container;
    private IBuildingDataProvider _buildingData;
    private IUIConfigProvider _uiConfig;
    private PlayerModelManager _playerModelManager;
    private MapVisualEventSO _mapVisualEvent;
    private UnitRemovalService _unitRemovalService;
    private AudioManager _audioManager;

    public void Initialize(CharacterData owner, IUnitStrategy strategy,
                           IMapDataService mapDataService, IUnitRepository unitRepository,
                           UnitMovementSystem movementSystem,
                           CombatResolver combatResolver = null,
                           DiContainer container = null,
                           IBuildingDataProvider buildingData = null,
                           IUIConfigProvider uiConfig = null,
                           PlayerModelManager playerModelManager = null,
                           MapVisualEventSO mapVisualEvent = null,
                           UnitRemovalService unitRemovalService = null,
                           AudioManager audioManager = null,
                           PublicBuildingMarkerManager markerManager = null)
    {
        Owner = owner;
        activeStrategy = strategy;
        MapData = mapDataService;
        Movement = movementSystem;
        Combat = combatResolver;
        _unitRepository = unitRepository;
        _container = container;
        _buildingData = buildingData;
        _uiConfig = uiConfig;
        _playerModelManager = playerModelManager;
        _mapVisualEvent = mapVisualEvent;
        _unitRemovalService = unitRemovalService;
        _audioManager = audioManager;
        SetPublicBuildingMarkerManager(markerManager);
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

        foreach (var group in _unitRepository.AllEnemyUnitGroups)
        {
            if (group == null) continue;
            foreach (var cd in group.Values)
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
            // 【探索重构-阶段6】敌方建筑始终可见，不再检查 IsVisible
            GameObject building = cell.BulidingTypeOnHex_Building.Value;
            if (building == null) continue;

            // 【断供方案-阶段2】失能（断供）建筑不是攻击目标。
            // 仅过滤阵营 0/1 的断供建筑；中立公共建筑（伪阵营 ≥ 2）保持可攻击（决策#36）。
            BuildingSupplyGate supplyGate = building.GetComponent<BuildingSupplyGate>();
            if (supplyGate != null && !supplyGate.IsFunctional)
            {
                BuildingBase targetBase = building.GetComponent<BuildingBase>();
                int targetFaction = targetBase != null ? targetBase.Player_City_Index.Key : -1;
                if (targetFaction == 0 || targetFaction == 1) continue;
            }

            var publicBuilding = building.GetComponent<PublicBuildingBase>();
            if (publicBuilding != null &&
                publicBuilding.CurrentDiscoveryState == PublicBuildingBase.DiscoveryState.Hidden)
            {
                continue;
            }

            // 【公共建筑系统-决策#36】敌方建筑 + 中立公共建筑都可攻击（"先遇到先打"）
            bool isEnemy = building.CompareTag("EnemyBuilding");
            bool isNeutral = building.CompareTag("NeutralBuilding");
            if (!isEnemy && !isNeutral) continue;

            Vector3 endHex = cell.HexCoordinate;

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

    // ── 移民建城（已移除）──
    public override bool TryFoundCity()
    {
        // 【探索重构-阶段5.5】建新城功能移除，始终返回 false。
        return false;
    }

    private bool IsValidPlayerCityCell(HexCellData cell, GameObject settlerObj)
    {
        // 【探索重构-阶段5.5】建城检查已废弃。
        return false;
    }
}
