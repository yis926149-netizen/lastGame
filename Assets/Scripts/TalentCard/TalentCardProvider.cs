using System.Collections.Generic;
using GameConfig;

//****************************************
//功能说明：天赋卡提供者（对象化 + Excel 数值化，阶段6 唯一主源）。
//         启用卡列表仅由 TalentCardBalanceDatabaseSO（Excel 生成）决定，
//         通过 talentId（稳定字符串 ID）→ 资源 SO 解析出卡资源对象（图标等人工引用）。
//         Excel 未加载时抛异常，暴露配置缺失。
//****************************************
public class TalentCardProvider
{
    private readonly TalentCardPoolSO _pool;                    // 资源池（图标等人工引用）
    private readonly TalentCardBalanceDatabaseSO _balance;      // Excel 数值（只读）

    public TalentCardProvider(TalentCardPoolSO pool, TalentCardBalanceDatabaseSO balance = null)
    {
        _pool = pool;
        _balance = balance;
    }

    private TalentCardBalanceDatabaseSO RequireBalance()
    {
        if (_balance == null)
            throw new System.InvalidOperationException(
                "[TalentCard] Excel 天赋数值库未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 TalentCardBalanceDatabaseSO。");
        return _balance;
    }

    /// <summary>启用天赋卡列表（资源对象），Excel 唯一主源。</summary>
    public IReadOnlyList<TalentCardConfigSO> GetEnabledCards()
    {
        var result = new List<TalentCardConfigSO>();
        foreach (var b in RequireBalance().EnabledTalents)
        {
            var config = FindResource(b.talentId);
            if (config != null) result.Add(config);
        }
        return result;
    }

    /// <summary>
    /// 从启用天赋卡中按「候选袋」随机抽 count 张：一袋内无放回，抽空后再开启同样的新袋。
    /// 因此会优先保证候选不重复；仅当 count 超过当前可用卡数时才进入下一袋并产生重复。
    /// repeatable=false 的卡只进入第一袋，repeatable=true 的卡可进入后续新袋。
    /// </summary>
    public List<TalentCardConfigSO> DrawRandom(int count)
    {
        var firstBag = new List<TalentCardConfigSO>(GetEnabledCards());
        firstBag.RemoveAll(c => c == null);
        if (firstBag.Count == 0 || count <= 0)
            return new List<TalentCardConfigSO>();

        var refillBag = firstBag.FindAll(IsRepeatable);
        var currentBag = new List<TalentCardConfigSO>(firstBag);
        var result = new List<TalentCardConfigSO>(count);

        while (result.Count < count)
        {
            if (currentBag.Count == 0)
            {
                if (refillBag.Count == 0)
                    break;
                currentBag.AddRange(refillBag);
            }

            var card = PickWeighted(currentBag);
            result.Add(card);
            currentBag.Remove(card);
        }
        return result;
    }

    /// <summary>按 Excel weight 权重从当前候选袋随机抽一张。</summary>
    private TalentCardConfigSO PickWeighted(List<TalentCardConfigSO> cards)
    {
        float total = 0f;
        var weights = new float[cards.Count];
        for (int i = 0; i < cards.Count; i++)
        {
            weights[i] = GetWeight(cards[i]);
            total += weights[i];
        }

        float roll = UnityEngine.Random.Range(0f, total);
        for (int i = 0; i < cards.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0f) return cards[i];
        }
        return cards[cards.Count - 1];
    }

    private float GetWeight(TalentCardConfigSO card)
    {
        if (_balance != null && _balance.TryGetTalent(card.talentId, out var b))
            return UnityEngine.Mathf.Max(1f, b.weight);
        return 1f;
    }

    private bool IsRepeatable(TalentCardConfigSO card)
    {
        if (_balance != null && _balance.TryGetTalent(card.talentId, out var b))
            return b.repeatable;
        return true;
    }

    /// <summary>按 talentId 查 Excel 数值；未命中返回 null。</summary>
    public TalentCardBalanceData GetBalance(TalentCardConfigSO card)
    {
        if (card == null) return null;
        return RequireBalance().TryGetTalent(card.talentId, out var b) ? b : null;
    }

    private TalentCardConfigSO FindResource(string talentId)
    {
        if (_pool == null || _pool.cards == null) return null;
        foreach (var card in _pool.cards)
            if (card != null && card.talentId == talentId) return card;
        return null;
    }
}
