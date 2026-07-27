using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

//****************************************
// 【公共建筑系统-决策#16】建筑基类
// 职责：血量管理、血条、阵营归属、受击入口、伤害公式（可覆写）、死亡标记
// 死亡触发改为 GameLoop 驱动（公共建筑）或保留 Update() 轮询（普通建筑）
//****************************************

public abstract class BuildingBase : MonoBehaviour
{
    // ── 依赖注入 ──────────────────────────────────────
    [Inject] protected IMapDataService _mapDataService;
    [Inject] protected MapVisualEventSO _mapVisualEvent;
    [Inject] protected AudioManager _audioManager;
    [Inject] protected EnemyModelManager _enemyModelManager;
    [Inject] protected PlayerModelManager _playerModelManager;

    // ── 公共字段 ──────────────────────────────────────
    [HideInInspector] public BuildingData buildingData;
    [HideInInspector] public Slider uiHealthBar;
    public Enums.BulidingType bulidingType;

    /// <summary>建筑归属（PlayerIndex, CityIndex）。(-1,-1)=无主，(0,n)=玩家，(k>=1,n)=AI/公共建筑</summary>
    public KeyValuePair<int, int> Player_City_Index = new KeyValuePair<int, int>(-1, -1);

    /// <summary>当前攻击该建筑的单位（记录最后一击用于易主/奖励分配）</summary>
    public GameObject Attacker;

    // ── 死亡标记 ──────────────────────────────────────
    protected bool _isDestroyed;
    public bool IsDestroyed => _isDestroyed;

    // ── 受击入口（统一调用点） ────────────────────────
    /// <summary>
    /// 建筑被攻击的统一入口。子类可覆写以添加自定义逻辑（如多格转发）。
    /// </summary>
    public virtual void BuildingAttacked(GameObject enemyAttacker)
    {
        if (buildingData == null || enemyAttacker == null)
        {
            Debug.LogWarning($"[{GetType().Name}] BuildingAttacked skipped: missing buildingData or attacker.");
            return;
        }

        var attackerController = enemyAttacker.GetComponent<UnitMovementController>();
        if (attackerController == null || attackerController.characterData == null)
        {
            Debug.LogWarning($"[{GetType().Name}] BuildingAttacked skipped: attacker unit data is missing.");
            return;
        }

        // 伤害计算与血量扣减
        float damage = ComputeDamage(attackerController.characterData, buildingData);
        buildingData.currentHp -= damage;

        // 同步血条
        SyncHealthBar();

        // 记录攻击者（用于易主/奖励分配）
        Attacker = enemyAttacker;
    }

    // ── 伤害公式（可覆写） ────────────────────────────
    /// <summary>
    /// 计算攻击者对该建筑造成的伤害。默认实现复用 BuildingController 的逻辑。
    /// 子类可覆写以实现不同的伤害公式。
    /// </summary>
    protected virtual float ComputeDamage(CharacterData attacker, BuildingData target)
    {
        HexCellData targetHex = _mapDataService.GetCellByWorldPosition(transform.position);
        HexCellData attackerHex = _mapDataService.GetCellByWorldPosition(attacker.model.transform.position);

        // 防御雕像：周围一环城市无敌
        float defenseModifier = 1f;
        if (targetHex != null && bulidingType == Enums.BulidingType.City)
        {
            for (int i = 0; i < 6; i++)
            {
                HexCellData neighbor = _mapDataService.GetNeighbor(targetHex, (Enums.HexDirection)i);
                if (neighbor != null && neighbor.BulidingTypeOnHex_Building.Key == Enums.BulidingType.DefenseStatue)
                {
                    defenseModifier = 0f; // 无敌
                    break;
                }
            }
        }

        // 攻击雕像：周围一环 +0.7 攻击（可叠加）
        float attackStatueGain = 0f;
        if (attackerHex != null)
        {
            for (int i = 0; i < 6; i++)
            {
                HexCellData neighbor = _mapDataService.GetNeighbor(attackerHex, (Enums.HexDirection)i);
                if (neighbor != null && neighbor.BulidingTypeOnHex_Building.Key == Enums.BulidingType.AttackStatue)
                {
                    attackStatueGain += 0.7f;
                }
            }
        }

        float attackPower = attacker.currentAttackValue;
        float attackGain = 1f + attacker.Resource_Animals + attackStatueGain;

        // 资源增益一次性清空
        if (attackGain > 1f)
        {
            attacker.Resource_Animals = 0;
        }

        return Mathf.Max(0, attackPower * attackGain * defenseModifier);
    }

    // ── 血条同步 ──────────────────────────────────────
    protected void SyncHealthBar()
    {
        // 兜底：运行时可能未正确缓存血条
        if (uiHealthBar == null)
        {
            uiHealthBar = GetComponentInChildren<Slider>();
        }

        if (uiHealthBar != null && buildingData != null && buildingData.hp > 0)
        {
            uiHealthBar.value = buildingData.currentHp / buildingData.hp;
        }
    }

    // ── 死亡检测（由 GameLoop 或 Update 调用） ────────
    /// <summary>
    /// 检测是否已死亡（血量≤0）。由子类决定何时触发（GameLoop.Tick 或 Update）。
    /// 返回 true 表示应触发死亡流程。
    /// </summary>
    public virtual bool CheckDeath()
    {
        return !_isDestroyed && uiHealthBar != null && uiHealthBar.value <= 0;
    }

    // ── 死亡处理（抽象，由子类实现） ──────────────────
    /// <summary>
    /// 建筑死亡时的具体行为（城市易主 / 普通建筑销毁 / 公共建筑易主）。
    /// 子类必须实现。
    /// </summary>
    public abstract void OnDeath();
}
