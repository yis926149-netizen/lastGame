using System.Collections;
using System.Collections.Generic;
using System.Resources;
using Unity.VisualScripting;
using UnityEngine;
using Zenject;

//****************************************
//创建人：易生
//功能说明：
//****************************************

public class SettlementPhase : MonoBehaviour, IPhase
{
    //注入
    [Inject] private IMapDataService _mapDataService;
    [Inject] private IUnitRepository _unitRepository;
    [Inject] private ITechCultureService _techCultureService;


    public void Enter()
    {
        //CommandQueue.ExecuteAll(Enums.CommandQueueType.Settlement);

        //每回合自动回血：农田地貌
        List<GameObject> keysToRemove = new List<GameObject>();
        foreach (CharacterData c in _unitRepository.AllPlayerUnits.Values)
        {
            //农田地貌
            //位置判断
            if (c.model == null)
            {
                keysToRemove.Add(c.model);
                continue;
            }
            HexCellData h = _mapDataService.GetCellByWorldPosition(c.model.transform.position);
            if (h.landFormType != Enums.LandFormType.FromLand)
            {
                c.LandFormType_FromLand = 0;
                continue;
            }
            else 
            { 
                c.LandFormType_FromLand = 0.1f; 
            }
            //数据更新
            c.currentHp += c.LandFormType_FromLand * c.unitData.hp;
            //UI更新
            c.healthBar.value += c.LandFormType_FromLand;
        }

        if(keysToRemove.Count > 0)
        {
            foreach (GameObject obj in keysToRemove)
            {
                _unitRepository.RemovePlayerUnit(obj);
            }
        }

        //每回合自动回血：回血阵  
        foreach (CharacterData c in _unitRepository.AllPlayerUnits.Values)
        {
            HexCellData h = _mapDataService.GetCellByWorldPosition(c.model.transform.position);
            //回血阵建筑         
            if (h.BulidingTypeOnHex_Building.Key == Enums.BulidingType.Altar)
            {
                //数据更新
                //float f = h.BulidingTypeOnHex_Building.Value.GetComponent<BuildingData>().AltarValue;
                float f = h.BulidingTypeOnHex_Building.Value.GetComponent<BuildingController>().buildingData.AltarValue;
                c.currentHp += f * c.unitData.hp;
                //UI更新
                c.healthBar.value += f;
            }
        }

        //结算科技值、文化值
        _techCultureService.AddPointsPerTurn();
    }

    public bool CanExit() => true;

    public void Exit()
    {

    }
}
