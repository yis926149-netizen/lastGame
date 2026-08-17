using System.Collections.Generic;
using GameConfig;

//****************************************
//功能说明：普通卡解锁规则提供者（对象化 + Excel 数值化）。
//         卡池内容优先由 NormalCardPoolDatabaseSO（Excel 生成）决定；
//         通过 cardId（稳定字符串 ID）→ legacyId → 资源 SO 的链路解析出卡资源对象。
//         Excel 卡池为空时回退 Legacy 手工列表 NormalCardPoolSO（双轨迁移期）。
//****************************************
public interface ICardUnlockRuleProvider
{
    /// <summary>当前可抽取的普通卡列表（单位卡 + 建筑卡）。</summary>
    IReadOnlyList<NormalCardConfigSO> GetUnlockedCards();

    /// <summary>首张保底卡（单位卡或建筑卡均可），不再依赖手工拖引用。</summary>
    NormalCardConfigSO GetGuaranteedFirstCard();
}

public class CardUnlockRuleProvider : ICardUnlockRuleProvider
{
    private readonly NormalCardPoolSO _legacyPool;                // Legacy 手工卡池（过渡期兜底）
    private readonly NormalCardPoolDatabaseSO _pool;              // Excel 卡池（数值）
    private readonly UnitBalanceDatabaseSO _unitBalance;          // cardId → legacyId
    private readonly BuildingBalanceDatabaseSO _buildingBalance;  // cardId → legacyId
    private readonly IUnitDataProvider _unitDataProvider;         // legacyId → 资源对象
    private readonly IBuildingDataProvider _buildingDataProvider; // legacyId → 资源对象

    public CardUnlockRuleProvider(
        IUnitDataProvider unitDataProvider,
        IBuildingDataProvider buildingDataProvider,
        NormalCardPoolSO legacyPool = null,
        NormalCardPoolDatabaseSO pool = null,
        UnitBalanceDatabaseSO unitBalance = null,
        BuildingBalanceDatabaseSO buildingBalance = null)
    {
        _unitDataProvider = unitDataProvider;
        _buildingDataProvider = buildingDataProvider;
        _legacyPool = legacyPool;
        _pool = pool;
        _unitBalance = unitBalance;
        _buildingBalance = buildingBalance;
    }

    public IReadOnlyList<NormalCardConfigSO> GetUnlockedCards()
    {
        if (_pool != null && _pool.EnabledCards.Count > 0)
        {
            var result = new List<NormalCardConfigSO>();
            foreach (var entry in _pool.EnabledCards)
            {
                var config = Resolve(entry);
                if (config != null)
                    result.Add(config);
            }
            return result;
        }

        // 过渡期回退：Excel 卡池未生成时用 Legacy 手工列表
        return _legacyPool != null && _legacyPool.cards != null
            ? _legacyPool.cards
            : new List<NormalCardConfigSO>();
    }

    public NormalCardConfigSO GetGuaranteedFirstCard()
    {
        if (_pool != null && !string.IsNullOrEmpty(_pool.GuaranteedFirstCardId)
            && _pool.TryGetCard(_pool.GuaranteedFirstCardId, out var entry))
        {
            return Resolve(entry);
        }

        // 过渡期回退
        return _legacyPool != null ? _legacyPool.guaranteedFirstCard : null;
    }

    private NormalCardConfigSO Resolve(NormalCardPoolEntry entry)
    {
        if (entry.cardType == "Unit")
        {
            if (_unitBalance != null && _unitBalance.TryGetUnit(entry.cardId, out var ub)
                && _unitDataProvider.TryGetUnitConfig(ub.legacyId, out var unit))
            {
                return unit;
            }
        }
        else if (entry.cardType == "Building")
        {
            if (_buildingBalance != null && _buildingBalance.TryGetBuilding(entry.cardId, out var bb)
                && _buildingDataProvider.TryGetBuildingConfig(bb.legacyId, out var building))
            {
                return building;
            }
        }
        return null;
    }
}
