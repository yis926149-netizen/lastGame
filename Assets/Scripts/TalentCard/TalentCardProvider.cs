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
