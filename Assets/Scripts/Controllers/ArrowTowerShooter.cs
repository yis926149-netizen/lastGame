using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Zenject;
using DG.Tweening;

public class ArrowTowerShooter : MonoBehaviour
{
    [Inject] private IMapDataService _mapDataService;
    [Inject] private GameLoop _gameLoop;
    [Inject] private IUnitRepository _unitRepository;
    [Inject] private UnitRemovalService _unitRemovalService;
    // 【伤害飘字】表现层事件总线：可选注入，缺失时静默跳过
    [Inject(Optional = true)] private DamageEventBroker _damageEventBroker;

    // 【Excel 数值化】箭塔射程/间隔/伤害/弹道/飞行时长迁移至 CoreGameplayConfigProvider（旧 Inspector 字段已删除）。
    private int _attackRange => CoreGameplayConfigProvider.ArrowTowerRange;
    private float _attackInterval => CoreGameplayConfigProvider.ArrowTowerAttackInterval;
    private float _damage => CoreGameplayConfigProvider.ArrowTowerDamage;
    private float _arcHeight => CoreGameplayConfigProvider.ArrowTowerArcHeight;
    private float _arrowFlightDuration => CoreGameplayConfigProvider.ArrowTowerFlightDuration;

    [SerializeField, Tooltip("箭矢预制体，射击时实例化并飞向目标")]
    private GameObject _arrowPrefab;

    [SerializeField, Tooltip("箭矢的发射点（箭塔上的出箭位置）")]
    private Transform _shootPoint;

    private float _timer;
    private GameObject _lockedTarget;
    private CharacterData _lockedTargetData;
    private int _effectiveRange;
    private bool _rangeCalculated;

    private void Awake()
    {
        _timer = 0f;
    }

    void Update()
    {
        if (_gameLoop == null || _gameLoop.IsPaused) return;

        // 【断供方案-阶段2】失能（断供）箭塔停火；恢复供应前清锁，避免对旧目标射击
        BuildingBase building = GetComponent<BuildingBase>();
        if (building != null && !building.IsFunctional)
        {
            if (_lockedTarget != null)
                ClearTargetLock();
            return;
        }

        // 缩放时间：x2/x3 时射击间隔同步加速（_gameLoop 已在方法开头判空，此处必然非空）
        _timer += _gameLoop.ScaledDeltaTime;
        if (_timer < _attackInterval) return;
        _timer = 0f;

        TryShoot();
    }

    private void TryShoot()
    {
        if (IsLockedTargetValid())
        {
            FireArrow(_lockedTarget, _lockedTargetData);
            return;
        }

        ClearTargetLock();

        Vector3 hexCoord = _mapDataService.WorldToHexCoordinate(transform.position);
        HexCellData centerHex = _mapDataService.GetCell(hexCoord);
        if (centerHex == null) return;

        int range = GetEffectiveRange(centerHex);
        HashSet<HexCellData> cellsInRange = CollectCellsInRange(centerHex, range);

        GameObject bestTarget = null;
        CharacterData bestData = null;
        float bestDist = float.MaxValue;

        foreach (var cell in cellsInRange)
        {
            if (cell == null || cell.HexType == Enums.HexType.LakeOrSea) continue;

            // 【多单位落点】枚举格内全部站位单位，取最近敌方单位。
            foreach (GameObject unit in cell.GetStandingUnits())
            {
                if (unit == null) continue;

                if (!TryGetEnemyTargetData(unit, out CharacterData data)) continue;

                if (data == null || data.unitData == null || data.currentHp <= 0) continue;

                float dist = HexDistance(centerHex.HexCoordinate, cell.HexCoordinate);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestTarget = unit;
                    bestData = data;
                }
            }
        }

        if (bestTarget == null || bestData == null) return;

        _lockedTarget = bestTarget;
        _lockedTargetData = bestData;
        FireArrow(_lockedTarget, _lockedTargetData);
    }

    private bool IsLockedTargetValid()
    {
        if (_lockedTarget == null ||
            !TryGetEnemyTargetData(_lockedTarget, out CharacterData currentData) ||
            currentData == null ||
            currentData.currentHp <= 0 ||
            !IsTargetInRange(_lockedTarget))
        {
            return false;
        }

        _lockedTargetData = currentData;
        return true;
    }

    private bool TryGetEnemyTargetData(GameObject unit, out CharacterData data)
    {
        data = null;
        if (unit == null || _unitRepository == null) return false;

        BuildingController building = GetComponent<BuildingController>();
        bool isPlayerTower = building != null && building.Player_City_Index.Key >= 0
            ? building.Player_City_Index.Key == 0
            : gameObject.CompareTag("PlayerBuilding");

        return isPlayerTower
            ? _unitRepository.TryGetEnemyUnit(unit, out data)
            : _unitRepository.TryGetPlayerUnit(unit, out data);
    }

    private bool IsTargetInRange(GameObject target)
    {
        if (target == null || _mapDataService == null) return false;

        HexCellData towerCell = _mapDataService.GetCellByWorldPosition(transform.position);
        HexCellData targetCell = _mapDataService.GetCellByWorldPosition(target.transform.position);
        if (towerCell == null || targetCell == null) return false;

        int range = GetEffectiveRange(towerCell);
        return HexDistance(towerCell.HexCoordinate, targetCell.HexCoordinate) <= range;
    }

    private int GetEffectiveRange(HexCellData towerCell)
    {
        if (!_rangeCalculated)
        {
            _effectiveRange = IsHighGround(towerCell) ? _attackRange + BattleFormulaRule.HighGroundRangeBonus : _attackRange;
            _rangeCalculated = true;
        }
        return _effectiveRange;
    }

    private bool IsHighGround(HexCellData towerCell)
    {
        if (towerCell == null) return false;
        return WaterLevelConfig.ClassifyHeight(towerCell.Height) == 2;
    }

    private void ClearTargetLock()
    {
        _lockedTarget = null;
        _lockedTargetData = null;
    }

    private void FireArrow(GameObject target, CharacterData targetData)
    {
        Vector3 startPos = _shootPoint != null ? _shootPoint.position : transform.position;
        Vector3 endPos = target.transform.position + Vector3.up * 1f;

        if (_arrowPrefab == null) return;

        GameObject arrow = Object.Instantiate(_arrowPrefab);
        arrow.transform.position = startPos;
        arrow.transform.LookAt(endPos);
        arrow.SetActive(true);

        Sequence seq = DOTween.Sequence();

        Vector3[] path = new Vector3[3];
        path[0] = startPos;
        path[1] = (startPos + endPos) * 0.5f + Vector3.up * _arcHeight;
        path[2] = endPos;

        // 箭矢飞行随速度档同步加速（timeScale 与 Animator.speed 同源；暂停时 0 冻结，恢复后继续）
        seq.timeScale = _gameLoop != null ? _gameLoop.SpeedMultiplier : 1f;

        seq.Append(arrow.transform.DOPath(path, _arrowFlightDuration, PathType.CatmullRom).SetEase(Ease.Linear));
        seq.Join(arrow.transform.DOScale(0.5f, _arrowFlightDuration).SetEase(Ease.InQuad));

        seq.OnUpdate(() =>
        {
            if (arrow != null)
                arrow.transform.LookAt(endPos);
        });

        seq.OnComplete(() =>
        {
            Object.Destroy(arrow);

            if (!TryGetEnemyTargetData(target, out CharacterData currentData) ||
                currentData != targetData ||
                targetData.currentHp <= 0)
            {
                if (target == _lockedTarget) ClearTargetLock();
                return;
            }

            targetData.currentHp -= _damage;

            // 【伤害飘字】箭到结算时发布表现事件（锚点优先血条位置）
            if (_damage > 0f && _damageEventBroker != null)
            {
                Vector3 anchor = targetData.healthBar != null
                    ? targetData.healthBar.transform.position
                    : target.transform.position;
                int targetFaction = targetData.unitMovementController != null
                    ? targetData.unitMovementController.PlayerIndex
                    : -1;
                _damageEventBroker.RaiseDamage(anchor, _damage, targetFaction: targetFaction);
            }

            if (targetData.healthBar != null && targetData.unitData.hp > 0)
                targetData.healthBar.value = Mathf.Max(0, targetData.currentHp / targetData.unitData.hp);

            if (targetData.currentHp <= 0)
            {
                _unitRemovalService.RemoveUnit(target);
                if (target == _lockedTarget) ClearTargetLock();
            }
        });
    }

    private HashSet<HexCellData> CollectCellsInRange(HexCellData center, int maxRange)
    {
        HashSet<HexCellData> result = new HashSet<HexCellData>();
        result.Add(center);

        List<HexCellData> frontier = new List<HexCellData> { center };

        for (int ring = 1; ring <= maxRange; ring++)
        {
            List<HexCellData> next = new List<HexCellData>();
            foreach (var cell in frontier)
            {
                for (int i = 0; i < 6; i++)
                {
                    HexCellData neighbor = _mapDataService.GetNeighbor(cell, (Enums.HexDirection)i);
                    if (neighbor != null && result.Add(neighbor))
                        next.Add(neighbor);
                }
            }
            frontier = next;
        }

        return result;
    }

    private static float HexDistance(Vector3 a, Vector3 b)
    {
        return (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z)) * 0.5f;
    }
}
