using System.Collections.Generic;
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

    private float _timer;
    private const float ExploreInterval = 3f;

    public AIAutoExplorer(
        IMapDataService mapData,
        EnemyModelManager enemyModelManager,
        MapVisualEventSO mapVisualEvent,
        GoldWallet goldWallet,
        AIManager aiManager,
        AIPlayerState aiState)
    {
        _mapData = mapData;
        _enemyModelManager = enemyModelManager;
        _mapVisualEvent = mapVisualEvent;
        _goldWallet = goldWallet;
        _aiManager = aiManager;
        _aiState = aiState;
    }

    public void Tick()
    {
        if (_aiManager.AIDisabled) return;
        // AI 操作间隔至少 1 秒
        if (UnityEngine.Time.time - _aiState.LastActionTime < 1f) return;
        _timer += UnityEngine.Time.deltaTime;
        if (_timer < ExploreInterval) return;
        _timer = 0f;

        TryAutoExplore();
    }

    private void TryAutoExplore()
    {
        // 【探索重构-阶段7】AI 探索需检查金币
        if (_goldWallet.GetGold(AIIndex) < _goldWallet.ExplorationCost) return;

        var ownedCells = GetAIOwnedCells();
        if (ownedCells.Count == 0) return;

        var candidates = new List<HexCellData>();
        var seen = new HashSet<(float, float, float)>();

        foreach (var cell in ownedCells)
        {
            for (int i = 0; i < 6; i++)
            {
                var neighbor = _mapData.GetNeighbor(cell, (Enums.HexDirection)i);
                if (neighbor == null || neighbor.IsExplored || neighbor.IsUnexplorable) continue;
                if (neighbor.HexType == Enums.HexType.LakeOrSea) continue;
                var key = (neighbor.HexCoordinate.x, neighbor.HexCoordinate.y, neighbor.HexCoordinate.z);
                if (seen.Add(key))
                    candidates.Add(neighbor);
            }
        }

        if (candidates.Count == 0) return;

        var target = candidates[UnityEngine.Random.Range(0, candidates.Count)];

        // 扣费
        if (!_goldWallet.TrySpendGold(AIIndex, _goldWallet.ExplorationCost)) return;

        // 探索 + 占领
        target.ExploreThisHexCell();
        target.Player_City_Index = new System.Collections.Generic.KeyValuePair<int, int>(AIIndex, 0);
        if (_enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData.TryGetValue(AIIndex, out var sphere))
            sphere[target.HexCoordinate] = target;

        // 收割资源（与玩家一致）
        HarvestAndReward(target);

        _aiState.LastActionTime = UnityEngine.Time.time;
        _mapVisualEvent?.Raise();
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
