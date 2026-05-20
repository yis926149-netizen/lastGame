using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class GameFlowManager : MonoBehaviour
{
    
    [Inject] private MapRenderer mapRenderer;
    [Inject] private MapGenerator mapGenerator;
    [Inject] private IMapDataService _mapDataService;
    [Inject] private MapVisualEventSO _mapVisualEvent;
    [Inject] private IAIManager _iaiManager;
    [Inject] private AudioManager _audioManager;

    void Start()
    {
        _audioManager.PlayBGM("Theme_Mistery_But_Then_Happy_Loop");
        mapGenerator.Generate();
        mapRenderer.MapRender();
        _iaiManager.AIInit();
        PlayerInit();
    }


    public void PlayerInit()
    {
        //玩家初始化
        //随机添加一环视野
        System.Random random = new System.Random();
        HexCellData h;
        while (true)
        {
            int j = random.Next(_mapDataService.GetAllCells().Count); // 生成[0, Count)

            h = _mapDataService.GetCell(j);

            if (
                //不能出生在海里
                h.HexType != Enums.HexType.LakeOrSea &&
                //只能出生在无主之地
                h.Player_City_Index.Equals(new KeyValuePair<int, int>(-1, -1))
                )
            { break; }
        }

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
