using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Zenject;
using UIToolkitDemo;

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

        // 直接用缓存表：寻路只读 allPoints，不需要防御性拷贝
        List<Vector3> allPoints = MapData.GetAllHexCoordinates();
        Vector3 startHex = SelfHexCoordinate;
        if (startHex == default) return null;

        // 收集候选 → 升序 → 限量寻路（旧实现对每个敌方单位跑一次完整 Dijkstra）
        foreach (var group in _unitRepository.AllEnemyUnitGroups)
        {
            if (group == null) continue;
            foreach (var cd in group.Values)
            {
                if (cd?.model == null || cd.currentHp <= 0) continue;

                var enemyMC = cd.unitMovementController;
                if (enemyMC == null) continue;

                AddTargetCandidate(startHex, enemyMC.CurrentHexCoordinate);
            }
        }

        return PickNearestByPathCost(allPoints, startHex);
    }

    public override Vector3? FindNearestEnemyBuilding()
    {
        if (Owner?.model == null || MapData == null || Movement == null) return null;

        // 直接用缓存表：寻路只读 allPoints，不需要防御性拷贝
        List<Vector3> allPoints = MapData.GetAllHexCoordinates();
        Vector3 startHex = SelfHexCoordinate;
        if (startHex == default) return null;

        // 收集候选 → 升序 → 限量寻路（旧实现对每个建筑格跑一次完整 Dijkstra）
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

            AddTargetCandidate(startHex, cell.HexCoordinate);
        }

        return PickNearestByPathCost(allPoints, startHex);
    }

    // ── 隔绝目标查询（忽略可达性）────────────────────────
    // 与上面两个查询的过滤条件完全一致，只把"可达且代价最小"换成"六边形距离最近"。
    // 用于目标被海洋完全隔绝时仍能识别其方位，进而走到最接近的岸格。

    public override Vector3? FindNearestEnemyIgnoringReachability()
    {
        if (Owner?.model == null || MapData == null) return null;

        Vector3 startHex = SelfHexCoordinate;
        if (startHex == default) return null;

        Vector3? best = null;
        float bestDist = float.MaxValue;

        foreach (var group in _unitRepository.AllEnemyUnitGroups)
        {
            if (group == null) continue;
            foreach (var cd in group.Values)
            {
                if (cd?.model == null || cd.currentHp <= 0) continue;

                var enemyMC = cd.unitMovementController;
                if (enemyMC == null) continue;

                float dist = HexDistance(startHex, enemyMC.CurrentHexCoordinate);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = enemyMC.CurrentHexCoordinate;
                }
            }
        }

        return best;
    }

    public override Vector3? FindNearestBuildingIgnoringReachability()
    {
        if (Owner?.model == null || MapData == null) return null;

        Vector3 startHex = SelfHexCoordinate;
        if (startHex == default) return null;

        Vector3? best = null;
        float bestDist = float.MaxValue;

        foreach (var cell in MapData.GetAllCells())
        {
            GameObject building = cell.BulidingTypeOnHex_Building.Value;
            if (building == null) continue;

            // 【断供方案-阶段2】失能建筑不是攻击目标（仅阵营 0/1；中立公共建筑豁免，决策#36）
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

            bool isEnemy = building.CompareTag("EnemyBuilding");
            bool isNeutral = building.CompareTag("NeutralBuilding");
            if (!isEnemy && !isNeutral) continue;

            float dist = HexDistance(startHex, cell.HexCoordinate);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = cell.HexCoordinate;
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
