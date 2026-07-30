using UnityEngine;

//****************************************
// 功能说明：单位行为基类（MonoBehaviour，挂在每个单位 GameObject 上）。
//
// 【批次 C】新增回血计时器（农田地貌/祭坛建筑），替代 SettlementPhase 每回合回血。
//   计时器在 Update 中累加（非暂停时），到达 HealInterval 后触发回血，
//   与 LandFormConfigSO 默认值（HealRatio=0.1f, HealInterval=5f）对齐。
//****************************************

public abstract class UnitBrainBase : MonoBehaviour
{
    // ── 关联数据 ──────────────────────────────────────────
    public CharacterData Owner { get; set; }

    // ── 共享依赖 ──────────────────────────────────────────
    public IMapDataService MapData { get; protected set; }
    public UnitMovementSystem Movement { get; protected set; }
    public CombatResolver Combat { get; protected set; }
    private PublicBuildingMarkerManager _publicBuildingMarkerManager;

    // ── 当前策略 ──────────────────────────────────────────
    protected IUnitStrategy activeStrategy;

    // ── 暂停标志 ──────────────────────────────────────────
    public bool IsPaused { get; set; }

    // ── 忙碌标志 ──────────────────────────────────────────
    public bool IsBusy => Owner?.unitMovementController?.IsBusy ?? false;

    // ── 回血计时器（批次 C）──────────────────────────────
    // 与 LandFormConfigSO 默认值对齐；ScriptableObject 无法直接注入 MonoBehaviour，使用常量。
    private const float HealInterval = 5f;          // 秒
    private const float LandHealRatio = 0.1f;       // 农田：占最大 HP 比例
    private float _landHealTimer = 0f;
    private float _buildingHealTimer = 0f;

    // ── 攻速冷却（批次 D）────────────────────────────────
    // 策略的 DoCombat 每次结算后调用 MarkAttacked()，冷却结束前 CanAttack 在骨架层被屏蔽。
    private float _attackCooldownRemaining = 0f;

    /// <summary>
    /// 当前是否处于攻速冷却中。
    /// 策略类的 DoCombat 结算完成后应调用 MarkAttacked() 开始冷却。
    /// </summary>
    public bool IsAttackOnCooldown => _attackCooldownRemaining > 0f;

    /// <summary>记录一次攻击，启动攻速冷却计时。</summary>
    public void MarkAttacked()
    {
        float interval = Owner?.unitData?.AttackInterval ?? 1.5f;
        _attackCooldownRemaining = interval;
    }

    // ── 回血计时器 + 攻速冷却（批次 C/D）────────────────
    protected virtual void Update()
    {
        if (IsPaused || Owner == null) return;

        float dt = Time.deltaTime;

        // 攻速冷却倒计时
        if (_attackCooldownRemaining > 0f)
            _attackCooldownRemaining -= dt;

        TickLandHeal(dt);
        TickBuildingHeal(dt);
    }

    private HexCellData GetCurrentCell()
    {
        if (MapData == null || Owner?.unitMovementController == null) return null;
        return MapData.GetCell(Owner.unitMovementController.CurrentHexCoordinate);
    }

    private void TickLandHeal(float dt)
    {
        HexCellData h = GetCurrentCell();
        if (h == null || h.landFormType != Enums.LandFormType.FromLand)
        {
            _landHealTimer = 0f;  // 离开农田时重置
            return;
        }

        _landHealTimer += dt;
        if (_landHealTimer < HealInterval) return;

        _landHealTimer = 0f;
        if (Owner.unitData != null)
            Owner.Heal(LandHealRatio * Owner.unitData.hp);
    }

    private void TickBuildingHeal(float dt)
    {
        HexCellData h = GetCurrentCell();
        if (h == null ||
            h.BulidingTypeOnHex_Building.Key != Enums.BulidingType.Altar ||
            h.BulidingTypeOnHex_Building.Value == null)
        {
            _buildingHealTimer = 0f;  // 离开祭坛时重置
            return;
        }

        var ctrl = h.BulidingTypeOnHex_Building.Value.GetComponent<BuildingController>();
        if (ctrl?.buildingData == null) return;

        float interval = ctrl.buildingData.HealInterval > 0 ? ctrl.buildingData.HealInterval : HealInterval;
        _buildingHealTimer += dt;
        if (_buildingHealTimer < interval) return;

        _buildingHealTimer = 0f;
        Owner.Heal(ctrl.buildingData.AltarValue * Owner.unitData.hp);
    }

    // ── 决策节流 ──────────────────────────────────────────
    // 节流仅在“上一次寻路失败（被困/无可达目标）”后生效，防止每帧重复空跑 Dijkstra。
    // 正常沿缓存路径逐格前进、以及路径耗尽后的立即重算都不节流，实现平滑移动。
    private int _idleSearchCounter;
    private const int SearchInterval = 20;
    // 上一次 ChooseNextPath 是否失败（无路可走）。只有在此状态下才对重算节流。
    private bool _pathfindFailed;

    // ── 路径缓存（方案 C：治本）─────────────────────────────
    // ChooseNextPath 一次性返回完整路径，缓存后逐格消费。
    // 每格抵达直接取缓存下一步，免去每步重新寻路 + 节流等待造成的卡顿。
    private System.Collections.Generic.List<Vector3> _cachedPath;
    private int _cachedPathIndex;

    /// <summary>移动完成后调用，重置节流计数器以确保立即搜索目标。</summary>
    public void ResetSearchThrottle()
    {
        _idleSearchCounter = 0;
        _pathfindFailed = false;
    }

    /// <summary>清空缓存路径（目标失效/被击杀/易主时调用，强制下次重新寻路）。</summary>
    public void InvalidatePath()
    {
        _cachedPath = null;
        _cachedPathIndex = 0;
    }

    /// <summary>缓存路径中是否还有未走完的步。</summary>
    private bool HasCachedStep => _cachedPath != null && _cachedPathIndex < _cachedPath.Count;

    // ── 决策骨架 ──────────────────────────────────────────
    public void OnStepFinished()
    {
        if (IsPaused || activeStrategy == null || Owner == null) return;
        if (IsBusy) return;

        var umc = Owner.unitMovementController;
        if (umc == null) return;

        // 1. 可攻击：清空路径缓存，进入战斗
        if (activeStrategy.CanAttack(this))
        {
            InvalidatePath();
            _idleSearchCounter = 0;
            _pathfindFailed = false;
            if (IsAttackOnCooldown) return;

            activeStrategy.DoCombat(this);
            return;
        }

        // 2. 有缓存路径：直接取下一步（平滑，无节流等待）
        if (HasCachedStep)
        {
            _idleSearchCounter = 0;
            _pathfindFailed = false;
            Vector3 step = _cachedPath[_cachedPathIndex];
            _cachedPathIndex++;
            umc.MoveTo(step, Enums.MovementPurpose.MoveToDestination);
            return;
        }

        // 3. 缓存耗尽/无缓存：重新计算完整路径
        //    节流仅在"连续寻路失败"时生效，避免每帧空跑 Dijkstra。
        //    首次寻路或上次成功后的重算立即执行，保证平滑。
        if (_pathfindFailed)
        {
            _idleSearchCounter++;
            if (_idleSearchCounter < SearchInterval) return;
        }

        // 执行寻路
        _idleSearchCounter = 0;
        var path = activeStrategy.ChooseNextPath(this);
        if (path != null && path.Count > 0)
        {
            _pathfindFailed = false;
            _cachedPath = path;
            _cachedPathIndex = 0;

            Vector3 step = _cachedPath[_cachedPathIndex];
            _cachedPathIndex++;
            umc.MoveTo(step, Enums.MovementPurpose.MoveToDestination);
        }
        else
        {
            // 寻路失败：标记，下次触发节流
            _pathfindFailed = true;
        }
    }

    // ── 子类必须实现 ──────────────────────────────────────
    public abstract Vector3? FindNearestEnemy();
    public abstract Vector3? FindNearestEnemyBuilding();

    public Vector3? FindApproximateDirectionToHiddenBuilding()
    {
        if (_publicBuildingMarkerManager == null || Owner?.unitMovementController == null)
            return null;

        return _publicBuildingMarkerManager.FindNearestApproximateHex(
            Owner.unitMovementController.CurrentHexCoordinate);
    }

    // ── 移民专属 ──────────────────────────────────────────
    public virtual bool TryFoundCity() => false;

    // ── 工具 ──────────────────────────────────────────────
    protected static float HexDistance(Vector3 a, Vector3 b)
    {
        return (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z)) * 0.5f;
    }

    // ── 策略装配 ──────────────────────────────────────────
    public void SetStrategy(IUnitStrategy strategy) => activeStrategy = strategy;

    protected void SetPublicBuildingMarkerManager(PublicBuildingMarkerManager markerManager)
    {
        _publicBuildingMarkerManager = markerManager;
    }
}
