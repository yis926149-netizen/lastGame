using System.Collections.Generic;
using UnityEngine;
using Zenject;

/// <summary>
/// AI 自动探索：每隔一定时间自动探索邻接己方领地且未探索的地块。
/// 【探索重构-阶段5.5】替代旧的"单位移动后自动探索"机制。
/// AI 探索不消耗资源（金币系统尚未多阵营化，先免费）。
/// </summary>
public class AIAutoExplorer : ITickable
{
    private const int AIIndex = 1;

    private readonly IMapDataService _mapData;
    private readonly EnemyModelManager _enemyModelManager;
    private readonly MapVisualEventSO _mapVisualEvent;
    private readonly GoldWallet _goldWallet;
    private readonly AIManager _aiManager;
    private readonly AIPlayerState _aiState;
    private readonly GameLoop _gameLoop;
    private readonly ILogisticsService _logisticsService;
    private readonly ExplorationRewardConfigSO _rewardConfig;
    private readonly AIEntityFactory _aiFactory;

    private float _timer;
    private const float ExploreInterval = 1.5f;

    public AIAutoExplorer(
        IMapDataService mapData,
        EnemyModelManager enemyModelManager,
        MapVisualEventSO mapVisualEvent,
        GoldWallet goldWallet,
        AIManager aiManager,
        AIPlayerState aiState,
        GameLoop gameLoop,
        ILogisticsService logisticsService,
        ExplorationRewardConfigSO rewardConfig,
        AIEntityFactory aiFactory)
    {
        _mapData = mapData;
        _enemyModelManager = enemyModelManager;
        _mapVisualEvent = mapVisualEvent;
        _goldWallet = goldWallet;
        _aiManager = aiManager;
        _aiState = aiState;
        _gameLoop = gameLoop;
        _logisticsService = logisticsService;
        _rewardConfig = rewardConfig;
        _aiFactory = aiFactory;
    }

    public void Tick()
    {
        if (_gameLoop != null && _gameLoop.IsPaused) return;
        if (_aiManager.AIDisabled) return;
        _timer += UnityEngine.Time.deltaTime;
        if (_timer < ExploreInterval) return;
        // 探索准备完成后等待全局动作窗口，不丢失已累计的计时。
        if (UnityEngine.Time.time - _aiState.LastActionTime < 1f) return;

        if (TryAutoExplore())
        {
            _timer = 0f;
            _aiState.LastActionTime = UnityEngine.Time.time;
        }
    }

    private bool TryAutoExplore()
    {
        // 【探索重构-阶段7】AI 探索需检查金币
        if (_goldWallet.GetGold(AIIndex) < _goldWallet.ExplorationCost) return false;

        var ownedCells = GetAIOwnedCells();
        if (ownedCells.Count == 0) return false;

        var candidates = new List<HexCellData>();
        var seen = new HashSet<(float, float, float)>();

        foreach (var cell in ownedCells)
        {
            if (_logisticsService != null && !_logisticsService.IsLogisticsConnected(cell, AIIndex)) continue;
            for (int i = 0; i < 6; i++)
            {
                var neighbor = _mapData.GetNeighbor(cell, (Enums.HexDirection)i);
                if (neighbor == null || neighbor.IsExploredBy(AIIndex) || neighbor.IsUnexplorable) continue;
                if (neighbor.HexType == Enums.HexType.LakeOrSea) continue;
                var key = (neighbor.HexCoordinate.x, neighbor.HexCoordinate.y, neighbor.HexCoordinate.z);
                if (seen.Add(key))
                    candidates.Add(neighbor);
            }
        }

        if (candidates.Count == 0) return false;

        var target = candidates[UnityEngine.Random.Range(0, candidates.Count)];

        // 扣费
        if (!_goldWallet.TrySpendGold(AIIndex, _goldWallet.ExplorationCost)) return false;

        // 探索 + 占领
        target.ExploreBy(AIIndex);
        var cityKey = new System.Collections.Generic.KeyValuePair<int, int>(AIIndex, 0);
        target.Player_City_Index = cityKey;
        if (_enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData.TryGetValue(AIIndex, out var sphere))
            sphere[target.HexCoordinate] = target;
        if (!_enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData.TryGetValue(cityKey, out var cityDict))
        {
            cityDict = new Dictionary<Vector3, HexCellData>();
            _enemyModelManager.Enemy_SingleCity_SphereOfInfluence_HexC_HexCellData[cityKey] = cityDict;
        }
        cityDict[target.HexCoordinate] = target;
        _logisticsService.RecalculateAll();

        // 收割资源（与玩家一致）
        HarvestAndReward(target);

        // 探索奖励：掷骰生成军事单位
        TrySpawnExplorationRewardUnit(target);

        _mapVisualEvent?.Raise();
        return true;
    }

    private void HarvestAndReward(HexCellData cell)
    {
        if (cell == null) return;
        int reward = 5;
        var resource = cell.GetResource();
        switch (resource)
        {
            case Enums.ResourceType.Animals:   reward += 20; break;
            case Enums.ResourceType.Plants:    reward += 15; break;
            case Enums.ResourceType.Minerals:  reward += 25; break;
            case Enums.ResourceType.Chest:     reward += 30; break;
            case Enums.ResourceType.HealthPack: reward += 10; break;
        }
        if (resource != Enums.ResourceType.None)
        {
            cell.ReapResource();
            if (cell.resourceModel != null) { UnityEngine.Object.Destroy(cell.resourceModel); cell.resourceModel = null; }
        }
        _goldWallet.AddGold(AIIndex, reward);
    }

    private void TrySpawnExplorationRewardUnit(HexCellData targetCell)
    {
        if (_rewardConfig == null || _aiFactory == null) return;

        int unitCount = RollRewardUnitCount();
        for (int i = 0; i < unitCount; i++)
        {
            HexCellData spawnCell = targetCell;
            Vector3 spawnPos = targetCell.RealCenterWorldCoordinate;

            if (spawnCell.IsHaveUnit())
            {
                spawnCell = FindOverflowCell(spawnCell);
                if (spawnCell == null) continue;
                spawnPos = spawnCell.RealCenterWorldCoordinate;
            }

            _aiFactory.GenerateUnit(_rewardConfig.rewardUnitID, spawnPos);
        }
    }

    private int RollRewardUnitCount()
    {
        var tiers = _rewardConfig.unitCountTiers;
        if (tiers == null || tiers.Length == 0) return 0;
        return tiers[UnityEngine.Random.Range(0, tiers.Length)];
    }

    private HexCellData FindOverflowCell(HexCellData origin)
    {
        var candidates = new List<HexCellData>();
        for (int dir = 0; dir < 6; dir++)
        {
            var neighbor = _mapData.GetNeighbor(origin, (Enums.HexDirection)dir);
            if (neighbor == null) continue;
            if (neighbor.HexType == Enums.HexType.LakeOrSea) continue;
            if (neighbor.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding) continue;
            if (neighbor.IsHaveUnit()) continue;
            candidates.Add(neighbor);
        }
        if (candidates.Count == 0) return null;
        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    private List<HexCellData> GetAIOwnedCells()
    {
        if (!_enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData.TryGetValue(AIIndex, out var sphere))
            return new List<HexCellData>();

        var list = new List<HexCellData>();
        foreach (var kv in sphere)
            if (kv.Value != null)
                list.Add(kv.Value);
        return list;
    }
}
