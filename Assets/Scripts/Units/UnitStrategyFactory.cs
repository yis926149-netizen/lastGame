//****************************************
// 功能说明：按单位配置策略类型装配单位基础兵种策略（玩家/AI 共用）。
//   Settler → SettlerStrategy（移民，不战斗）
//   Ranged  → RangedStrategy（远程兵种）
//   Melee   → MeleeStrategy（近战兵种）
//
// 对象化改造：不再按 UnitID 魔法数（0/3/5/9）判断，改读 UnitConfigSO.strategyType。
//****************************************

public static class UnitStrategyFactory
{
    /// <summary>判断给定单位配置是否为远程兵种。</summary>
    public static bool IsRanged(UnitConfigSO config)
    {
        return config != null && config.strategyType == UnitStrategyType.Ranged;
    }

    /// <summary>按策略类型创建单位基础策略（裸策略，未套 buff 装饰器）。</summary>
    public static IUnitStrategy Create(UnitStrategyType type)
    {
        if (type == UnitStrategyType.Settler) return new SettlerStrategy();
        if (type == UnitStrategyType.Ranged) return new RangedStrategy();
        return new MeleeStrategy();
    }

    /// <summary>按单位配置创建基础策略；config 缺失时按近战兜底。</summary>
    public static IUnitStrategy Create(UnitConfigSO config)
    {
        if (config == null) return new MeleeStrategy();
        return Create(config.strategyType);
    }
}
