/// <summary>
/// 山脉格视觉可见性纯规则（阶段 6.1，决策 ⑪）。
/// 唯一入口 = IsPermanentlyVisible(cell)：仅"有效山格"返回 true；水淹、永久清除、
/// 无 ridge 数据或低于最小可见高度时返回 false。本函数直接委托
/// MountainGeometryBuilder.HasVisibleMountain —— 与几何贡献使用同一有效性口径，
/// 判定零漂移（阶段 6.9 诊断断言"山格规则不得写 IsExplored/owner"以此为唯一入口）。
///
/// 契约（决策 ⑪，阶段边界）：
/// 1. 输出只参与视觉可见性合成（FogAlpha 目标等），绝不调用 ExploreBy、
///    不写 IsExplored / 归属 / 探索费用，也不伪造 TemporaryVisibilityService lease。
/// 2. 结果与阵营无关：中立、玩家归属、AI 归属山格视觉均可见，数据层归属与探索位保持原值。
/// 3. 本规则不改变玩法资格：山格仍不可通行、不可部署、不可建造（见 MountainCellRule）。
/// </summary>
public static class MountainVisibilityRule
{
    /// <summary>
    /// 该格是否永久视觉可见（免雾但不算已探索）。
    /// 仅视觉合成使用；调用前后探索位、归属与玩法规则保持不变（纯函数，无副作用）。
    /// </summary>
    public static bool IsPermanentlyVisible(HexCellData cell)
    {
        return MountainGeometryBuilder.HasVisibleMountain(cell);
    }
}
