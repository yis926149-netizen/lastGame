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

    public PublicBuildingGenerator(
        IMapDataService mapDataService,
        IPublicBuildingDataProvider dataProvider,
        EnemyModelManager enemyModelManager,
        GameLoop gameLoop,
        DiContainer container,
        IUIConfigProvider uiConfigProvider)
    {
        _mapDataService = mapDataService;
        _dataProvider = dataProvider;
        _enemyModelManager = enemyModelManager;
        _gameLoop = gameLoop;
        _container = container;
        _uiConfigProvider = uiConfigProvider;
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
        if (buildingCount == 0)
        {
            Debug.LogWarning("[PublicBuildingGenerator] No public buildings configured in PublicBuildingSO.");
            return startPlayerIndex;
        }

        // 【待确认项3.2】暂定简单规则：随机选取 N 个陆地格作为根格，尝试放置
        // 后续讨论后补充：数量策略、避开出生点、最小间距、形状旋转等
        int targetCount = Mathf.Min(3, buildingCount); // 暂定生成3个公共建筑（或配置数量上限）
        var candidates = GetCandidateRootHexes();

        if (candidates.Count == 0)
        {
            Debug.LogWarning("[PublicBuildingGenerator] No suitable candidate hexes for public buildings.");
            return startPlayerIndex;
        }

        System.Random random = SeedService.GetRandom("PublicBuilding");
        int playerIndexCounter = startPlayerIndex;

        for (int i = 0; i < targetCount && candidates.Count > 0; i++)
        {
            int buildingId = i % buildingCount; // 循环使用配置中的建筑类型
            int candidateIndex = random.Next(candidates.Count);
            HexCellData rootHex = candidates[candidateIndex];
            candidates.RemoveAt(candidateIndex); // 避免重复

            bool success = TrySpawnPublicBuilding(buildingId, rootHex, playerIndexCounter);
            if (success)
            {
                // 成功生成，分配下一个 PlayerIndex
                _enemyModelManager.PublicBuildingPlayerIndexes.Add(playerIndexCounter);
                playerIndexCounter++;
            }
        }

        Debug.Log($"[PublicBuildingGenerator] Generated {playerIndexCounter - startPlayerIndex} public buildings.");
        return playerIndexCounter;
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
            if (landNeighbors < 4) continue;

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
        }

        Debug.Log($"[PublicBuildingGenerator] Marked {unexplorableArea.Count} hexes as unexplorable.");
    }
}
