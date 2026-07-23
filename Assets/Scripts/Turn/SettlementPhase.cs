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


    public void Enter()
    {
        //每回合自动回血：农田地貌
        List<GameObject> keysToRemove = new List<GameObject>();
        foreach (var unit in _unitRepository.AllPlayerUnits)
        {
            CharacterData c = unit.Value;
            if (c == null || c.model == null)
            {
                keysToRemove.Add(unit.Key);
                continue;
            }
            HexCellData h = _mapDataService.GetCellByWorldPosition(c.model.transform.position);
            if (h == null)
            {
                continue;
            }
            if (h.landFormType != Enums.LandFormType.FromLand)
            {
                c.LandFormType_FromLand = 0;
                continue;
            }
            else
            {
                c.LandFormType_FromLand = 0.1f;
            }
            c.Heal(c.LandFormType_FromLand * c.unitData.hp);
        }

        if (keysToRemove.Count > 0)
        {
            foreach (GameObject obj in keysToRemove)
            {
                _unitRepository.RemovePlayerUnit(obj);
            }
        }

        //每回合自动回血：回血阵
        foreach (CharacterData c in _unitRepository.AllPlayerUnits.Values)
        {
            if (c == null || c.model == null) continue;

            HexCellData h = _mapDataService.GetCellByWorldPosition(c.model.transform.position);
            if (h == null) continue;

            GameObject altar = h.BulidingTypeOnHex_Building.Value;
            if (h.BulidingTypeOnHex_Building.Key == Enums.BulidingType.Altar &&
                altar != null &&
                altar.TryGetComponent<BuildingController>(out var controller) &&
                controller.buildingData != null)
            {
                float f = controller.buildingData.AltarValue;
                c.Heal(f * c.unitData.hp);
            }
        }
    }

    public bool CanExit() => true;

    public void Exit()
    {

    }
}
