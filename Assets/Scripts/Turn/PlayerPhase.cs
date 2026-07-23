using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

//****************************************
//创建人：易生
//功能说明：
//****************************************
public interface IPhase
{
    void Enter();
    bool CanExit();
    void Exit();
}

public class PlayerPhase : MonoBehaviour, IPhase
{
    [Inject] private IUnitRepository _unitRepository;

    public void Enter()
    {
        // 恢复所有玩家单位的移动力
        foreach (var kv in _unitRepository.AllPlayerUnits)
        {
            if (kv.Value != null && kv.Value.unitMovementController != null)
            {
                kv.Value.unitMovementController.RestoreUnitMovementStandbyParameters();
            }
        }

    }

    public bool CanExit()
    {
        foreach (var unit in _unitRepository.AllPlayerUnits.Values)
        {
            if (unit?.unitMovementController != null && unit.unitMovementController.IsBusy)
            {
                return false;
            }
        }
        return true;
    }

    public void Exit()
    {
        CommandQueue.ExecuteAll(Enums.CommandQueueType.Unit);
    }
}
