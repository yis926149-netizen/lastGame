using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zenject;

//****************************************
//创建人：易生
//功能说明：控制单位向目标点移动
//****************************************

public class UnitMovementController : MonoBehaviour, IUnitMovement
{
    //注入
    [Inject] private IMapDataService _mapDataService;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private UnitMovementSystem _movementSystem;
    [Inject] private UIManagerPresenter _uiPresenter;
    [Inject] private AudioManager _audioManager;
    [Inject] private UnitRemovalService _unitRemovalService;
    // 【地图资源配置化】资源统一消费服务（替代原拾取 switch + 特效 provider）
    [Inject] private MapResourceCollectionService _resourceCollectionService;
    // 【普通卡池对象化】按 UnitID 查单位配置（攻击音效配置化）
    [Inject] private IUnitDataProvider _unitData;

    //与之对应的CharacterData - 在生产单位模型时外部设置的
    public CharacterData characterData;

    // 【Excel 数值化】移动/转向/攻击手感参数迁移至 CoreGameplayConfigProvider；
    // 旧 Inspector 字段 moveSpeed/rotationSpeed 已删除。

    //移动力
    public float MaxMovementPoints;
    public float currentMovementPoints;

    //单位移动状态
    public bool isMoving = false;

    //移动方式
    public Enums.MovementPurpose movementPurpose = Enums.MovementPurpose.None;

    //该单位本回合是否移动过（用于回合重置逻辑）
    public bool isMoved = false;

    //单位移动记录（保留以备可能的回放需求，但不再主动使用）
    public int MovedIndex = 0;
    public Dictionary<int, unitMovementRecord> Turn_Record = new Dictionary<int, unitMovementRecord>();

    public bool CanBeSelected
    {
        get
        {
            if (_isDeathScheduled) return false;
            if (currentMovementPoints <= 0) return false;
            // 系统移动中、攻击过程中（靠近、攻击动画、返回）均不可选
            if (isMoving) return false;
            if (GoToAttackPosition || CommenceAttack || isAttack || ReturnToOriginalPosition) return false;
            if (isRangedAttack && attackedUnit != null) return false;
            return true;
        }
    }

    // 锁定攻击流程
    private bool isAttackingInProgress = false; // 防止攻击流程重复触发

    public struct unitMovementRecord
    {
        public HexCellData startHexCell;
        public HexCellData endHexCellData;
        public GameObject movingObject;
        public Enums.MovementPurpose movementPurpose;

        public unitMovementRecord(HexCellData startHexCell, HexCellData endHexCell, GameObject movingObject, Enums.MovementPurpose movementPurpose)
        {
            this.startHexCell = startHexCell;
            this.endHexCellData = endHexCell;
            this.movingObject = movingObject;
            this.movementPurpose = movementPurpose;
        }
    }

    //状态机参数
    private Animator animator;
    private bool lastIsMoving;
    public bool isAttack;
    public bool isAttacked;
    private bool lastAttackedState;
    public GameObject attackedUnit;
    public GameObject enemyAttacker;
    //攻击目标
    public string attackTarget;
    public bool isRangedAttack = false;
    //攻击目标坐标
    public Vector3 attackTargetPosition = new Vector3();
    //攻击前自身坐标
    private Vector3 attackerPosition = new Vector3();
    //前往攻击位置
    private bool GoToAttackPosition = false;
    //结束攻击后回到原位
    private bool ReturnToOriginalPosition = false;
    //开始攻击
    private bool CommenceAttack = false;

    //用于矫正攻击数据计算
    private int attackNumber = 0;

    //单位所属玩家
    [HideInInspector]
    public int PlayerIndex = -1;

    private bool _isMeleeAttackInProgress = false;
    private bool _isDeathScheduled = false;
    /// <summary>死亡流程已触发（动画播放中，等待销毁）。供外部（CombatResolver 等）只读查询。</summary>
    public bool IsDeathScheduled => _isDeathScheduled;

    // 【批次 D】动画-only 标志：为 true 时 CommenceAttack 分支不施加伤害（伤害已由 CombatResolver 结算）
    private bool _animOnly = false;
    private GameObject _hitParticles;

    // ---------- 单位坐标缓存 ----------
    private Vector3 _cachedHexCoord;
    private bool _hexCoordCached;

    public Vector3 CurrentHexCoordinate
    {
        get
        {
            if (!_hexCoordCached)
            {
                _cachedHexCoord = _mapDataService.WorldToHexCoordinate(transform.position);
                _hexCoordCached = true;
            }
            return _cachedHexCoord;
        }
    }

    public float RemainingMovement
    {
        get => currentMovementPoints;
        set => currentMovementPoints = value;
    }

    public float MaxMovement => MaxMovementPoints;

    public bool IsMoving => isMoving;
    public bool IsBusy => isMoving || isAttackingInProgress || GoToAttackPosition || CommenceAttack || isAttack || ReturnToOriginalPosition || (isRangedAttack && attackedUnit != null);

    // UnitMovementController.cs

    public void MoveTo(Vector3 targetHex, Enums.MovementPurpose purpose = Enums.MovementPurpose.MoveToDestination)
    {
        if (_isDeathScheduled) return;
        // 【实时化】移动力配额概念已废除，不再用 currentMovementPoints 作为移动许可判断。

        if (purpose == Enums.MovementPurpose.MoveToAttack)
        {
            if (attackedUnit == null) return;
            attackTargetPosition = attackedUnit.transform.position;
        }

        movementPurpose = purpose;

        // 向系统请求移动。
        // 特殊情况：RequestMove 在 MoveToAttack 且路径为空时会同步调用 OnMoveFinished（仍返回 true），
        // OnMoveFinished 会把 isMoving 设回 false、movementPurpose 设回 None，并触发攻击序列。
        bool success = _movementSystem.RequestMove(this, targetHex, purpose);

        if (!success)
        {
            movementPurpose = Enums.MovementPurpose.None;
            attackedUnit = null;
            // Move request rejected — 正常情况（目标不可达/已被占用），不记录日志避免刷屏。
            return;
        }

        // RequestMove 成功后，若 movementPurpose 尚未被同步的 OnMoveFinished 重置为 None，
        // 说明是正常入队（非同步完成）场景，此时才设 isMoving = true。
        // 这样避免原来"先设 true 再失败改回 false"在同帧完成、Update() 观测不到动画切换的问题。
        if (movementPurpose != Enums.MovementPurpose.None)
            isMoving = true;
    }

    public void CancelMove()
    {
        _movementSystem.CancelMove(this);
        isMoving = false;
        movementPurpose = Enums.MovementPurpose.None;
        attackedUnit = null;
    }

    public void ResetMovement()
    {
        RestoreUnitMovementStandbyParameters();
    }

    public bool TryStartRangedAttack(GameObject target)
    {
        if (target == null || characterData?.unitData == null) return false;
        if (!CanBeSelected || characterData.unitData.BasicAttackRange <= 1) return false;

        Vector3 currentHex = CurrentHexCoordinate;
        int effectiveRange = characterData.unitData.BasicAttackRange;
        HexCellData selfCell = _mapDataService.GetCell(currentHex);
        if (selfCell != null && WaterLevelConfig.ClassifyHeight(selfCell.Height) == 2)
            effectiveRange = characterData.unitData.BasicAttackRange + BattleFormulaRule.HighGroundRangeBonus;

        Vector3 targetHex = _mapDataService.WorldToHexCoordinate(target.transform.position);
        float distance = (Mathf.Abs(currentHex.x - targetHex.x) +
                          Mathf.Abs(currentHex.y - targetHex.y) +
                          Mathf.Abs(currentHex.z - targetHex.z)) * 0.5f;
        if (distance > effectiveRange) return false;

        var targetUnit = target.GetComponent<UnitMovementController>();
        if (targetUnit != null && (targetUnit.characterData == null || targetUnit.characterData.currentHp <= 0)) return false;

        var targetBuilding = target.GetComponent<BuildingBase>();
        if (targetUnit == null && (targetBuilding?.buildingData == null || targetBuilding.buildingData.currentHp <= 0)) return false;

        attackedUnit = target;
        attackTarget = target.tag;
        isRangedAttack = true;
        return true;
    }

    public List<Vector3> GetReachableHexes()
    {
        // 调用系统的可达格子计算
        return _movementSystem.GetAllReachableHexesFromStartHex(
            new List<Vector3>(_mapDataService.GetAllHexCoordinates()),
            CurrentHexCoordinate,
            currentMovementPoints
        );
    }

    public void OnMoveFinished()
    {
        // 移动完成后的处理
        isMoving = false;
        characterData.isSelected = false;

        _cachedHexCoord = _mapDataService.WorldToHexCoordinate(transform.position);
        _hexCoordCached = true;

        AutoHarvestResource();

        _uiPresenter.RefreshCurrentUnitInfo();

        // 如果本次移动目的是攻击，则触发攻击逻辑
        if (movementPurpose == Enums.MovementPurpose.MoveToAttack && attackedUnit != null)
        {
            StartAttackSequence();
        }

        movementPurpose = Enums.MovementPurpose.None;

        // 【批次 B】通知 Brain 决策下一步（形成逐格决策循环）
        // Brain 在 GameLoop.Tick 中也会周期性检测，此处的直接调用是为了减少延迟。
        var brain = GetComponent<UnitBrainBase>();
        if (brain != null && !brain.IsPaused)
        {
            brain.ResetSearchThrottle();
            brain.OnStepFinished();
        }
    }

    // ---------- 攻击、受击、死亡等 ----------
    private void Start()
    {
        MaxMovementPoints = characterData.unitData.MovementPoints;
        currentMovementPoints = MaxMovementPoints;

        animator = GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogWarning($"[UnitMovementController] {gameObject.name} (PlayerIndex={PlayerIndex}): Animator 组件为 null！移动动画将无法播放。请检查预制体根节点是否挂载了 Animator。", gameObject);
        }
        else
        {
            var ctrl = animator.runtimeAnimatorController;
            if (ctrl == null)
                Debug.LogWarning($"[UnitMovementController] {gameObject.name} (PlayerIndex={PlayerIndex}): Animator 存在但 runtimeAnimatorController 为 null！请为预制体 Animator 挂载控制器。", gameObject);
            else
                Debug.Log($"[UnitMovementController] {gameObject.name} (PlayerIndex={PlayerIndex}): Animator 初始化正常，控制器={ctrl.name}", gameObject);

            // 一次性初始化动画参数，避免依赖边沿检测。
            // 控制器文件默认 isMoving=true（swordsman/archer 均为 m_DefaultBool:1），
            // 若从不触发 SetBool，Animator 会一直停在 Run 0（跑步），导致“原地不动 + 播放移动动画”。
            animator.SetBool("isMoving", isMoving);
        }

        lastIsMoving = isMoving;
    }

    void Update()
    {
        // 不再调用 UnitMove()
        UnitAttacked();
        UnitAttack();
        UnitDeath();

        // 动画状态同步
        if (isMoving != lastIsMoving)
        {
            if (animator == null)
            {
                // 兜底：尝试重新获取（运行时动态挂载 Animator 的情况）
                animator = GetComponent<Animator>();
                if (animator == null)
                {
                    Debug.LogWarning($"[UnitMovementController] {gameObject.name} (PlayerIndex={PlayerIndex}): isMoving 切换为 {isMoving}，但 Animator 仍为 null，跳过 SetBool。", gameObject);
                    lastIsMoving = isMoving;
                    return;
                }
            }

            Debug.Log($"[UnitMovementController] {gameObject.name} (PlayerIndex={PlayerIndex}): isMoving 切换 {lastIsMoving} → {isMoving}，调用 animator.SetBool(\"isMoving\", {isMoving})", gameObject);
            animator.SetBool("isMoving", isMoving);
            lastIsMoving = isMoving;
        }
    }

    //死亡
    private void UnitDeath()
    {
        if (_isDeathScheduled || characterData == null || characterData.currentHp > 0) return;

        _isDeathScheduled = true;

        // 取消所有挂起的攻击相关 Invoke（StopAttackAnimation / SetReturnToOriginalPositionTrue 等），
        // 防止死亡动画期间继续冲刺/返回原位。
        CancelInvoke();

        // 暂停 Brain，阻止决策层在死亡动画期间发起新攻击。
        var brain = GetComponent<UnitBrainBase>();
        if (brain != null) brain.IsPaused = true;

        _unitRemovalService.DeactivateUnit(gameObject);
        foreach (var collider in GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }
        var canvas = GetComponentInChildren<Canvas>();
        if (canvas != null)
            canvas.gameObject.SetActive(false);
        animator.SetBool("isDeath", true);
        _audioManager.PlaySFX("cartoon_trumpet_fail(5)");
        Invoke(nameof(RemoveUnit), CoreGameplayConfigProvider.UnitDeathDestroyDelay);
    }

    private void RemoveUnit()
    {
        _unitRemovalService.DestroyDeactivatedUnit(gameObject);
    }

    public void PrepareForRemoval()
    {
        isMoving = false;
        movementPurpose = Enums.MovementPurpose.None;
        isAttackingInProgress = false;
        _isMeleeAttackInProgress = false;
        GoToAttackPosition = false;
        ReturnToOriginalPosition = false;
        CommenceAttack = false;
        isAttack = false;
        attackedUnit = null;
        enemyAttacker = null;

        if (characterData != null)
        {
            characterData.isSelected = false;
        }
    }

    private void UnitAttacked()
    {
        if (_isDeathScheduled) return; // 死亡后不再接受受击处理
        if (isAttacked != lastAttackedState)
        {
            if (enemyAttacker != null)
            {
                Vector3 attackerHorizontalPos = new Vector3(
                    enemyAttacker.transform.position.x,
                    transform.position.y,
                    enemyAttacker.transform.position.z
                );
                transform.LookAt(attackerHorizontalPos);
            }

            animator.SetBool("isAttacked", isAttacked);
            lastAttackedState = isAttacked;
            isAttacked = false;

            if (attackNumber == 0)
            {
                characterData.currentHp -= AttackDataComputation(enemyAttacker.GetComponent<UnitMovementController>().characterData, characterData);
                characterData.healthBar.value = characterData.currentHp / characterData.unitData.hp;
            }
            attackNumber++;
            if (attackNumber == 2) attackNumber = 0;

            _uiPresenter.RefreshCurrentUnitInfo();
        }
    }

    //攻击数据计算
    public float AttackDataComputation(CharacterData attacker, CharacterData theAttacked)
    {
        HexCellData h = _mapDataService.GetCellByWorldPosition(theAttacked.model.transform.position);
        HexCellData attackerHex = _mapDataService.GetCellByWorldPosition(attacker.model.transform.position);

        // 【地图地貌配置化】与 CombatResolver 共用同一地貌效果规则（原 BigBones 缓存字段已删除）
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

        float AttackPower = attacker.currentAttackValue;
        float AttackGain = 1 + attacker.Resource_Animals + AttackStatueGain + TerrainElevation;
        float Defense = theAttacked.Defense;
        float DefenseGain = 1 + theAttacked.Resource_Minerals + landFormDefenseBonus + theAttacked.LandFormType_River;

        return Mathf.Max(0, AttackPower * AttackGain - Defense * DefenseGain);
    }

    /// <summary>
    /// 每帧处理攻击相关的移动和动画
    /// </summary>
    private void UnitAttack()
    {
        if (_isDeathScheduled) return; // 死亡后不再继续任何攻击流程

        // ----- 1. 处理攻击冲刺（GoToAttackPosition）-----
        if (GoToAttackPosition)
        {
            MoveToTargetVector(attackTargetPosition);
            // 到达目标附近
            if (Vector3.Distance(transform.position, attackTargetPosition) < CoreGameplayConfigProvider.AttackArrivalThreshold)
            {              
                GoToAttackPosition = false;
                CommenceAttack = true;
            }
        }

        // ----- 2. 处理返回原位（ReturnToOriginalPosition）-----
        if (ReturnToOriginalPosition)
        {
            MoveToTargetVector(attackerPosition);
            if (Vector3.Distance(transform.position, attackerPosition) < CoreGameplayConfigProvider.AttackReturnThreshold)
            {
                transform.position = attackerPosition;
                ReturnToOriginalPosition = false;
                isAttackingInProgress = false;
                _isMeleeAttackInProgress = false;
            }
        }

        // ----- 3. 触发攻击动画和伤害（CommenceAttack）-----
        if (CommenceAttack)
        {
            CommenceAttack = false; // 立即清除，防止重复触发

            animator.SetBool("isAttack", true);
            isAttack = true;

            // 【批次 D】_animOnly 为 true 时表示伤害已由 CombatResolver 瞬间结算，
            // 此处仅播放动画/音效/特效，跳过所有伤害判定（避免双重扣血）。
            if (!_animOnly && attackedUnit != null)
            {
                // 通知被攻击者
                UnitMovementController targetCtrl = attackedUnit.GetComponent<UnitMovementController>();
                if (targetCtrl != null)
                {
                    targetCtrl.isAttacked = true;
                    targetCtrl.enemyAttacker = gameObject;
                }

                // 建筑（支持多格公共建筑攻击转发，决策#37）
                if (attackTarget == "EnemyBuilding" || attackTarget == "PlayerBuilding" || attackTarget == "NeutralBuilding")
                {
                    attackedUnit.GetComponent<BuildingBase>()?.BuildingAttacked(gameObject);
                }
            }

            // 【批次 D】攻击不再耗尽移动力（实时化无配额）。旧 currentMovementPoints = 0 删除。

            // 特效、音效等保持不变...
            SetHitParticlesActive(true);

            // 攻击音效（对象化：读取 UnitConfig.attackSfx，config 缺失时回退旧 switch）
            PlayAttackSfx();

            // 延迟停止动画和启动返回
            Invoke(nameof(StopAttackAnimation), CoreGameplayConfigProvider.AttackAnimationDuration);
            Invoke(nameof(SetReturnToOriginalPositionTrue), CoreGameplayConfigProvider.AttackAnimationDuration);
        }

        // ----- 4. 初始触发逻辑：移动结束或远程单位准备攻击 -----
        // 注意：这里使用 isAttackingInProgress 防止重复进入
        if (!isAttackingInProgress)
        {
            // 情况A：近战单位已移动到目标邻格（isMoving == false, purpose == MoveToAttack）
            if (!isMoving && movementPurpose == Enums.MovementPurpose.MoveToAttack && currentMovementPoints > 0)
            {
                StartAttackSequence();
            }
            // 情况B：远程单位直接攻击（不移动）
            else if (!isMoving && isRangedAttack && attackedUnit != null)
            {
                // 远程攻击：直接原地开始攻击
                attackerPosition = transform.position; // 记录当前位置（可能不需要返回，但保留）
                transform.LookAt(attackedUnit.transform.position);
                CommenceAttack = true;
                isAttackingInProgress = true;
            }
        }
    }

    /// <summary>攻击音效（对象化：读取 UnitConfig.attackSfx；config 缺失时回退旧 UnitID switch）。</summary>
    private void PlayAttackSfx()
    {
        if (characterData == null || _audioManager == null) return;

        AttackSfxConfig sfx = null;
        if (_unitData != null && _unitData.TryGetUnitConfig(characterData.UnitID, out var config))
        {
            sfx = config.attackSfx;
        }

        if (sfx != null)
        {
            if (!string.IsNullOrEmpty(sfx.primarySfx))
                _audioManager.PlaySFX(sfx.primarySfx);
            if (sfx.delayedSfx != null)
            {
                foreach (AttackSfxEntry entry in sfx.delayedSfx)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.sfxName)) continue;
                    if (entry.delay <= 0f)
                    {
                        _audioManager.PlaySFX(entry.sfxName);
                    }
                    else
                    {
                        StartCoroutine(PlayDelayedSfx(entry.sfxName, entry.delay));
                    }
                }
            }
            return;
        }

        // 回退：旧 switch（config 缺失的过渡期兼容）
        switch (characterData.UnitID)
        {
            case 1:
            case 2:
                _audioManager.PlaySFX("Blunt5");
                Invoke("attackAudio_1_2", 0.5f);
                Invoke("attackAudio_1_2", 1f);
                break;
            case 3: _audioManager.PlaySFX("Indicator4"); break;
            case 4:
                _audioManager.PlaySFX("Weapon_Whoosh 09");
                Invoke("attackAudio_4", 0.6f);
                Invoke("attackAudio_4", 1.0f);
                break;
            case 5:
                _audioManager.PlaySFX("Machine_Gun-008");
                Invoke("attackAudio_5", 0.6f);
                Invoke("attackAudio_5", 1.0f);
                break;
            case 6:
                _audioManager.PlaySFX("Weapon_Whoosh 09");
                Invoke("attackAudio_4", 0.5f);
                Invoke("attackAudio_4", 1.1f);
                _audioManager.PlaySFX("Short_Sword_Hit 03");
                break;
            case 7:
            case 8:
                _audioManager.PlaySFX("Creature_02_05");
                Invoke("attackAudio_7", 0.5f);
                break;
            case 9:
                _audioManager.PlaySFX("Toilet_Flush-006");
                Invoke("attackAudio_9", 0.5f);
                break;
            case 10:
                _audioManager.PlaySFX("Big_Explosion-004");
                Invoke("attackAudio_10", 0.4f);
                Invoke("attackAudio_10", 0.9f);
                break;
            case 11:
                _audioManager.PlaySFX("Weapon_Whoosh 09");
                Invoke("attackAudio_4", 0.5f);
                Invoke("attackAudio_4", 1.0f);
                _audioManager.PlaySFX("Short_Sword_Hit 03");
                break;
        }
    }

    private System.Collections.IEnumerator PlayDelayedSfx(string sfxName, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_audioManager != null) _audioManager.PlaySFX(sfxName);
    }

    private void attackAudio_1_2() => _audioManager.PlaySFX("Blunt5");
    private void attackAudio_4() => _audioManager.PlaySFX("Short_Sword_Hit 04");
    private void attackAudio_5() => _audioManager.PlaySFX("Machine_Gun-008");
    private void attackAudio_7() => _audioManager.PlaySFX("Creature_02_05");
    private void attackAudio_9() => _audioManager.PlaySFX("Toilet_Flush-006");
    private void attackAudio_10() => _audioManager.PlaySFX("Big_Explosion-004");

    /// <summary>
    /// 设置返回原位标志（由Invoke调用）
    /// </summary>
    private void SetReturnToOriginalPositionTrue()
    {
        if (_isMeleeAttackInProgress)
        {
            ReturnToOriginalPosition = true;
        }
        else
        {
            isAttackingInProgress = false;
        }

        SetHitParticlesActive(false);
    }

    private void SetHitParticlesActive(bool active)
    {
        if (_hitParticles == null)
        {
            Transform particles = GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(child => child != transform &&
                                         string.Equals(child.name, "particles", StringComparison.OrdinalIgnoreCase));
            _hitParticles = particles?.gameObject;
        }

        if (_hitParticles != null)
            _hitParticles.SetActive(active);
    }


    /// <summary>
    /// 停止攻击动画
    /// </summary>
    private void StopAttackAnimation()
    {
        animator.SetBool("isAttack", false);
        isAttack = false;
        attackedUnit = null; // 清除目标引用，避免残留
        _animOnly = false;   // 重置动画-only 标志
    }

    /// <summary>
    /// 启动攻击序列
    /// </summary>
    private void StartAttackSequence()
    {
        isAttackingInProgress = true;

        if (attackedUnit == null)
        {
            isAttackingInProgress = false;
            movementPurpose = Enums.MovementPurpose.None;
            return;
        }

        // 记录当前所在 Hex 的中心
        HexCellData cell = _mapDataService.GetCellByWorldPosition(transform.position);
        attackerPosition = cell.RealCenterWorldCoordinate;

        Vector3 lookTarget = attackedUnit.transform.position;
        lookTarget.y = transform.position.y;
        transform.LookAt(lookTarget);

        GoToAttackPosition = true;
        _isMeleeAttackInProgress = true;

        Debug.Log($"[Attack] Recorded AttackerPosition: {attackerPosition}");
    }

    /// <summary>
    /// 移动到目标向量（用于冲刺和返回）
    /// </summary>
    private void MoveToTargetVector(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - transform.position;
        if (direction.magnitude > 0.01f)
        {
            // 水平旋转
            Vector3 horizontalDir = new Vector3(direction.x, 0, direction.z);
            if (horizontalDir != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(horizontalDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, CoreGameplayConfigProvider.UnitRotationSpeed * Time.deltaTime);
            }

            // 移动
            Vector3 move = direction.normalized * CoreGameplayConfigProvider.AttackDashSpeed * Time.deltaTime;
            if (move.magnitude > direction.magnitude)
                transform.position = targetPosition;
            else
                transform.position += move;
        }
    }

    //恢复单位移动参数至待机值
    public void RestoreUnitMovementStandbyParameters()
    {
        if (characterData != null && characterData.unitData != null)
            MaxMovementPoints = characterData.unitData.MovementPoints;
        currentMovementPoints = MaxMovementPoints;
        isMoved = false; // 新回合可移动
    }

    // ── 【批次 D】纯动画接口：不含任何伤害，供 CombatResolver 结算后调用 ────────────
    /// <summary>
    /// 播放一次近战攻击动画（冲刺 → 攻击动画 → 返回原位 + 音效）。
    /// 伤害已由 CombatResolver 瞬间结算，此处仅负责表现。
    /// </summary>
    public void PlayAttackAnim(GameObject target)
    {
        if (_isDeathScheduled || target == null || isAttackingInProgress) return;

        _animOnly = true;            // 告诉 CommenceAttack 分支跳过伤害
        attackedUnit = target;
        attackTarget = target.tag;
        isAttackingInProgress = true;

        HexCellData cell = _mapDataService.GetCellByWorldPosition(transform.position);
        attackerPosition = cell != null ? cell.RealCenterWorldCoordinate : transform.position;

        Vector3 lookTarget = target.transform.position;
        lookTarget.y = transform.position.y;
        transform.LookAt(lookTarget);

        attackTargetPosition = target.transform.position;
        // 新版战斗逻辑已由 CombatResolver 瞬时结算；这里只原地播放表现，
        // 不再复用旧版“冲向目标再退回原格”的攻击位移状态机。
        GoToAttackPosition = false;
        ReturnToOriginalPosition = false;
        _isMeleeAttackInProgress = false;
        CommenceAttack = true;
    }

    /// <summary>
    /// 播放一次远程攻击动画（原地朝向目标 + 攻击动画 + 音效，无冲刺）。
    /// 伤害已由 CombatResolver 瞬间结算，此处仅负责表现。
    /// </summary>
    public void PlayRangedAttackAnim(GameObject target)
    {
        if (_isDeathScheduled || target == null || isAttackingInProgress) return;

        _animOnly = true;
        attackedUnit = target;
        attackTarget = target.tag;
        isAttackingInProgress = true;
        _isMeleeAttackInProgress = false; // 远程不返回原位

        attackerPosition = transform.position;

        Vector3 lookTarget = target.transform.position;
        lookTarget.y = transform.position.y;
        transform.LookAt(lookTarget);

        // 直接进入攻击动画（不冲刺）
        CommenceAttack = true;
    }

    private void AutoHarvestResource()
    {
        if (characterData == null || characterData.unitData == null) return;

        HexCellData currentCell = _mapDataService.GetCellByWorldPosition(transform.position);
        if (currentCell == null) return;

        // 【地图资源配置化】拾取效果/特效/音效统一由 MapResourceCollectionService 按 MapResourceSO 配置执行
        _resourceCollectionService.TryCollectForUnit(currentCell, characterData, PlayerIndex);
    }

}
