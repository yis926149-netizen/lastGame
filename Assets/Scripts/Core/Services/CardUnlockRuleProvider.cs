using System.Collections.Generic;

//****************************************
//功能说明：普通卡解锁规则提供者（对象化）。
//         卡池内容完全由 NormalCardPoolSO 决定，硬编码 ID 数组与科技/文化参数已移除。
//****************************************
public interface ICardUnlockRuleProvider
{
    /// <summary>当前可抽取的普通卡列表（单位卡 + 建筑卡）。</summary>
    IReadOnlyList<NormalCardConfigSO> GetUnlockedCards();

    /// <summary>首张保底卡（移民卡），替代硬编码 return 0。</summary>
    UnitConfigSO GetGuaranteedFirstCard();
}

public class CardUnlockRuleProvider : ICardUnlockRuleProvider
{
    private readonly NormalCardPoolSO _pool;

    public CardUnlockRuleProvider(NormalCardPoolSO pool)
    {
        _pool = pool;
    }

    public IReadOnlyList<NormalCardConfigSO> GetUnlockedCards()
    {
        if (_pool == null || _pool.cards == null) return new List<NormalCardConfigSO>();
        return _pool.cards;
    }

    public UnitConfigSO GetGuaranteedFirstCard() => _pool != null ? _pool.guaranteedFirstCard : null;
}
