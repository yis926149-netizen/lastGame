using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

//****************************************
//创建人：易生
//功能说明：AI 管理器（协调者）。持有 AI 状态与场景引用，编排开局初始化与每回合流程；
//         具体逻辑委托给 AIEntityFactory / AICardBrain / AITacticalBrain / AITechCultureProgress。
//****************************************

public class AIManager : MonoBehaviour, IAIManager
{
    [Inject] private IMapDataService _mapDataService;
    [Inject] private IUnitRepository _unitRepository;
    [Inject] private AIEntityFactory _factory;
    [Inject] private AICardBrain _cardBrain;
    [Inject] private AITacticalBrain _tacticalBrain;
    [Inject] private AIRandomProvider _rng;

    // 城市预制体：Inspector 序列化引用，注入后转交工厂（场景引用无法直接注入普通类）。
    public GameObject AICity;

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

        // 开局纯随机位置：收集所有可选地块，独立随机选两个不同地块放单位与城市
        List<HexCellData> candidates = new List<HexCellData>();
        foreach (HexCellData cell in _mapDataService.GetAllCells())
        {
            if (IsUnoccupiedLandCell(cell))
            {
                candidates.Add(cell);
            }
        }

        if (candidates.Count < 2)
        {
            Debug.LogError("AI initialization failed: less than 2 unoccupied land cells on the map.");
            return;
        }

        int idx1 = Random.Next(candidates.Count);
        int idx2;
        do
        {
            idx2 = Random.Next(candidates.Count);
        } while (idx2 == idx1);

        Vector3 AIUnitV = candidates[idx1].RealCenterWorldCoordinate;
        Vector3 AICityV = candidates[idx2].RealCenterWorldCoordinate;

        _factory.GenerateUnit(1, AIUnitV);
        _factory.GenerateCity(AICityV);
        _cardBrain.InitializeCardState();
    }

    private static bool IsUnoccupiedLandCell(HexCellData cell)
    {
        return cell != null &&
               cell.HexType != Enums.HexType.LakeOrSea &&
               cell.BulidingTypeOnHex_Building.Key == Enums.BulidingType.NoBuilding &&
               !cell.IsHaveUnit();
    }

    // ---------------- AI 回合行为 ----------------
    /// <summary>在 AI 回合被调用：先跑卡牌管线，再依次处理每个敌方单位的行动。</summary>
    public IEnumerator ExecuteAITurn()
    {
        _cardBrain.RunCardPipeline();

        // 从仓库收集所有敌方单位（扁平化）
        List<CharacterData> enemyUnits = new List<CharacterData>();
        foreach (var group in _unitRepository.AllEnemyUnitGroups)
        {
            enemyUnits.AddRange(group.Values);
        }

        // 重置每个单位移动力为待机值
        foreach (var cd in enemyUnits)
        {
            if (cd?.unitMovementController != null)
            {
                cd.unitMovementController.RestoreUnitMovementStandbyParameters();
            }
        }

        if (enemyUnits.Count == 0) yield break;

        // 逐单位执行决策并等待其动作完成
        foreach (var cd in enemyUnits)
        {
            if (cd == null || cd.model == null) continue;
            if (cd.currentHp <= 0) continue;

            yield return StartCoroutine(_tacticalBrain.HandleSingleUnitTurn(cd));

            yield return new WaitForSeconds(0.1f);
        }
    }
}
