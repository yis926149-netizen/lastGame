using System.Collections.Generic;
using UnityEngine;
using Zenject;

/// <summary>
/// AI 自动探索：每隔一定时间自动探索邻接己方领地且未探索的中立地块。
/// 【统一开发入口】开发（校验/扣费/归属/重算/收割）改走 IExplorationService，
/// 与玩家共用同一服务，中立校验保证同时开发互斥；奖励按统一事件结算。
/// </summary>
public class AIAutoExplorer : ITickable
{
    private const int AIIndex = 1;

    private readonly IMapDataService _mapData;
    private readonly EnemyModelManager _enemyModelManager;
    private readonly GoldWallet _goldWallet;
    private readonly AIManager _aiManager;
    private readonly AIPlayerState _aiState;
    private readonly GameLoop _gameLoop;
    private readonly ILogisticsService _logisticsService;
    private readonly IExplorationCostProvider _costProvider;
    private readonly AIEntityFactory _aiFactory;
    private readonly IExplorationService _explorationService;
    private readonly AIConfigProvider _aiConfig;

    private float _timer;

    public AIAutoExplorer(
        IMapDataService mapData,
        EnemyModelManager enemyModelManager,
        GoldWallet goldWallet,
        AIManager aiManager,
        AIPlayerState aiState,
        GameLoop gameLoop,
        ILogisticsService logisticsService,
        IExplorationCostProvider costProvider,
        AIEntityFactory aiFactory,
        IExplorationService explorationService,
        AIConfigProvider aiConfig = null)
    {
        _mapData = mapData;
        _enemyModelManager = enemyModelManager;
        _goldWallet = goldWallet;
        _aiManager = aiManager;
        _aiState = aiState;
        _gameLoop = gameLoop;
        _logisticsService = logisticsService;
        _costProvider = costProvider;
        _aiFactory = aiFactory;
        _explorationService = explorationService;
        _aiConfig = aiConfig;

        _explorationService.ExplorationRewardTriggered += OnExplorationRewardTriggered;
    }

    public void Tick()
    {
        if (_gameLoop != null && _gameLoop.IsPaused) return;
        if (_aiManager.AIDisabled) return;
        _timer += UnityEngine.Time.deltaTime;
        if (_timer < _aiConfig.ExploreInterval) return;
        // 探索准备完成后等待全局动作窗口，不丢失已累计的计时。
        if (UnityEngine.Time.time - _aiState.LastActionTime < _aiConfig.GlobalActionMinInterval) return;

        if (TryAutoExplore())
        {
            _timer = 0f;
            _aiState.LastActionTime = UnityEngine.Time.time;
        }
    }

    private bool TryAutoExplore()
    {
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
                // 【统一开发入口-互斥】只选中立格，避免反复瞄准玩家已占格
                if (neighbor.Player_City_Index.Key != -1) continue;
                var key = (neighbor.HexCoordinate.x, neighbor.HexCoordinate.y, neighbor.HexCoordinate.z);
                if (seen.Add(key))
                    candidates.Add(neighbor);
            }
        }

        if (candidates.Count == 0) return false;

        var target = candidates[UnityEngine.Random.Range(0, candidates.Count)];

        // 【探索费用按奖励类型】按目标地块自身奖励类型做预算预筛（扣费由服务内部完成）
        if (_costProvider != null && _goldWallet.GetGold(AIIndex) < _costProvider.GetCost(target).Amount)
            return false;

        // 【统一开发入口】玩家/AI 共用同一服务：
        // 校验（含中立校验）→ 扣费 → 归属 → 连通重算 → 收割 → 奖励事件，同步完成互斥。
        return _explorationService.TryExplore(target, AIIndex) == ExploreResult.Success;
    }

    // ── 探索奖励结算（订阅统一事件，按阵营分发）────────────
    /// <summary>
    /// 消费地图生成时固化的奖励快照。
    /// 金币 → 进入 AI 钱包；军事 → 生成单位（溢出邻格）；战术 → AI 战术牌系统未实现，暂不发放；无奖励 → 无结算。
    /// </summary>
    private void OnExplorationRewardTriggered(HexCellData cell, int factionId)
    {
        if (factionId != AIIndex || cell == null) return;
        ExplorationRewardData reward = cell.TakeExplorationReward();
        if (reward == null)
        {
            Debug.LogWarning($"[AIAutoExplorer] 地块 {cell.HexCoordinate} 没有预生成奖励，跳过结算");
            return;
        }

        switch (reward.RewardType)
        {
            case ExplorationRewardConfigSO.ExplorationRewardType.None:
                break;

            case ExplorationRewardConfigSO.ExplorationRewardType.Gold:
                AddGoldReward(reward.GoldAmount);
                Debug.Log($"[AIAutoExplorer] 探索奖励：金币 +{reward.GoldAmount}");
                break;

            case ExplorationRewardConfigSO.ExplorationRewardType.MilitaryUnit:
                SpawnRewardUnits(cell, reward.UnitConfigs);
                break;

            case ExplorationRewardConfigSO.ExplorationRewardType.TacticalCard:
                // AI 战术牌系统尚未实现，战术奖励暂不发放
                Debug.Log("[AIAutoExplorer] 探索奖励：战术卡牌（AI 暂不发放）");
                break;

            case ExplorationRewardConfigSO.ExplorationRewardType.Building:
                SpawnRewardBuilding(cell, reward.BuildingConfig, reward.GoldAmount);
                break;
        }
    }

    private void SpawnRewardUnits(HexCellData targetCell, UnitConfigSO[] unitConfigs)
    {
        if (_aiFactory == null || unitConfigs == null) return;

        for (int i = 0; i < unitConfigs.Length; i++)
        {
            HexCellData spawnCell = targetCell;
            Vector3 spawnPos = targetCell.RealCenterWorldCoordinate;

            // 【程序化山脉-阶段 7.6】山格/水域不可部署（决策 ①）：目标格不合格时尝试溢出
            if (spawnCell.IsHaveUnit() || !MountainCellRule.CanSpawnUnitOnCell(spawnCell))
            {
                spawnCell = FindOverflowCell(spawnCell);
                if (spawnCell == null) continue;
                spawnPos = spawnCell.RealCenterWorldCoordinate;
            }

            UnitConfigSO unitConfig = unitConfigs[i];
            if (unitConfig == null)
            {
                Debug.LogWarning("[AIAutoExplorer] 探索奖励无可用单位配置（rewardUnits 为空），跳过生成");
                continue;
            }
            _aiFactory.GenerateUnit(unitConfig.Id, spawnPos);
        }
    }

    /// <summary>
    /// AI 建筑奖励：直接放置在被探索地块上；格子不合格或生成失败时降级为金币（与玩家侧同规则）。
    /// </summary>
    private void SpawnRewardBuilding(HexCellData cell, BuildingConfigSO config, int fallbackGoldAmount)
    {
        if (_aiFactory == null) return;

        if (config == null)
        {
            Debug.LogWarning("[AIAutoExplorer] 探索奖励：建筑奖励但 rewardBuildings 为空，降级为金币");
            DegradeToGold(fallbackGoldAmount);
            return;
        }

        // 建造资格：与玩家侧共用同一规则（RewardBuildingRule）
        if (!RewardBuildingRule.CanPlace(cell))
        {
            Debug.Log("[AIAutoExplorer] 探索奖励：地块不可建造，建筑奖励降级为金币");
            DegradeToGold(fallbackGoldAmount);
            return;
        }

        _aiFactory.GenerateBuilding(config, cell.RealCenterWorldCoordinate);
        Debug.Log($"[AIAutoExplorer] 探索奖励：建筑 {config.name}");
    }

    /// <summary>使用预生成的备用金币完成建筑奖励降级。</summary>
    private void DegradeToGold(int goldAmount)
    {
        AddGoldReward(goldAmount);
        Debug.Log($"[AIAutoExplorer] 探索奖励降级：金币 +{goldAmount}");
    }

    private void AddGoldReward(int goldAmount)
    {
        if (goldAmount > 0)
            _goldWallet.AddGold(AIIndex, goldAmount);
    }

    private HexCellData FindOverflowCell(HexCellData origin)
    {
        int maxRings = _aiConfig.MilitaryRewardOverflowRings;
        var visited = new HashSet<Vector3> { origin.HexCoordinate };
        var frontier = new List<HexCellData> { origin };

        for (int ring = 0; ring < maxRings; ring++)
        {
            var nextFrontier = new List<HexCellData>();
            foreach (var cell in frontier)
            {
                for (int dir = 0; dir < 6; dir++)
                {
                    var neighbor = _mapData.GetNeighbor(cell, (Enums.HexDirection)dir);
                    if (neighbor == null) continue;
                    if (!visited.Add(neighbor.HexCoordinate)) continue;

                    if (neighbor.HexType != Enums.HexType.LakeOrSea &&
                        MountainCellRule.CanSpawnUnitOnCell(neighbor) &&
                        neighbor.BulidingTypeOnHex_Building.Key == Enums.BulidingType.NoBuilding &&
                        !neighbor.IsHaveUnit())
                    {
                        return neighbor;
                    }

                    nextFrontier.Add(neighbor);
                }
            }
            frontier = nextFrontier;
        }
        return null;
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
