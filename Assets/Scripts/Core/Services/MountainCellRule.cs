/// <summary>
/// 山脉地块玩法规则（纯函数，决策 ①/⑦/㉕）。
/// 山脉占用标记 = landForm.mountainForm；有效山体判定（决策 ①/⑦）：
/// isMountain &amp;&amp; !mountainCleared &amp;&amp; !IsWater（水淹时移除山体贡献，保留基础海床）。
/// 统一格级资格入口：寻路看 movementCost，部署/建造走 CanSpawnUnitOnCell/CanBuildOnCell，
/// 各调用方不得自行只依赖 movementCost（源码审计修正 A-4）。
/// </summary>
public static class MountainCellRule
{
    /// <summary>该地貌 SO 是否为山脉地貌（占用标记）。</summary>
    public static bool IsMountainForm(MapLandFormSO form)
    {
        return form != null && form.mountainForm;
    }

    /// <summary>该格是否标记为山脉地块（含已被水淹/已清除的山格，仅看占用标记）。</summary>
    public static bool IsMountainCell(HexCellData cell)
    {
        return cell != null && IsMountainForm(cell.landForm);
    }

    /// <summary>
    /// 有效山体判定（决策 ①/⑦）：山脉标记 && 未被永久清除 && 非水域。
    /// 用于山体几何贡献、不可通行规则派生；水淹时返回 false（山体贡献移除，基础海床保留）。
    /// </summary>
    public static bool IsEffectiveMountainCell(HexCellData cell)
    {
        return IsMountainCell(cell) && !cell.mountainCleared && !WaterLevelConfig.IsWater(cell);
    }

    /// <summary>
    /// 派生格级移动力：水域或有效山体 = MaxValue（不可通行），否则 1。
    /// 水→陆/陆→水/清除山体等状态变化后必须重新调用本函数（MapMutationService.ApplyPatch）。
    /// </summary>
    public static float DeriveMovementCost(HexCellData cell)
    {
        if (cell == null) return 1f;
        if (WaterLevelConfig.IsWater(cell)) return float.MaxValue;
        if (IsMountainCell(cell) && !cell.mountainCleared) return float.MaxValue;
        return 1f;
    }

    /// <summary>统一通行资格：movementCost &lt; MaxValue（含寻路、移动）。</summary>
    public static bool CanEnterCell(HexCellData cell)
    {
        return cell != null && DeriveMovementCost(cell) < float.MaxValue;
    }

    /// <summary>统一部署资格：有效山体/水域不可部署单位（决策 ①）。</summary>
    public static bool CanSpawnUnitOnCell(HexCellData cell)
    {
        return CanEnterCell(cell);
    }

    /// <summary>统一建造资格：有效山体（blockBuildingSpawn=true 已覆盖）与水域不可建造。</summary>
    public static bool CanBuildOnCell(HexCellData cell)
    {
        if (cell == null) return false;
        if (WaterLevelConfig.IsWater(cell)) return false;
        if (IsEffectiveMountainCell(cell)) return false;
        return cell.landForm == null || !LandFormEffectRule.GetBlockBuildingSpawn(cell.landForm);
    }
}
