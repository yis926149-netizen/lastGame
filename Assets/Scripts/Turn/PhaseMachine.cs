using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;


//****************************************
//创建人：易生
//功能说明：
//****************************************

public class PhaseMachine : MonoBehaviour
{
    [Inject] private IGameStateMachine _gameStateMachine;

    private List<IPhase> phases;
    private int index;

    public PhaseMachine(List<IPhase> phaseList)
    {
        phases = phaseList;
    }

    public void StartPhases()
    {
        index = 0;
        phases[index].Enter();
    }

    public void NextPhase()
    {
        if (!phases[index].CanExit()) return;

        phases[index].Exit();
        index++;

        if (index >= phases.Count)
        {
            _gameStateMachine?.EndTurn();
            return;
        }

        phases[index].Enter();
    }
}
