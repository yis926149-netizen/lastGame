using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 多玩家金币钱包：按 PlayerIndex 索引分别追踪金币。
/// PlayerIndex 0 = 玩家，1 = AI。
/// 【探索重构-阶段7】支持玩家和 AI 各自的独立金币。
/// </summary>
public class GoldWallet : IPlayerResourceWallet
{
    private readonly Dictionary<int, int> _gold = new Dictionary<int, int>();

    /// <summary>起始金币（双方一致）</summary>
    public int StartingGold { get; set; } = 100;

    /// <summary>每秒被动收入（双方一致）</summary>
    public int PassiveIncomePerTick { get; set; } = 2;

    /// <summary>探索固定费用</summary>
    public int ExplorationCost { get; set; } = 50;

    /// <summary>出牌费用</summary>
    public int CardCost { get; set; } = 10;

    /// <summary>玩家（Index 0）金币变动事件</summary>
    public event System.Action<int> OnGoldChanged;

    // ---- 便捷属性（玩家 Gold） ----

    public int Gold => GetGold(0);

    // ---- 多玩家 API ----

    public int GetGold(int playerIndex)
    {
        return _gold.TryGetValue(playerIndex, out int v) ? v : 0;
    }

    public void InitPlayer(int playerIndex)
    {
        _gold[playerIndex] = StartingGold;
    }

    public void AddGold(int playerIndex, int amount)
    {
        if (amount <= 0) return;
        var key = playerIndex;
        if (!_gold.ContainsKey(key)) _gold[key] = 0;
        _gold[key] += amount;

        if (playerIndex == 0)
            OnGoldChanged?.Invoke(_gold[key]);
    }

    public bool TrySpendGold(int playerIndex, int amount)
    {
        if (amount <= 0) return true;
        var key = playerIndex;
        if (!_gold.TryGetValue(key, out int balance) || balance < amount)
            return false;
        _gold[key] = balance - amount;

        if (playerIndex == 0)
            OnGoldChanged?.Invoke(_gold[key]);
        return true;
    }

    // ---- IPlayerResourceWallet（玩家 Index 0）----

    bool IPlayerResourceWallet.TrySpend(ExplorationCost cost)
    {
        return TrySpendGold(0, cost.Amount);
    }

    bool IPlayerResourceWallet.CanAfford(ExplorationCost cost)
    {
        return GetGold(0) >= cost.Amount;
    }
}

/// <summary>
/// 探索成本按地块自身的预生成奖励类型决定（见 ExplorationRewardConfigSO.explorationCostsByType）；
/// 地块没有预生成奖励或配置缺失时回退 GoldWallet.ExplorationCost。
/// </summary>
public class FixedExplorationCostProvider : IExplorationCostProvider
{
    private readonly GoldWallet _wallet;
    private readonly ExplorationRewardConfigSO _rewardConfig;

    public FixedExplorationCostProvider(GoldWallet wallet, ExplorationRewardConfigSO rewardConfig)
    {
        _wallet = wallet;
        _rewardConfig = rewardConfig;
    }

    public ExplorationCost GetCost(HexCellData targetCell)
    {
        int amount = _wallet.ExplorationCost;
        if (_rewardConfig != null && targetCell != null && targetCell.ExplorationReward != null)
        {
            amount = _rewardConfig.GetExplorationCost(targetCell.ExplorationReward.RewardType);
        }
        return new ExplorationCost("Gold", amount);
    }
}
