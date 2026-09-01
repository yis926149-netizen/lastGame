using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Zenject;
using UIToolkitDemo;

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
    [Inject(Optional = true)] protected IFactionBuffService _factionBuff;
    [Inject(Optional = true)] protected ILogisticsService _logisticsService;
    // 【动态地图-阶段二】统一可见性解析（永久 || 临时 VisibilityLease）：宝箱血条随 Arena lease 显示
    [Inject(Optional = true)] protected IMapVisibilityResolver _visibilityResolver;
    // 【伤害飘字】表现层事件总线：可选注入，缺失时静默跳过（数据层不依赖 UI）
    [Inject(Optional = true)] protected DamageEventBroker _damageEventBroker;

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

    // ── 初始化 ────────────────────────────────────────
    protected virtual void Start()
    {
        ApplyFactionBuildingHpBuff();
        EnsureSupplyGate();
        EnsureHealthBarVisibilitySync();
    }

    // 【断供方案-阶段5/决策4】血条（整个建筑 Canvas）按玩家视角可见性隐藏：
    // 血条是 Canvas 的 child0（SpawnUIWiring.cs:52-56），兵营生产进度条是血条兄弟
    //（BarracksSpawner.cs:193）——隐藏粒度是整个 Canvas。
    private void EnsureHealthBarVisibilitySync()
    {
        if (_logisticsService == null) return;
        _logisticsService.LogisticsChanged += RefreshHealthBarVisibility;
        RefreshHealthBarVisibility();
    }

    private void RefreshHealthBarVisibility()
    {
        if (_mapDataService == null) return;

        HexCellData cell = _mapDataService.GetCellByWorldPosition(transform.position);
        if (cell == null) return;

        // 【动态地图-阶段二】统一查询 IMapVisibilityResolver：永久可见性 || 临时 VisibilityLease
        bool visible;
        if (_visibilityResolver != null)
        {
            visible = _visibilityResolver.IsVisibleToFaction(cell, 0);
        }
        else if (_logisticsService != null)
        {
            visible = _logisticsService.IsVisibleToFaction(cell, 0);
        }
        else
        {
            visible = cell.IsExplored;
        }

        if (_buildingCanvas == null)
            _buildingCanvas = GetComponentInChildren<Canvas>();
        if (_buildingCanvas != null)
            _buildingCanvas.gameObject.SetActive(visible);
    }

    private Canvas _buildingCanvas;

    protected virtual void OnDestroy()
    {
        if (_logisticsService != null)
            _logisticsService.LogisticsChanged -= RefreshHealthBarVisibility;
    }

    // 【断供方案-阶段2】统一挂载失能门控；阵营/格子首帧解析，易主由迁移函数 Retarget。
    private void EnsureSupplyGate()
    {
        if (_logisticsService == null) return;
        if (GetComponent<BuildingSupplyGate>() != null) return;

        BuildingSupplyGate gate = gameObject.AddComponent<BuildingSupplyGate>();
        gate.Initialize(_mapDataService, _logisticsService);
    }

    /// <summary>当前建筑是否功能正常（断供即失能）。供箭塔/兵营等行为查询。</summary>
    public bool IsFunctional
    {
        get
        {
            BuildingSupplyGate gate = GetComponent<BuildingSupplyGate>();
            return gate != null && gate.IsFunctional;
        }
    }

    /// <summary>通知地图视觉刷新（雾化遮罩注册/血条可见性等）。</summary>
    public void NotifyVisualChanged() => _mapVisualEvent?.Raise();

    private void ApplyFactionBuildingHpBuff()
    {
        if (_factionBuff == null || buildingData == null) return;

        int factionId = Player_City_Index.Key;
        if (factionId < 0) return;

        float mult = _factionBuff.GetStatMultiplier(factionId, "buildingHp");
        if (Mathf.Abs(mult - 1f) < 0.001f) return;

        buildingData.hp *= mult;
        buildingData.currentHp = buildingData.hp;
        SyncHealthBar();
    }

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

        // 【伤害飘字】发布表现事件（无 UI 依赖；锚点优先血条位置）
        if (damage > 0f && _damageEventBroker != null)
        {
            Vector3 anchor = uiHealthBar != null
                ? uiHealthBar.transform.position
                : transform.position;
            _damageEventBroker.RaiseDamage(anchor, damage, targetFaction: Player_City_Index.Key);

            // 【受击反馈】发布受击事件：参数为建筑自身 gameObject
            _damageEventBroker.RaiseHit(gameObject);
        }

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
                    attackStatueGain += BattleFormulaRule.AttackStatueBonus;
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
    public void SyncHealthBar()
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
