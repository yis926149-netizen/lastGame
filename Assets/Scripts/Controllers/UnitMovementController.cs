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

    //与之对应的CharacterData - 在生产单位模型时外部设置的
    public CharacterData characterData;

    [Header("移动设置")]
    public float moveSpeed = 20.0f; // 移动速度（仅供动画或参考，实际移动由系统控制）
    public float rotationSpeed = 5.0f; // 旋转速度

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
            // 系统移动中、攻击过程中（靠近、攻击动画、返回）均不可选
            if (isMoving) return false;
            if (GoToAttackPosition || CommenceAttack || isAttack || ReturnToOriginalPosition) return false;
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

    // ---------- IUnitMovement 接口实现 ----------
    public GameObject gameObject => base.gameObject;

    public Vector3 CurrentHexCoordinate => _mapDataService.WorldToHexCoordinate(transform.position);

    public float RemainingMovement
    {
        get => currentMovementPoints;
        set => currentMovementPoints = value;
    }

    public float MaxMovement => MaxMovementPoints;

    public bool IsMoving => isMoving;

    // UnitMovementController.cs

    public void MoveTo(Vector3 targetHex, Enums.MovementPurpose purpose = Enums.MovementPurpose.MoveToDestination)
    {
        //Debug.Log($"[UnitMovementController] MoveTo called on {gameObject.name}, target={targetHex}, purpose={purpose}, remainingMP={currentMovementPoints}");

        if (currentMovementPoints <= 0)
        {
            Debug.LogWarning("[UnitMovementController] MoveTo aborted: no movement points left.");
            return;
        }

        if (purpose == Enums.MovementPurpose.MoveToAttack)
        {
            if (attackedUnit == null) return;
            attackTargetPosition = attackedUnit.transform.position;
        }

        // --- 先设定状态 ---
        // 这样如果 RequestMove 内部立即调用了 OnMoveFinished，
        // OnMoveFinished 会把这里的 true 改回 false。
        isMoving = true;
        movementPurpose = purpose;

        // 向系统请求移动
        bool success = _movementSystem.RequestMove(this, targetHex, purpose);

        if (!success)
        {
            // 如果请求失败，再把状态重置回来
            isMoving = false;
            movementPurpose = Enums.MovementPurpose.None;
            attackedUnit = null;
            Debug.Log("[UnitMovementController] Move request rejected.");
        }
        else
        {
            Debug.Log("[UnitMovementController] Move request accepted.");
        }
    }

    public void CancelMove()
    {
        // 暂不需要实现，可由系统处理
    }

    public void ResetMovement()
    {
        RestoreUnitMovementStandbyParameters();
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

        _uiPresenter.RefreshCurrentUnitInfo();

        // 添加一环视野
        HexCellData h = _mapDataService.GetCellByWorldPosition(transform.position);
        for (int i = 0; i < 6; i++)
        {
            if (_mapDataService.GetNeighbor(h, (Enums.HexDirection)i) != null)
                _mapDataService.GetNeighbor(h, (Enums.HexDirection)i).ExploreThisHexCell();
        }
        _mapVisualEvent.Raise();

        // 将停留地块的存在单位标记设置为真
        h.SetHaveUnit(true, gameObject);

        // 如果本次移动目的是攻击，则触发攻击逻辑
        if (movementPurpose == Enums.MovementPurpose.MoveToAttack && attackedUnit != null)
        {
            StartAttackSequence();
        }

        movementPurpose = Enums.MovementPurpose.None;
    }

    // ---------- 攻击、受击、死亡等 ----------
    private void Start()
    {
        MaxMovementPoints = characterData.unitData.MovementPoints;
        currentMovementPoints = MaxMovementPoints;

        animator = GetComponent<Animator>();
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
            animator.SetBool("isMoving", isMoving);
            lastIsMoving = isMoving;
        }
    }

    //死亡
    private void UnitDeath()
    {
        if (characterData.currentHp <= 0)
        {
            animator.SetBool("isDeath", true);
            _audioManager.PlaySFX("cartoon_trumpet_fail(5)");
            Invoke("Destroy", 2.2f);
        }
    }

    private void Destroy()
    {
        Destroy(gameObject);
    }

    private void UnitAttacked()
    {
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

        if (h.landFormType != Enums.LandFormType.BigBones)
            theAttacked.LandFormType_BigBones = 0;
        else
            theAttacked.LandFormType_BigBones = 0.3f;

        if (!h.hasRiver)
            theAttacked.LandFormType_River = 0;
        else
            theAttacked.LandFormType_River = -0.5f;

        float AttackStatueGain = 0;
        for (int i = 0; i < 6; i++)
        {
            HexCellData neighborHex = _mapDataService.GetNeighbor(attackerHex, (Enums.HexDirection)i);
            if (neighborHex != null && neighborHex.BulidingTypeOnHex_Building.Key == Enums.BulidingType.AttackStatue)
                AttackStatueGain += 0.7f;
        }

        float TerrainElevation = 0;
        switch (attackerHex.Height - h.Height)
        {
            case -1: TerrainElevation = -0.5f; break;
            case 1: TerrainElevation = 0.5f; break;
        }

        float AttackPower = attacker.currentAttackValue;
        float AttackGain = 1 + attacker.Resource_Animals + AttackStatueGain + TerrainElevation;
        float Defense = theAttacked.Defense;
        float DefenseGain = 1 + theAttacked.Resource_Minerals + theAttacked.LandFormType_BigBones + theAttacked.LandFormType_River;

        if (AttackGain > 1)
            attacker.Resource_Animals = 0;
        if (DefenseGain > 1)
            theAttacked.Resource_Minerals = 0;

        return Mathf.Max(0, AttackPower * AttackGain - Defense * DefenseGain);
    }

    /// <summary>
    /// 每帧处理攻击相关的移动和动画
    /// </summary>
    private void UnitAttack()
    {
        // ----- 1. 处理攻击冲刺（GoToAttackPosition）-----
        if (GoToAttackPosition)
        {
            MoveToTargetVector(attackTargetPosition);
            // 到达目标附近
            if (Vector3.Distance(transform.position, attackTargetPosition) < 1.5f)
            {              
                GoToAttackPosition = false;
                CommenceAttack = true;
            }
        }

        // ----- 2. 处理返回原位（ReturnToOriginalPosition）-----
        if (ReturnToOriginalPosition)
        {
            MoveToTargetVector(attackerPosition);
            if (Vector3.Distance(transform.position, attackerPosition) < 0.2f)
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

            if (attackedUnit != null)
            {
                // 通知被攻击者
                UnitMovementController targetCtrl = attackedUnit.GetComponent<UnitMovementController>();
                if (targetCtrl != null)
                {
                    targetCtrl.isAttacked = true;
                    targetCtrl.enemyAttacker = gameObject;
                }

                // 建筑
                if (attackTarget == "EnemyBuilding" || attackTarget == "PlayerBuilding")
                {
                    attackedUnit.GetComponent<BuildingController>()?.BuildingAttacked(gameObject);
                }
            }

            // 扣除移动力（攻击即耗尽）
            currentMovementPoints = 0;

            // 特效、音效等保持不变...
            Transform hitParticles = transform.GetChild(transform.childCount - 1);
            hitParticles.gameObject.SetActive(true);

            // 攻击音效
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

            // 延迟停止动画和启动返回
            Invoke(nameof(StopAttackAnimation), 1.5f);
            Invoke(nameof(SetReturnToOriginalPositionTrue), 1.5f);
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

        Transform hitParticles = transform.GetChild(transform.childCount - 1);
        if (hitParticles != null)
            hitParticles.gameObject.SetActive(false);
    }


    /// <summary>
    /// 停止攻击动画
    /// </summary>
    private void StopAttackAnimation()
    {
        animator.SetBool("isAttack", false);
        isAttack = false;
        attackedUnit = null; // 清除目标引用，避免残留
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
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }

            // 移动
            Vector3 move = direction.normalized * moveSpeed * Time.deltaTime;
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


}