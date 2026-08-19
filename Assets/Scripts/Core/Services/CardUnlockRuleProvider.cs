using System.Collections.Generic;
using GameConfig;

//****************************************
//功能说明：普通卡解锁规则提供者（对象化 + Excel 数值化，阶段6 唯一主源）。
//         卡池内容仅由 NormalCardPoolDatabaseSO（Excel 生成）决定；
//         通过 cardId（稳定字符串 ID）→ legacyId → 资源 SO 的链路解析出卡资源对象。
//         Excel 未加载时抛异常，暴露配置缺失。
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
    private readonly NormalCardPoolDatabaseSO _pool;              // Excel 卡池（数值）
    private readonly UnitBalanceDatabaseSO _unitBalance;          // cardId → legacyId
    private readonly BuildingBalanceDatabaseSO _buildingBalance;  // cardId → legacyId
    private readonly IUnitDataProvider _unitDataProvider;         // legacyId → 资源对象
    private readonly IBuildingDataProvider _buildingDataProvider; // legacyId → 资源对象

    public CardUnlockRuleProvider(
        IUnitDataProvider unitDataProvider,
        IBuildingDataProvider buildingDataProvider,
        NormalCardPoolDatabaseSO pool = null,
        UnitBalanceDatabaseSO unitBalance = null,
        BuildingBalanceDatabaseSO buildingBalance = null)
    {
        _unitDataProvider = unitDataProvider;
        _buildingDataProvider = buildingDataProvider;
        _pool = pool;
        _unitBalance = unitBalance;
        _buildingBalance = buildingBalance;
    }

    private NormalCardPoolDatabaseSO RequirePool()
    {
        if (_pool == null)
            throw new System.InvalidOperationException(
                "[CardUnlock] Excel 普通卡池未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 NormalCardPoolDatabaseSO。");
        return _pool;
    }

    public IReadOnlyList<NormalCardConfigSO> GetUnlockedCards()
    {
        var result = new List<NormalCardConfigSO>();
        foreach (var entry in RequirePool().EnabledCards)
        {
            var config = Resolve(entry);
            if (config != null)
                result.Add(config);
        }
        return result;
    }

    public NormalCardConfigSO GetGuaranteedFirstCard()
    {
        var pool = RequirePool();
        if (string.IsNullOrEmpty(pool.GuaranteedFirstCardId))
            return null;

        if (!pool.TryGetCard(pool.GuaranteedFirstCardId, out var entry))
            throw new System.InvalidOperationException(
                $"[CardUnlock] 保底卡 {pool.GuaranteedFirstCardId} 未在 Excel 普通卡池命中。");
        return Resolve(entry);
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
