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
        return true;   // 可添加“是否还有单位未操作”等限制
    }

    public void Exit()
    {
        CommandQueue.ExecuteAll(Enums.CommandQueueType.Unit);
    }
}