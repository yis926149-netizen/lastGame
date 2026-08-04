using System.Collections.Generic;
using UnityEngine;
using Zenject;

//****************************************
// 【公共建筑系统-决策#11/#28/#38】公共建筑随机生成器
// 职责：按一定规律在地图中立区域生成公共建筑（多格、中立态）
// 生成时机：地图生成后、AI/玩家初始化前（GameFlowManager.Initialize 中调用）
// 生成规则：待确认项3.2（数量、位置约束、最小间距等），此处仅搭骨架
//****************************************

public class PublicBuildingGenerator
{
    private readonly IMapDataService _mapDataService;
    private readonly IPublicBuildingDataProvider _dataProvider;
    private readonly EnemyModelManager _enemyModelManager;
    private readonly GameLoop _gameLoop;
    private readonly DiContainer _container;
    private readonly IUIConfigProvider _uiConfigProvider;
    private readonly MapGenerationConfigSO _config;
    private readonly PublicBuildingMarkerManager _markerManager;
    private readonly ArenaEventManager _arenaEventManager;

    public PublicBuildingGenerator(
        IMapDataService mapDataService,
        IPublicBuildingDataProvider dataProvider,
        EnemyModelManager enemyModelManager,
        GameLoop gameLoop,
        DiContainer container,
        IUIConfigProvider uiConfigProvider,
        MapGenerationConfigSO config,
        PublicBuildingMarkerManager markerManager,
        ArenaEventManager arenaEventManager)
    {
        _mapDataService = mapDataService;
        _dataProvider = dataProvider;
        _enemyModelManager = enemyModelManager;
        _gameLoop = gameLoop;
        _container = container;
        _uiConfigProvider = uiConfigProvider;
        _config = config;
        _markerManager = markerManager;
        _arenaEventManager = arenaEventManager;
    }

    /// <summary>
    /// 生成所有公共建筑（决策#28/#38）。
    /// 在 GameFlowManager.Initialize 中调用：地图生成后、势力范围初始化前。
    /// </summary>
    /// <param name="startPlayerIndex">公共建筑的起始 PlayerIndex（玩家0 + 真AI数量后递增）</param>
    /// <returns>生成完毕后下一个可用的 PlayerIndex</returns>
    public int GenerateAll(int startPlayerIndex)
    {
        int buildingCount = _dataProvider.GetBuildingCount();
        Debug.Log($"<color=cyan>[PublicBuildingGenerator] GenerateAll 开始, buildingCount={buildingCount}, startPlayerIndex={startPlayerIndex}</color>");
        if (buildingCount == 0)
        {
            Debug.LogWarning("[PublicBuildingGenerator] No public buildings configured in PublicBuildingSO.");
            return startPlayerIndex;
        }

        // 【待确认项3.2】暂定简单规则：随机选取 N 个陆地格作为根格，尝试放置
        // 后续讨论后补充：数量策略、避开出生点、最小间距、形状旋转等
        var validBuildingIds = GetValidBuildingIds(buildingCount);
        int targetCount = Mathf.Min(3, validBuildingIds.Count); // 暂定生成3个公共建筑（或配置数量上限）
        if (targetCount == 0)
        {
            Debug.LogError("[PublicBuildingGenerator] No valid public building prefabs configured.");
            return startPlayerIndex;
        }

        var candidates = GetCandidateRootHexes();
        Debug.Log($"<color=orange>[PublicBuildingGenerator] 候选根格数量={candidates.Count}，中立区 z=[{_config.neutralZone.zMin},{_config.neutralZone.zMax}]</color>");

        if (candidates.Count == 0)
        {
            Debug.LogWarning("[PublicBuildingGenerator] No suitable candidate hexes for public buildings.");
            return startPlayerIndex;
        }

        System.Random random = SeedService.GetRandom("PublicBuilding");
        System.Random markerRandom = SeedService.GetRandom("PublicBuildingMarker");
        int playerIndexCounter = startPlayerIndex;

        int spawned = 0;
        while (spawned < targetCount && candidates.Count > 0)
        {
            int buildingId = validBuildingIds[spawned % validBuildingIds.Count];
            int candidateIndex = random.Next(candidates.Count);
            HexCellData rootHex = candidates[candidateIndex];
            candidates.RemoveAt(candidateIndex);

            bool success = TrySpawnPublicBuilding(buildingId, rootHex, playerIndexCounter);
            if (success)
            {
                Debug.Log($"<color=cyan>[PublicBuildingGenerator] 建筑 #{playerIndexCounter - startPlayerIndex} 成功, buildingId={buildingId}, rootHex=({rootHex.HexCoordinate.x:F0},{rootHex.HexCoordinate.y:F0},{rootHex.HexCoordinate.z:F0})</color>");
                PublicBuildingBase publicBuilding = rootHex.publicBuildingRoot;
                _markerManager.CreateMarker(publicBuilding, markerRandom,
                    _dataProvider.GetMarkerPrefab(),
                    _dataProvider.GetMarkerIcon(buildingId));

                // 成功生成，分配下一个 PlayerIndex
                _enemyModelManager.PublicBuildingPlayerIndexes.Add(playerIndexCounter);
                playerIndexCounter++;
                spawned++;
            }
            else
            {
                Debug.LogWarning($"[PublicBuildingGenerator] 建筑失败, buildingId={buildingId}, rootHex=({rootHex.HexCoordinate.x:F0},{rootHex.HexCoordinate.y:F0},{rootHex.HexCoordinate.z:F0})");
            }
        }

        Debug.Log($"[PublicBuildingGenerator] Generated {playerIndexCounter - startPlayerIndex} public buildings.");
        return playerIndexCounter;
    }

    private List<int> GetValidBuildingIds(int buildingCount)
    {
        var validIds = new List<int>(buildingCount);
        for (int buildingId = 0; buildingId < buildingCount; buildingId++)
        {
            GameObject prefab = _dataProvider.GetPrefab(buildingId);
            if (prefab == null || prefab.GetComponent<PublicBuildingBase>() == null)
            {
                Debug.LogError(
                    $"[PublicBuildingGenerator] Skipping invalid buildingId={buildingId}: " +
                    "prefab is null or its root does not have a PublicBuildingBase component.",
                    prefab);
                continue;
            }

            validIds.Add(buildingId);
        }

        return validIds;
    }

    // ── 候选根格筛选（决策#3.2 待补充约束）──────────
    private List<HexCellData> GetCandidateRootHexes()
    {
        var candidates = new List<HexCellData>();
        foreach (var cell in _mapDataService.GetAllCells())
        {
            if (cell == null) continue;

            // 基础约束：陆地、无建筑、无主
            if (cell.HexType == Enums.HexType.LakeOrSea) continue;
            if (cell.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding) continue;
            if (cell.Player_City_Index.Key != -1) continue;

            // 排除地图边缘：至少 4 个方向有陆地邻居（过滤边角孤立格）
            int landNeighbors = 0;
            for (int d = 0; d < 6; d++)
            {
                var n = _mapDataService.GetNeighbor(cell, (Enums.HexDirection)d);
                if (n != null && n.HexType != Enums.HexType.LakeOrSea)
                    landNeighbors++;
            }
            if (landNeighbors < 2) continue;

            // 限定中立区域
            if (!_config.neutralZone.Contains(cell.HexCoordinate.z)) continue;

            // 【竞技场-阶段二】公共建筑生成避开预留区（含外 1 环，玩法文档 §7.3）
            if (_arenaEventManager != null && _arenaEventManager.IsNearReservedZone(cell, 1)) continue;

            // 【待确认项3.2】后续补充：
            // - 避开玩家/AI出生点一定范围
            // - 最小间距（已生成公共建筑之间保持距离）
            // - 可建地块限制（例如不能在河流、森林密集区等）

            candidates.Add(cell);
        }
        return candidates;
    }

    // ── 尝试在根格生成一个公共建筑（决策#4/#34）────
    private bool TrySpawnPublicBuilding(int buildingId, HexCellData rootHex, int assignedPlayerIndex)
    {
        // 1. 获取配置
        GameObject prefab = _dataProvider.GetPrefab(buildingId);
        if (prefab == null)
        {
            Debug.LogWarning($"[PublicBuildingGenerator] Prefab for buildingId={buildingId} is null.");
            return false;
        }

        float captureHp = _dataProvider.GetCaptureHp(buildingId);
        float defenseHp = _dataProvider.GetDefenseHp(buildingId);
        Enums.HexDirection[] subDirs = _dataProvider.GetSubHexDirections(buildingId);

        // 2. 检查子格是否可用（4格形状：根格 + 3个子格）
        foreach (var dir in subDirs)
        {
            HexCellData subHex = _mapDataService.GetNeighbor(rootHex, dir);
            if (subHex == null || 
                subHex.HexType == Enums.HexType.LakeOrSea ||
                subHex.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding)
            {
                return false; // 子格不可用，放弃此位置
            }
        }

        // 3. 实例化建筑模型（放在根格中心）
        GameObject instance = Object.Instantiate(prefab);
        instance.transform.SetParent(GameObject.Find("NeutralBuilding")?.transform, false);
        instance.transform.position = rootHex.RealCenterWorldCoordinate;
        instance.tag = "NeutralBuilding";

        // 4. 添加 PublicBuildingBase 组件（具体子类由 prefab 预先挂载或此处动态添加）
        // 此处假设 prefab 上已挂载具体子类（如 PublicBuilding_Fort），否则需要 AddComponent<具体子类>()
        PublicBuildingBase pb = instance.GetComponent<PublicBuildingBase>();
        if (pb == null)
        {
            Debug.LogWarning($"[PublicBuildingGenerator] Prefab {prefab.name} does not have PublicBuildingBase component.");
            Object.Destroy(instance);
            return false;
        }

        // 5. Zenject 注入依赖
        _container.Inject(pb);

        // 6. 初始化 BuildingData
        pb.buildingData = new BuildingData(Enums.BulidingType.PublicBuilding, null, buildingId);
        pb.buildingData.controller = null; // 公共建筑不使用旧 BuildingController
        pb.bulidingType = Enums.BulidingType.PublicBuilding;

        // 7. 初始化公共建筑（多格、两阶段HP、PlayerIndex）
        pb.Initialize(rootHex, subDirs, assignedPlayerIndex, captureHp, defenseHp, _mapDataService);

        // 8. UI 画布 + 血条（中立色）
        SpawnUIWiring.WireBuildingCanvas(instance, pb, Color.white, _container, _uiConfigProvider);

        // 9. 注册到 GameLoop（死亡检测）
        _gameLoop.RegisterPublicBuilding(pb);

        // 10. 【决策#42】标记公共建筑占位格+周围一环为不可探索
        MarkUnexplorableArea(pb.OccupiedHexes);

        // 11. 开局仅保留数据占位，隐藏建筑模型及其子级血条。
        instance.SetActive(false);

        Debug.Log($"[PublicBuildingGenerator] Spawned public building ID={buildingId} at {rootHex.HexCoordinate}, PlayerIndex={assignedPlayerIndex}");
        return true;
    }

    /// <summary>
    /// 【公共建筑系统-决策#42】标记公共建筑占位格+各格外一环为不可探索。
    /// 这些地块只能通过占领公共建筑获得，不能通过探索系统主动探索。
    /// </summary>
    private void MarkUnexplorableArea(List<HexCellData> occupiedHexes)
    {
        HashSet<HexCellData> unexplorableArea = new HashSet<HexCellData>();

        // 收集所有占位格及其外一环
        foreach (var hex in occupiedHexes)
        {
            // 占位格本身
            unexplorableArea.Add(hex);

            // 占位格周围一环
            for (int dir = 0; dir < 6; dir++)
            {
                var neighbor = _mapDataService.GetNeighbor(hex, (Enums.HexDirection)dir);
                if (neighbor != null)
                {
                    unexplorableArea.Add(neighbor);
                }
            }
        }

        // 去重后统一标记
        foreach (var hex in unexplorableArea)
        {
            hex.IsUnexplorable = true;
            if (hex.resourceModel != null)
                hex.resourceModel.SetActive(false);
        }

        Debug.Log($"[PublicBuildingGenerator] Marked {unexplorableArea.Count} hexes as unexplorable.");
    }
}

/// <summary>管理未发现公共建筑的近似位置提示。</summary>
public class PublicBuildingMarkerManager
{
    private sealed class MarkerEntry
    {
        public GameObject View;
        public Vector3 ApproximateHex;
    }

    private readonly IMapDataService _mapDataService;
    private readonly Dictionary<PublicBuildingBase, MarkerEntry> _markers =
        new Dictionary<PublicBuildingBase, MarkerEntry>();

    public PublicBuildingMarkerManager(IMapDataService mapDataService)
    {
        _mapDataService = mapDataService;
    }

    public void CreateMarker(PublicBuildingBase building, System.Random random,
        GameObject markerPrefab, Sprite markerIcon)
    {
        if (building == null || building.RootHex == null || _markers.ContainsKey(building)) return;

        if (markerPrefab == null) return;

        HexCellData approximateCell = FindApproximateCell(building.RootHex, random);
        Vector3 worldPosition = approximateCell.RealCenterWorldCoordinate + Vector3.up * 5f;
        GameObject view = Object.Instantiate(markerPrefab, worldPosition, Quaternion.identity);

        var markerView = view.GetComponent<PublicBuildingMarkerView>();
        if (markerView != null)
            markerView.SetIcon(markerIcon);

        _markers.Add(building, new MarkerEntry
        {
            View = view,
            ApproximateHex = approximateCell.HexCoordinate
        });
    }

    public void RemoveMarker(PublicBuildingBase building)
    {
        if (building == null || !_markers.TryGetValue(building, out var entry)) return;

        if (entry.View != null)
            Object.Destroy(entry.View);
        _markers.Remove(building);
    }

    public Vector3? FindNearestApproximateHex(Vector3 fromHex)
    {
        Vector3? nearest = null;
        float bestDistance = float.MaxValue;

        foreach (var pair in _markers)
        {
            if (pair.Key == null ||
                pair.Key.CurrentDiscoveryState != PublicBuildingBase.DiscoveryState.Hidden)
            {
                continue;
            }

            Vector3 target = pair.Value.ApproximateHex;
            float distance = HexDistance(fromHex, target);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = target;
            }
        }

        return nearest;
    }

    private HexCellData FindApproximateCell(HexCellData root, System.Random random)
    {
        var candidates = new List<HexCellData>();
        var seen = new HashSet<HexCellData>();
        foreach (var occupied in root.publicBuildingRoot.OccupiedHexes)
        {
            for (int direction = 0; direction < 6; direction++)
            {
                HexCellData neighbor = _mapDataService.GetNeighbor(
                    occupied, (Enums.HexDirection)direction);
                if (neighbor == null ||
                    neighbor.HexType == Enums.HexType.LakeOrSea ||
                    neighbor.movementCost == float.MaxValue ||
                    root.publicBuildingRoot.OccupiedHexes.Contains(neighbor) ||
                    !seen.Add(neighbor))
                {
                    continue;
                }

                candidates.Add(neighbor);
            }
        }

        return candidates.Count > 0 ? candidates[random.Next(candidates.Count)] : root;
    }

    private static float HexDistance(Vector3 a, Vector3 b)
    {
        return (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z)) * 0.5f;
    }
}
