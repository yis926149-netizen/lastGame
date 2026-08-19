using System;
using System.Collections.Generic;
using UnityEngine;

//****************************************
// 【竞技场-阶段二】中央宝箱（CentralChest : PublicBuildingBase）
// 差异项（vs 公共建筑，玩法文档 §3.3）全部通过参数/覆写消解：
//   - 占格 1 格：Initialize 传空 subHexDirections
//   - 单段 500HP：captureHp=defenseHp=500（不走易主）
//   - 销毁结局：覆写 OnDeath 直接销毁（触发海克斯 + 通知 ArenaEventManager），不走 OnCaptured
//   - 始终中立：PlayerIndex=-1，不扩势力范围
//   - 直接出现：MarkRevealedWithoutExploration（不写探索位，迷雾由 Arena VisibilityLease 覆盖）
//   - 索敌：tag=NeutralBuilding（双方均可攻击），箭塔忽略（ArrowTowerShooter 只索敌单位）
//****************************************

public class CentralChest : PublicBuildingBase
{
    // 【Excel 数值化】宝箱 HP 迁移至 CoreGameplayConfigProvider（保持静态访问兼容旧调用）。
    public static float ChestHp => CoreGameplayConfigProvider.CentralChestHp;

    /// <summary>宝箱被摧毁事件（通知 ArenaEventManager 进入 Destroyed 恢复流程）。</summary>
    public event Action<CentralChest> ChestDestroyed;

    /// <summary>
    /// 以宝箱参数初始化：1 格、始终中立、单段 HP、生成即激活（Revealed，不写探索位）。
    /// 必须在实例化 + 注入 + buildingData 赋值后立即调用。
    /// </summary>
    public void InitializeAsChest(HexCellData rootHex, IMapDataService mapDataService)
    {
        base.Initialize(rootHex, new Enums.HexDirection[0], -1, ChestHp, ChestHp, mapDataService);

        // 生成即激活：跳过发现机制（不触发 Reveal 的探索位写入）
        MarkRevealedWithoutExploration();
        // 保持中立视觉与索敌语义（双方可攻击）
        UpdateVisual(-1);
    }

    /// <summary>宝箱不参与发现 Tick（生成即激活）。</summary>
    public override void TickDiscovery()
    {
    }

    /// <summary>
    /// 覆写视觉：宝箱始终中立（NeutralBuilding，双方可攻击）、金色血条。
    /// 不调用基类 UpdateVisual（基类会把 -1 判为 EnemyBuilding，导致 AI 无法攻击）。
    /// </summary>
    protected override void UpdateVisual(int ownerPlayerIndex)
    {
        gameObject.tag = "NeutralBuilding";
        UITool.TrySetSliderFillColor(uiHealthBar, new Color(1f, 0.85f, 0.2f));
    }

    /// <summary>
    /// 宝箱死亡 = 直接销毁（不复活/不易主）：触发海克斯（RaiseCapturedEvent）+
    /// 通知 ArenaEventManager 执行竞技场恢复流程。
    /// 清格（movementCost=1/BulidingType=NoBuilding/publicBuildingRoot=null）由基类 OnDestroy 完成。
    /// </summary>
    public override void OnDeath()
    {
        if (_isDestroyed) return;
        _isDestroyed = true;

        if (Attacker != null)
        {
            var attackerController = Attacker.GetComponent<UnitMovementController>();
            if (attackerController != null)
            {
                Debug.Log($"[CentralChest] 被阵营 {attackerController.PlayerIndex} 摧毁，触发海克斯选择。");
                RaiseCapturedEvent(attackerController.PlayerIndex);
            }
        }

        ChestDestroyed?.Invoke(this);
    }
}
