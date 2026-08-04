using UnityEngine;

//****************************************
// 【动态地图-阶段二/阶段五】地块变化补丁（HexCellPatch）
// 各系统不直接写 HexCellData 字段，统一经 MapMutationService.Apply 提交。
// 只填写需要修改的字段；未设置的字段保持原值。
// 归属变化（Owner）：阶段五起经 ILogisticsService 领域入口应用（§二十-12），
// 语义：>=0 设置为该阵营归属；<0 清除归属。未注入 ILogisticsService 时仍抛 NotSupported。
//****************************************

public sealed class HexCellPatch
{
    // ── 高度/地形 ─────────────────────────────
    public bool HasHeight;
    public float Height;

    public bool HasHexType;
    public Enums.HexType HexType;

    // ── 通行/探索 ─────────────────────────────
    public bool HasMovementCost;
    public float MovementCost;

    public bool HasIsUnexplorable;
    public bool IsUnexplorable;

    // ── 地貌/资源（清空）────────────────────────
    public bool ClearLandForm;
    public bool ClearResource;

    // ── 河流（清空）────────────────────────────
    public bool ClearRiver;

    // ── 归属（阶段五起支持：经 ILogisticsService 领域入口，§二十-12）──
    /// <summary>
    /// 目标归属阵营：&gt;=0 设置归属（LogisticsService.SetOwner）；&lt;0 清除归属（ClearOwner）。
    /// 不直接写 Player_City_Index——由 MapMutationService 在 Commit 时经领域入口应用。
    /// </summary>
    public int? Owner;

    public static HexCellPatch HeightPatch(float height) => new HexCellPatch { HasHeight = true, Height = height };
    public static HexCellPatch MovementCostPatch(float cost) => new HexCellPatch { HasMovementCost = true, MovementCost = cost };
    public static HexCellPatch UnexplorablePatch(bool unexplorable) => new HexCellPatch { HasIsUnexplorable = true, IsUnexplorable = unexplorable };
}
