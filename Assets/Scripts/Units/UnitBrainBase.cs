using System.Collections.Generic;
using UnityEngine;

//****************************************
// 功能说明：单位行为基类（MonoBehaviour，挂在每个单位 GameObject 上）。
//
// 【批次 C】新增回血计时器（农田地貌/祭坛建筑），替代 SettlementPhase 每回合回血。
//   计时器在 Update 中累加（非暂停时），到达间隔后触发回血。
//   【地图地貌配置化】农田回血参数改由当前格 MapLandFormSO 配置，不再对齐 LandFormConfigSO。
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

    // ── 阵营 id（寻路迷雾判定用）──────────────────────────
    // 优先读 UnitMovementController.PlayerIndex（真相源，支持多 AI 阵营），
    // 缺失时退回 tag 判定（PlayerUnit=0，其余=1）。
    public int FactionId
    {
        get
        {
            var umc = Owner?.unitMovementController;
            if (umc != null && umc.PlayerIndex >= 0) return umc.PlayerIndex;
            return Owner?.model != null && Owner.model.CompareTag("PlayerUnit") ? 0 : 1;
        }
    }

    // ── 回血计时器（批次 C）──────────────────────────────
    // 【地图地貌配置化】农田回血参数来自当前格 MapLandFormSO（LandFormEffectRule.TryGetPeriodicHeal），
    // 不再使用硬编码常量；祭坛回血兜底间隔已迁移至 CoreGameplayConfigProvider。
    private float _landHealTimer = 0f;
    private float _buildingHealTimer = 0f;
    private MapLandFormSO _landHealSource;

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
        // 【地图地貌配置化】回血参数按当前格地貌配置查询
        MapLandFormSO current = h?.landForm;
        if (!LandFormEffectRule.TryGetPeriodicHeal(current, out float ratio, out float interval))
        {
            _landHealTimer = 0f;  // 离开农田时重置
            _landHealSource = null;
            return;
        }

        // 回血来源切换（移动到不同配置的回血地貌）时重置计时，避免沿用旧地貌已累计的时间
        if (_landHealSource != current)
        {
            _landHealSource = current;
            _landHealTimer = 0f;
        }

        _landHealTimer += dt;
        if (_landHealTimer < interval) return;

        _landHealTimer = 0f;
        if (Owner.unitData != null)
            Owner.Heal(ratio * Owner.unitData.hp);
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

        float interval = ctrl.buildingData.HealInterval > 0 ? ctrl.buildingData.HealInterval : CoreGameplayConfigProvider.BuildingHealIntervalFallback;
        _buildingHealTimer += dt;
        if (_buildingHealTimer < interval) return;

        _buildingHealTimer = 0f;
        Owner.Heal(ctrl.buildingData.AltarValue * Owner.unitData.hp);
    }

    // ── 决策节流 ──────────────────────────────────────────
    // 节流仅在“上一次寻路失败（被困/无可达目标）”后生效，防止每帧重复空跑 Dijkstra。
    // 正常沿缓存路径逐格前进、以及路径耗尽后的立即重算都不节流，实现平滑移动。
    private int _idleSearchCounter;
    private int SearchInterval => CoreGameplayConfigProvider.UnitPathSearchThrottle;
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

    /// <summary>
    /// 【竞技场-阶段二】最近中央宝箱（索敌链第二优先级：敌方单位 > 宝箱 > 敌方建筑，玩法文档 §4.2）。
    /// 近战/远程共用；箭塔不索敌建筑故天然忽略宝箱。
    /// </summary>
    public virtual Vector3? FindNearestChest()
    {
        if (Owner?.model == null || MapData == null || Movement == null) return null;

        List<Vector3> allPoints = new List<Vector3>(MapData.GetAllHexCoordinates());
        Vector3 startHex = Owner.unitMovementController?.CurrentHexCoordinate ?? MapData.WorldToHexCoordinate(Owner.model.transform.position);
        if (startHex == default) return null;

        Vector3? best = null;
        float bestCost = float.MaxValue;

        foreach (var cell in MapData.GetAllCells())
        {
            GameObject building = cell.BulidingTypeOnHex_Building.Value;
            if (building == null || building.GetComponent<CentralChest>() == null) continue;

            Vector3 endHex = cell.HexCoordinate;
            if (HexDistance(startHex, endHex) >= bestCost) continue;

            if (Movement.CalculateMinMovementCostBetweenTwoHexes(
                    allPoints, startHex, endHex,
                    Enums.MovementPurpose.MoveToAttack, FactionId, out float cost, out _)
                && cost < bestCost)
            {
                bestCost = cost;
                best = endHex;
            }
        }

        return best;
    }

    // ── 隔绝目标查询（忽略可达性）────────────────────────
    // FindNearestEnemy/Chest/EnemyBuilding 三个查询都带可达性过滤（cost 有解才入选），
    // 被海洋完全隔绝的目标因此对索敌链"隐形"，三查询齐返回 null → 策略落到兜底原地站桩。
    // 下面这组同名镜像只按六边形距离选目标，专供"目标被隔开 → 走到最接近目标的地块"使用。

    /// <summary>最近敌方单位（忽略可达性，纯六边形距离）。</summary>
    public abstract Vector3? FindNearestEnemyIgnoringReachability();

    /// <summary>最近敌方/中立建筑（忽略可达性）。阵营 tag 两边不同，由子类覆写。</summary>
    public virtual Vector3? FindNearestBuildingIgnoringReachability() => null;

    /// <summary>最近中央宝箱（忽略可达性，纯六边形距离）。</summary>
    public virtual Vector3? FindNearestChestIgnoringReachability()
    {
        if (Owner?.model == null || MapData == null) return null;

        Vector3 startHex = Owner.unitMovementController?.CurrentHexCoordinate ?? MapData.WorldToHexCoordinate(Owner.model.transform.position);
        if (startHex == default) return null;

        Vector3? best = null;
        float bestDist = float.MaxValue;

        foreach (var cell in MapData.GetAllCells())
        {
            GameObject building = cell.BulidingTypeOnHex_Building.Value;
            if (building == null || building.GetComponent<CentralChest>() == null) continue;

            float dist = HexDistance(startHex, cell.HexCoordinate);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = cell.HexCoordinate;
            }
        }

        return best;
    }

    /// <summary>
    /// 在本单位所在的连通可达区域内，找到距 <paramref name="targetHex"/> 六边形距离最近的格。
    /// 用于目标被水域/山体完全隔绝时"尽量走到最接近目标的地块"（远程可隔海射击，近战海边驻扎）。
    /// 返回值等于 startHex 表示当前已是最优岸格，调用方据此驻守。
    /// </summary>
    public Vector3? FindClosestReachableCellToTarget(List<Vector3> allPoints, Vector3 startHex, Vector3 targetHex)
    {
        if (Movement == null || allPoints == null || allPoints.Count == 0) return null;

        // 预算必须有限：GetAllReachableHexesFromStartHex 的收尾判据是 minCost <= totalCost，
        // 而不可达格的 minCost 恰为 float.MaxValue —— 传 float.MaxValue 会因 MaxValue <= MaxValue
        // 成立而把全图（含水域）一并返回。单格 cost 为 1，故任何可达格代价必 <= 总格数。
        List<Vector3> reachable = Movement.GetAllReachableHexesFromStartHex(allPoints, startHex, allPoints.Count, FactionId);
        if (reachable == null || reachable.Count == 0) return null;

        Vector3 best = startHex;
        float bestDist = float.MaxValue;
        float bestFromStart = float.MaxValue;

        foreach (Vector3 hex in reachable)
        {
            float dist = HexDistance(hex, targetHex);
            // 并列时取离出发点更近者：省去逐候选跑 Dijkstra 求真实代价，同时保证结果稳定。
            float fromStart = HexDistance(hex, startHex);
            if (dist < bestDist || (dist == bestDist && fromStart < bestFromStart))
            {
                bestDist = dist;
                bestFromStart = fromStart;
                best = hex;
            }
        }

        return best;
    }

    // ── 随机游走（无目标兜底）────────────────────────────

    /// <summary>
    /// 游走半径（格）。所有可通行陆格 movementCost == 1，故此值等价于 Dijkstra 花费预算。
    /// 刻意写成常量：currentMovementPoints 已改造为速度倍率（0.5/1.0），不再是移动力配额，
    /// 不能当游走预算用。后续若需可配，再提升到 Excel（游戏数值配置.xlsx → 核心玩法配置）。
    /// </summary>
    public const float WanderRadiusHexes = 3f;

    // 每单位独立的游走随机源。
    // 不能用 SeedService.GetRandom("AI")：它每次都 new 新实例（见 AIRandomProvider 注释），
    // 各单位自取会拿到完全相同的序列 → 全员同步游走；UnityEngine.Random 则是全局态，破坏可复现性。
    // 静态计数器在确定的 spawn 顺序下保持可复现，同时保证每单位序列互异。
    private static int _wanderSeedCounter;
    private System.Random _wanderRandom;

    // 上一次游走目标。作为候选排除项，防止在两格间来回横跳
    //（旧 ChooseFrontierStep 被删就是因为"随机游走打转"，见 commit 40f63c8cc）。
    private Vector3? _lastWanderTarget;

    /// <summary>
    /// 在 <see cref="WanderRadiusHexes"/> 半径内随机挑一个可达格作为游走目标。
    /// 排除当前格与上一次游走目标（防打转）。无候选时返回 null。
    /// </summary>
    public Vector3? PickRandomWanderTarget(List<Vector3> allPoints, Vector3 startHex)
    {
        if (Movement == null || allPoints == null || allPoints.Count == 0) return null;

        List<Vector3> reachable = Movement.GetAllReachableHexesFromStartHex(allPoints, startHex, WanderRadiusHexes, FactionId);
        if (reachable == null || reachable.Count == 0) return null;

        reachable.RemoveAll(v => v == startHex ||
                                 (_lastWanderTarget.HasValue && v == _lastWanderTarget.Value));
        if (reachable.Count == 0) return null;

        // & int.MaxValue 与 SeedService 一致：避免负种子（Random(int.MinValue) 会 Abs 溢出）。
        _wanderRandom ??= new System.Random((SeedService.CurrentSeed * 31 + (++_wanderSeedCounter)) & int.MaxValue);

        Vector3 chosen = reachable[_wanderRandom.Next(reachable.Count)];
        _lastWanderTarget = chosen;
        return chosen;
    }

    /// <summary>
    /// 在索敌链（单位 > 宝箱 > 建筑）内取第一个"存在但不可达"的目标 —— 即被水域/山体完全隔绝。
    /// 只挑忽略可达性查询有结果、而对应可达性查询无结果者。null 表示无任何被隔绝目标。
    /// </summary>
    private Vector3? FirstUnreachable(List<Vector3> allPoints, Vector3 startHex)
    {
        Vector3? enemy = FindNearestEnemyIgnoringReachability();
        if (enemy.HasValue && !IsReachable(allPoints, startHex, enemy.Value))
            return enemy;

        Vector3? chest = FindNearestChestIgnoringReachability();
        if (chest.HasValue && !IsReachable(allPoints, startHex, chest.Value))
            return chest;

        Vector3? building = FindNearestBuildingIgnoringReachability();
        if (building.HasValue && !IsReachable(allPoints, startHex, building.Value))
            return building;

        return null;
    }

    private bool IsReachable(List<Vector3> allPoints, Vector3 startHex, Vector3 targetHex)
    {
        return Movement != null &&
               Movement.CalculateMinMovementCostBetweenTwoHexes(
                   allPoints, startHex, targetHex,
                   Enums.MovementPurpose.MoveToAttack, FactionId, out float cost, out _)
               && cost < float.MaxValue;
    }

    /// <summary>
    /// 【临时调试开关】隔海趋近（5a）总开关。
    /// false = 屏蔽 5a，兜底直接落到 5b 随机游走，用于排查"双方单位隔海相望卡住"是否由本逻辑引起。
    /// 排查结束后改回 true 即可恢复，不要删除 5a 代码。
    /// </summary>
    public static bool EnableIsolatedShoreApproach = true;

    /// <summary>
    /// 两级空闲兜底（无目标可打/可到达时）：先隔海趋近，再随机游走。
    /// 由 MeleeStrategy / RangedStrategy 在原先直接 return null 的兜底处调用。
    /// </summary>
    /// <returns>完整到达路径；已在最优岸格/游走无解时返回 null（由节流机制驻守）。</returns>
    public List<Vector3> ChooseFallbackPath(List<Vector3> allPoints, Vector3 startHex)
    {
        // 5a. 隔海趋近：目标存在但被水/山隔绝，走到最接近目标的可达格。
        //     必须校验"确实不可达"：近战 step 2 的警戒范围（3格）门槛会让一个**可达但较远**的敌人
        //     也落到本兜底，若不校验就会变成无限追击，既越过 AlertRange 的设计意图，
        //     也抢掉了"无目标 → 随机游走"。用户的语义是「目标被隔开」，即真正不可达。
        //     【临时屏蔽】EnableIsolatedShoreApproach=false 时整段跳过，直接走 5b 随机游走。
        if (EnableIsolatedShoreApproach)
        {
            Vector3? isolated = FirstUnreachable(allPoints, startHex);
            if (isolated.HasValue)
            {
                Vector3? shore = FindClosestReachableCellToTarget(allPoints, startHex, isolated.Value);
                if (!shore.HasValue)
                    return null;   // 连起点都在孤立区域外（理论上不会发生），驻守兜底

                if (shore.Value == startHex)
                    return null;   // 已在最优岸格（近战海边驻扎 / 远程射程不够驻守），不再游走

                if (Movement.CalculateMinMovementCostBetweenTwoHexes(
                        allPoints, startHex, shore.Value,
                        Enums.MovementPurpose.MoveToDestination, FactionId, out _, out List<Vector3> shorePath)
                    && shorePath != null && shorePath.Count > 0)
                {
                    return shorePath;
                }
                return null;   // 岸格路径求解失败，驻守；不落入游走，避免近战离开海岸
            }
        }

        // 5b. 真无目标 → 随机游走。
        Vector3? wanderTarget = PickRandomWanderTarget(allPoints, startHex);
        if (!wanderTarget.HasValue) return null;

        if (Movement.CalculateMinMovementCostBetweenTwoHexes(
                allPoints, startHex, wanderTarget.Value,
                Enums.MovementPurpose.MoveToDestination, FactionId, out _, out List<Vector3> wanderPath)
            && wanderPath != null && wanderPath.Count > 0)
        {
            return wanderPath;
        }
        return null;
    }

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
