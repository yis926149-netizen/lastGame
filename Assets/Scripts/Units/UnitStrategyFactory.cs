//****************************************
// 功能说明：按 UnitID 装配单位基础兵种策略的工厂（玩家/AI 共用）。
//   0        → SettlerStrategy（移民，不战斗）
//   3/5/9    → RangedStrategy（远程兵种，与现有 isRangedUnit 判定一致）
//   其他      → MeleeStrategy（近战兵种）
//
// 【批次 A】提供统一装配入口，供 CardPresenter / AIEntityFactory 挂载 Brain 时调用。
//****************************************

public static class UnitStrategyFactory
{
    /// <summary>判断给定 UnitID 是否为远程兵种。</summary>
    public static bool IsRanged(int unitID)
    {
        return unitID == 3 || unitID == 5 || unitID == 9;
    }

    /// <summary>按 UnitID 创建基础兵种策略（裸策略，未套 buff 装饰器）。</summary>
    public static IUnitStrategy Create(int unitID)
    {
        if (unitID == 0) return new SettlerStrategy();
        if (IsRanged(unitID)) return new RangedStrategy();
        return new MeleeStrategy();
    }
}
