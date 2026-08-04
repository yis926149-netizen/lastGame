using System.Collections.Generic;
using UnityEngine;

//****************************************
// 【断供方案-阶段3】建筑易主迁移：逐格占领与区域吞并共用。
// 同步建筑归属真相源（BuildingBase.Player_City_Index）、tag/视觉、HP、索引字典；
// 公共建筑走 OnCaptured 全量路径（含外一环归属、奖励/收割结算，决策 11）。
// 领地字典不在此手工维护——由 LogisticsService.RecalculateAll 从地块归属重建（§4.1）。
//****************************************

public static class BuildingTransferService
{
    /// <summary>
    /// 将建筑整体易主给 newFaction。
    /// triggerRecalculate：逐格占领传 true（公共建筑 OnCaptured 内部重算，覆盖外一环归属变化）；
    /// 区域吞并传 false（AnnexationService 批量后统一一次 RecalculateAll）。
    /// </summary>
    public static void TransferBuilding(GameObject building, int newFaction, bool triggerRecalculate = true)
    {
        if (building == null || newFaction < 0) return;

        var buildingBase = building.GetComponent<BuildingBase>();
        if (buildingBase == null) return;

        // 0. 同步建筑归属真相源（被箭塔阵营判定/血量 buff/占领阻挡判定读取，BuildingBase.cs:28）
        buildingBase.Player_City_Index = new KeyValuePair<int, int>(newFaction, 0);

        // 公共建筑：全量易主（含 ExpandSphereOfInfluence 外一环、HarvestForGold、OnPublicBuildingCaptured）
        var publicBuilding = building.GetComponent<PublicBuildingBase>();
        if (publicBuilding != null)
        {
            publicBuilding.OnCaptured(newFaction, triggerRecalculate);
            buildingBase.NotifyVisualChanged();
            return;
        }

        // 普通建筑/城市：tag + 父节点 + 血条颜色
        var controller = building.GetComponent<BuildingController>();
        if (controller != null)
        {
            controller.ApplyTransferVisual(newFaction);
            controller.RemoveFromPlayerIndexes();
        }
        else
        {
            building.tag = newFaction == 0 ? "PlayerBuilding" : "EnemyBuilding";
        }

        // HP 回满（参考 BuildingController.CityDestroyed，BuildingController.cs:84-91）
        if (buildingBase.buildingData != null)
            buildingBase.buildingData.currentHp = buildingBase.buildingData.hp;
        buildingBase.SyncHealthBar();

        // 门控重刷：阵营已由步骤 0 同步，Retarget(null) 触发 Refresh
        var gate = building.GetComponent<BuildingSupplyGate>();
        gate?.Retarget(null);

        buildingBase.NotifyVisualChanged();
    }
}
