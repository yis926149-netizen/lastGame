using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI 逻辑迷雾服务（仅逻辑，不渲染）。
/// 为每个 AI 阵营现算"当前可见格集合"，供 AI 决策使用；并维护"探索记忆"（历次视野并集）
/// 仅供导航（前沿/未探索方向游走），不参与锁敌。
///
/// 视野源与玩家侧对称：该 AI 的所有单位（各自 UnitData.ViewPoints 圈）+ 该 AI 领土
/// （势力范围每格外扩 CityViewRadius 圈），并集为该阵营共享视野。
///
/// 注意：本服务不写任何 HexCellData 字段（IsVisible/IsExplored 属玩家专属），
/// 只返回集合、内部维护 AI 自己的记忆，保证与玩家迷雾互不干扰。
/// </summary>
public class AIFogService
{
    private readonly IMapDataService _mapDataService;
    private readonly IUnitRepository _unitRepository;
    private readonly EnemyModelManager _enemyModelManager;

    // 领土视野半径（与玩家侧 FieldOfViewService.CityViewRadius 对齐）
    private const int CityViewRadius = 1;

    // 每个 AI 的探索记忆（历次可见并集，单调增长）——仅用于导航，不用于锁敌
    private readonly Dictionary<int, HashSet<HexCellData>> _aiExplored = new Dictionary<int, HashSet<HexCellData>>();

    public AIFogService(
        IMapDataService mapDataService,
        IUnitRepository unitRepository,
        EnemyModelManager enemyModelManager)
    {
        _mapDataService = mapDataService;
        _unitRepository = unitRepository;
        _enemyModelManager = enemyModelManager;
    }

    /// <summary>
    /// 现算某 AI 阵营当前可见格集合（每单位决策前调用一次，反映本回合已移动单位的新位置）。
    /// 顺带把结果并入该 AI 的探索记忆。
    /// </summary>
    public HashSet<HexCellData> ComputeVisible(int aiIndex)
    {
        var visible = new HashSet<HexCellData>();

        // 1. 该 AI 的所有单位：各自 ViewPoints 圈
        if (_unitRepository != null)
        {
            var group = _unitRepository.GetEnemyUnitGroup(aiIndex);
            if (group != null)
            {
                foreach (var kv in group)
                {
                    GameObject unitGO = kv.Key;
                    CharacterData data = kv.Value;
                    if (unitGO == null || data == null) continue;

                    HexCellData originCell = _mapDataService.GetCellByWorldPosition(unitGO.transform.position);
                    if (originCell == null) continue;

                    int radius = 1;
                    if (data.unitData != null)
                        radius = Mathf.Max(1, Mathf.RoundToInt(data.unitData.ViewPoints));

                    FieldOfViewService.CollectVisibleCells(_mapDataService, originCell, radius, visible);
                }
            }
        }

        // 2. 该 AI 的领土：势力范围每格外扩 CityViewRadius 圈
        if (_enemyModelManager != null &&
            _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData != null &&
            _enemyModelManager.Enemy_SphereOfInfluence_HexC_HexCellData.TryGetValue(aiIndex, out var territory) &&
            territory != null)
        {
            foreach (var cell in territory.Values)
            {
                if (cell == null) continue;
                FieldOfViewService.CollectVisibleCells(_mapDataService, cell, CityViewRadius, visible);
            }
        }

        // 3. 并入探索记忆
        if (!_aiExplored.TryGetValue(aiIndex, out var explored))
        {
            explored = new HashSet<HexCellData>();
            _aiExplored[aiIndex] = explored;
        }
        explored.UnionWith(visible);

        return visible;
    }

    /// <summary>
    /// 该格是否从未被该 AI 探索过（用于"未探索方向"打分）。
    /// </summary>
    public bool IsUnexplored(int aiIndex, HexCellData cell)
    {
        if (cell == null) return false;
        return !(_aiExplored.TryGetValue(aiIndex, out var explored) && explored.Contains(cell));
    }
}
