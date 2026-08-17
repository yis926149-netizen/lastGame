using System.Collections.Generic;
using GameConfig;

//****************************************
//功能说明：天赋卡提供者（对象化 + Excel 数值化）。
//         启用卡列表优先由 TalentCardBalanceDatabaseSO（Excel 生成）决定，
//         通过 talentId（稳定字符串 ID）→ 资源 SO 解析出卡资源对象（图标等人工引用）。
//         Excel 天赋库为空时回退 Legacy 手工列表 TalentCardPoolSO（双轨迁移期）。
//****************************************
public class TalentCardProvider
{
    private readonly TalentCardPoolSO _pool;                    // Legacy 资源池（过渡期兜底）
    private readonly TalentCardBalanceDatabaseSO _balance;      // Excel 数值（只读）

    public TalentCardProvider(TalentCardPoolSO pool, TalentCardBalanceDatabaseSO balance = null)
    {
        _pool = pool;
        _balance = balance;
    }

    /// <summary>启用天赋卡列表（资源对象），Excel 优先，缺失回退 Legacy。</summary>
    public IReadOnlyList<TalentCardConfigSO> GetEnabledCards()
    {
        if (_balance != null && _balance.EnabledTalents.Count > 0)
        {
            var result = new List<TalentCardConfigSO>();
            foreach (var b in _balance.EnabledTalents)
            {
                var config = FindResource(b.talentId);
                if (config != null) result.Add(config);
            }
            return result;
        }

        return _pool != null && _pool.cards != null ? _pool.cards : new List<TalentCardConfigSO>();
    }

    /// <summary>从启用天赋卡中随机抽 count 张（Fisher-Yates 洗牌，无重复）。</summary>
    public List<TalentCardConfigSO> DrawRandom(int count)
    {
        var available = new List<TalentCardConfigSO>(GetEnabledCards());
        available.RemoveAll(c => c == null);
        if (available.Count == 0)
            return new List<TalentCardConfigSO>();

        int drawCount = System.Math.Min(count, available.Count);
        for (int i = 0; i < drawCount; i++)
        {
            int j = i + UnityEngine.Random.Range(0, available.Count - i);
            var temp = available[i];
            available[i] = available[j];
            available[j] = temp;
        }

        return available.GetRange(0, drawCount);
    }

    /// <summary>按 talentId 查 Excel 数值；未命中返回 null。</summary>
    public TalentCardBalanceData GetBalance(TalentCardConfigSO card)
    {
        if (card == null || _balance == null) return null;
        return _balance.TryGetTalent(card.talentId, out var b) ? b : null;
    }

    private TalentCardConfigSO FindResource(string talentId)
    {
        if (_pool == null || _pool.cards == null) return null;
        foreach (var card in _pool.cards)
            if (card != null && card.talentId == talentId) return card;
        return null;
    }
}
