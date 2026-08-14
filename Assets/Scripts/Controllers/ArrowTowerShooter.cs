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

    [SerializeField, Tooltip("箭塔攻击范围（格）")]
    private int _attackRange = 2;

    [SerializeField, Tooltip("两次射击之间的间隔时间（秒）")]
    private float _attackInterval = 1f;

    [SerializeField, Tooltip("每次射击对目标造成的伤害值")]
    private float _damage = 15f;

    [SerializeField, Tooltip("箭矢飞行路径的抛物线高度")]
    private float _arcHeight = 2f;

    [SerializeField, Tooltip("箭矢从发射到命中所用的飞行时间（秒）")]
    private float _arrowFlightDuration = 0.3f;

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

        _timer += Time.deltaTime;
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

            GameObject unit = cell.GetUnit();
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
            _effectiveRange = IsHighGround(towerCell) ? _attackRange + 1 : _attackRange;
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
