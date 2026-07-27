using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

//****************************************
//创建人：易生
//功能说明：AI 管理器（协调者）。持有 AI 状态与场景引用，编排开局初始化与每回合流程；
//         具体逻辑委托给 AIEntityFactory / AICardBrain / AITacticalBrain。
//****************************************

public class AIManager : MonoBehaviour, IAIManager
{
    [Inject] private IMapDataService _mapDataService;
    [Inject] private AIEntityFactory _factory;
    [Inject] private AICardBrain _cardBrain;
    [Inject] private AIRandomProvider _rng;
    [Inject] private GoldWallet _goldWallet;

    // 城市预制体：Inspector 序列化引用，注入后转交工厂（场景引用无法直接注入普通类）。
    public GameObject AICity;

    /// <summary>AI 无操作开关：勾选后 AI 不执行任何操作（探索、出牌、单位AI），方便开发调试。</summary>
    public bool AIDisabled;

    private System.Random Random => _rng.Random;

    private void Start()
    {
        // 把场景序列化的城市预制体交给工厂（工厂是普通类，拿不到 Inspector 引用）。
        if (_factory != null) _factory.CityPrefab = AICity;
    }

    // AI初始化（开局时调用）
    public void AIInit()
    {
        // 确保工厂已拿到城市预制体（AIInit 可能早于 Start 被 GameFlow 调用）
        if (_factory != null) _factory.CityPrefab = AICity;

        // 【探索重构-阶段7】初始化 AI 金币
        _goldWallet.InitPlayer(1);

        // 开局上方随机位置：选一个合法地块放城市
        List<HexCellData> candidates = new List<HexCellData>();
        foreach (HexCellData cell in _mapDataService.GetAllCells())
        {
            if (IsUnoccupiedLandCell(cell) &&
                cell.HexCoordinate.z >= 20f && cell.HexCoordinate.z <= 27f)
            {
                candidates.Add(cell);
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogError("AI initialization failed: no unoccupied land cells on the map.");
            return;
        }

        Vector3 AICityV = candidates[Random.Next(candidates.Count)].RealCenterWorldCoordinate;

        _factory.GenerateCity(AICityV);
        _cardBrain.InitializeCardState();

        // 【探索重构】AI 起始领地标记已探索（与玩家一致）
        MarkAITerritoryExplored(AICityV);
    }

    /// <summary>
    /// 将 AI 主城格 + 周围一环标记为已探索（AI 固有领地，无需花费资源）。
    /// </summary>
    private void MarkAITerritoryExplored(Vector3 cityWorldPos)
    {
        var centerCell = _mapDataService.GetCellByWorldPosition(cityWorldPos);
        if (centerCell == null) return;

        centerCell.ExploreThisHexCell();
        for (int i = 0; i < 6; i++)
        {
            var neighbor = _mapDataService.GetNeighbor(centerCell, (Enums.HexDirection)i);
            if (neighbor != null)
                neighbor.ExploreThisHexCell();
        }
    }

    private static bool IsUnoccupiedLandCell(HexCellData cell)
    {
        return cell != null &&
               cell.HexType != Enums.HexType.LakeOrSea &&
               cell.BulidingTypeOnHex_Building.Key == Enums.BulidingType.NoBuilding &&
               !cell.IsHaveUnit();
    }

    // ---------------- AI 回合行为 ----------------
    /// <summary>
    /// 【检查点 6】已停用并保留兼容性存根。AI 行为已全面由 AIUnitBrain + GameLoop 实时驱动。
    /// </summary>
    public IEnumerator ExecuteAITurn()
    {
        // 停用：AI 行为已改由 AIUnitBrain + GameLoop.Tick 实时驱动。
        // 原逐单位串行协程不再执行。
        yield break;
    }
}
