using UnityEngine;

//****************************************
//功能说明：AI 科技文化推进。持有共享 AIPlayerState，负责每回合累积推进与即时加点。
//         逻辑与拆分前 AIManager.ApplyTechCultureProgress / AddInstantTechCulturePoints 完全一致。
//****************************************

public class AITechCultureProgress
{
    private readonly AIPlayerState _aiPlayerState;
    private readonly TechData _techData;
    private readonly CultureData _cultureData;

    public AITechCultureProgress(AIPlayerState aiPlayerState, TechData techData, CultureData cultureData)
    {
        _aiPlayerState = aiPlayerState;
        _techData = techData;
        _cultureData = cultureData;
    }

    /// <summary>每回合推进：累积值按每回合产量推进，达阈值升级（与玩家 AddPointsPerTurn 一致）。</summary>
    public void AdvancePerTurn()
    {
        int techMaxLevel = Mathf.Max(0, _techData.TechCost.Count - 1);
        if (_aiPlayerState.TechCulture.TechLevel < _techData.TechCost.Count)
        {
            float techCost = _techData.TechCost[_aiPlayerState.TechCulture.TechLevel];
            if (techCost > 0)
            {
                _aiPlayerState.TechCulture.TechAccumulatedPoints =
                    Mathf.Min(1f, _aiPlayerState.TechCulture.TechAccumulatedPoints + _aiPlayerState.TechCulture.TechPointsPerTurn / techCost);
            }
            if (_aiPlayerState.TechCulture.TechAccumulatedPoints >= 1f && _aiPlayerState.TechCulture.TechLevel < techMaxLevel)
            {
                _aiPlayerState.TechCulture.TechLevel++;
                _aiPlayerState.TechCulture.TechAccumulatedPoints = 0f;
            }
        }

        int cultureMaxLevel = Mathf.Max(0, _cultureData.CultureCost.Count - 1);
        if (_aiPlayerState.TechCulture.CultureLevel < _cultureData.CultureCost.Count)
        {
            float cultureCost = _cultureData.CultureCost[_aiPlayerState.TechCulture.CultureLevel];
            if (cultureCost > 0)
            {
                _aiPlayerState.TechCulture.CultureAccumulatedPoints =
                    Mathf.Min(1f, _aiPlayerState.TechCulture.CultureAccumulatedPoints + _aiPlayerState.TechCulture.CulturePointsPerTurn / cultureCost);
            }
            if (_aiPlayerState.TechCulture.CultureAccumulatedPoints >= 1f && _aiPlayerState.TechCulture.CultureLevel < cultureMaxLevel)
            {
                _aiPlayerState.TechCulture.CultureLevel++;
                _aiPlayerState.TechCulture.CultureAccumulatedPoints = 0f;
            }
        }
    }

    /// <summary>即时加点（如开箱奖励）。</summary>
    public void AddInstant(float techPoints, float culturePoints)
    {
        if (_techData.TechCost.Count > 0 && _aiPlayerState.TechCulture.TechLevel < _techData.TechCost.Count)
        {
            float cost = _techData.TechCost[_aiPlayerState.TechCulture.TechLevel];
            if (cost > 0)
            {
                _aiPlayerState.TechCulture.TechAccumulatedPoints =
                    Mathf.Min(1f, _aiPlayerState.TechCulture.TechAccumulatedPoints + techPoints / cost);
            }
        }

        if (_cultureData.CultureCost.Count > 0 && _aiPlayerState.TechCulture.CultureLevel < _cultureData.CultureCost.Count)
        {
            float cost = _cultureData.CultureCost[_aiPlayerState.TechCulture.CultureLevel];
            if (cost > 0)
            {
                _aiPlayerState.TechCulture.CultureAccumulatedPoints =
                    Mathf.Min(1f, _aiPlayerState.TechCulture.CultureAccumulatedPoints + culturePoints / cost);
            }
        }
    }
}
