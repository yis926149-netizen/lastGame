using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class GameFlowManager : MonoBehaviour, IInitializable
{
    
    [Inject] private MapRenderer mapRenderer;
    [Inject] private MapGenerator mapGenerator;
    [Inject] private IMapDataService _mapDataService;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private IAIManager _iaiManager;
    [Inject] private AudioManager _audioManager;
    [Inject] private EndGame _endGame;

    public void Initialize()
    {
        _audioManager.PlayBGM("Theme_Mistery_But_Then_Happy_Loop");
        mapGenerator.Generate();
        mapRenderer.MapRender();
        _iaiManager.AIInit();
        PlayerInit();
        _endGame.MarkInitializationComplete();
    }


    public void PlayerInit()
    {
        //玩家初始化：随机一个非水、无城的陆地格
        System.Random random = SeedService.GetRandom("Player");

        var candidates = new List<HexCellData>();
        foreach (HexCellData cell in _mapDataService.GetAllCells())
        {
            if (cell != null &&
                cell.HexType != Enums.HexType.LakeOrSea &&
                cell.Player_City_Index.Equals(new KeyValuePair<int, int>(-1, -1)))
            {
                candidates.Add(cell);
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogError("玩家初始化失败：地图上没有可用的陆地格（水域阈值过高或高度范围过低）。");
            return;
        }

        HexCellData h = candidates[random.Next(candidates.Count)];

        mapGenerator.SpawnHexCenterPoint = h.RealCenterWorldCoordinate;
        h.ExploreThisHexCell();

        for (int i = 0; i < 6; i++)
        {
            //Debug.Log("111");
            if (_mapDataService.GetNeighbor(h, (Enums.HexDirection)i) != null)
            {
                _mapDataService.GetNeighbor(h, (Enums.HexDirection)i).ExploreThisHexCell();
            }

        }

        _mapVisualEvent.Raise();

    }
}
