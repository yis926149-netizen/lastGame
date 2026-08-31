using UnityEngine;

//****************************************
// 功能说明：战斗逻辑结算器（纯数据层，与表现分离）。
//   瞬间结算一次攻击的伤害/死亡，不含任何动画或延时。
//   伤害公式搬移自 UnitMovementController.AttackDataComputation。
//   动画表现由 UnitMovementController.PlayAttackAnim 单独负责。
//
// 设计要点（见 5.5）：
//   - 瞬间结算：调用即完成扣血，动画只是回放。
//   - 无反击：单向施加伤害，防守方不回打。
//   - 可被 buff 装饰器改写（吸血、溅射等，后续检查点）。
//
// 【批次 D】实现瞬间伤害结算，接管战斗核心。
//****************************************

public class CombatResolver
{
    private readonly IMapDataService _mapDataService;
    private readonly IFactionBuffService _factionBuff;
    private readonly DamageEventBroker _damageEventBroker;

    public CombatResolver(IMapDataService mapDataService, IFactionBuffService factionBuff, DamageEventBroker damageEventBroker)
    {
        _mapDataService = mapDataService;
        _factionBuff = factionBuff;
        _damageEventBroker = damageEventBroker;
    }

    /// <summary>
    /// 结算一次攻击：attacker 对 target 造成一次瞬间伤害。
    /// </summary>
    public void Resolve(CharacterData attacker, CharacterData target)
    {
        if (attacker?.model == null || target?.model == null) return;
        // 攻击者已死亡（HP≤0 或死亡流程已触发）时不允许再结算伤害
        if (attacker.currentHp <= 0) return;
        if (attacker.unitMovementController != null &&
            attacker.unitMovementController.IsDeathScheduled) return;
        if (target.currentHp <= 0) return;

        float damage = ComputeUnitDamage(attacker, target);
        target.currentHp -= damage;

        // 【伤害飘字】发布表现事件：只发布有效伤害，飘字由 DamageFloatTextRenderer 订阅（数据层不依赖 UI）
        if (damage > 0f)
            _damageEventBroker.RaiseDamage(GetDamageAnchor(target), damage,
                targetFaction: target.unitMovementController != null ? target.unitMovementController.PlayerIndex : -1);

        // 同步血条
        if (target.healthBar != null && target.unitData != null && target.unitData.hp > 0)
        {
            target.healthBar.value = Mathf.Clamp01(target.currentHp / target.unitData.hp);
        }
        // 死亡检查由 UnitMovementController.UnitDeath（Update 中）处理，currentHp <= 0 即触发。
    }

    /// <summary>伤害飘字锚点：优先血条位置（预制体头顶），无血条时用模型位置。</summary>
    private static Vector3 GetDamageAnchor(CharacterData target)
    {
        if (target.healthBar != null)
            return target.healthBar.transform.position;
        return target.model.transform.position;
    }

    /// <summary>
    /// 结算一次对建筑的攻击。委托 BuildingController.BuildingAttacked（沿用建筑侧公式与死亡处理）。
    /// </summary>
    public void ResolveBuilding(CharacterData attacker, BuildingData target)
    {
        if (attacker?.model == null || target?.controller == null) return;
        if (target.currentHp <= 0) return;

        target.controller.BuildingAttacked(attacker.model);
    }

    /// <summary>统一结算普通建筑与公共建筑攻击。</summary>
    public void ResolveBuilding(CharacterData attacker, BuildingBase target)
    {
        if (attacker?.model == null || target?.buildingData == null) return;
        if (target.buildingData.currentHp <= 0) return;

        target.BuildingAttacked(attacker.model);
    }

    // ── 多格攻击转发（决策#37/#19）────────────────────
    /// <summary>
    /// 从目标 hex 解析出建筑目标（普通建筑或公共建筑根格）。
    /// 公共建筑子格攻击会转发到根格，实现多格共享一份 HP。
    /// </summary>
    public BuildingBase GetBuildingTarget(HexCellData targetHex)
    {
        if (targetHex == null) return null;

        // 优先检查是否为公共建筑（多格建筑，根格或子格）
        if (targetHex.publicBuildingRoot != null)
        {
            return targetHex.publicBuildingRoot;
        }

        // 普通建筑（城市、雕像、祭坛等）
        if (targetHex.BulidingTypeOnHex_Building.Key != Enums.BulidingType.NoBuilding
            && targetHex.BulidingTypeOnHex_Building.Value != null)
        {
            return targetHex.BulidingTypeOnHex_Building.Value.GetComponent<BuildingBase>();
        }

        return null;
    }

    // ── 单位伤害公式（搬移自 UnitMovementController.AttackDataComputation）─────────
    private float ComputeUnitDamage(CharacterData attacker, CharacterData theAttacked)
    {
        HexCellData h = _mapDataService.GetCellByWorldPosition(theAttacked.model.transform.position);
        HexCellData attackerHex = _mapDataService.GetCellByWorldPosition(attacker.model.transform.position);
        if (h == null || attackerHex == null) return 0;

        // 【地图地貌配置化】地貌防御加成按被攻击者所在格配置查询（原 BigBones 缓存字段已删除）
        float landFormDefenseBonus = LandFormEffectRule.GetDefenseBonus(h.landForm);

        if (!h.hasRiver)
            theAttacked.LandFormType_River = 0;
        else
            theAttacked.LandFormType_River = BattleFormulaRule.RiverDefensePenalty;

        float AttackStatueGain = 0;
        for (int i = 0; i < 6; i++)
        {
            HexCellData neighborHex = _mapDataService.GetNeighbor(attackerHex, (Enums.HexDirection)i);
            if (neighborHex != null && neighborHex.BulidingTypeOnHex_Building.Key == Enums.BulidingType.AttackStatue)
                AttackStatueGain += BattleFormulaRule.AttackStatueBonus;
        }

        float TerrainElevation = 0;
        float heightDiff = attackerHex.Height - h.Height;
        if (heightDiff > 0.01f)
            TerrainElevation = BattleFormulaRule.HighGroundAttackBonus;
        else if (heightDiff < -0.01f)
            TerrainElevation = -BattleFormulaRule.HighGroundAttackBonus;

        int attackerFaction = attacker.unitMovementController?.PlayerIndex ?? -1;
        float factionAttackMultiplier = _factionBuff.GetStatMultiplier(attackerFaction, "damage");
        float AttackPower = attacker.currentAttackValue * factionAttackMultiplier;
        float AttackGain = 1 + attacker.Resource_Animals + AttackStatueGain + TerrainElevation;

        int defenderFaction = theAttacked.unitMovementController?.PlayerIndex ?? -1;
        float factionDefenseMultiplier = _factionBuff.GetStatMultiplier(defenderFaction, "defense");
        float Defense = theAttacked.Defense * factionDefenseMultiplier;
        float DefenseGain = 1 + theAttacked.Resource_Minerals + landFormDefenseBonus + theAttacked.LandFormType_River;

        return Mathf.Max(0, AttackPower * AttackGain - Defense * DefenseGain);
    }
}
