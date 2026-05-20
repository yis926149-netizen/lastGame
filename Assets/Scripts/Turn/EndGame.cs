using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

//****************************************
//创建人：易生
//功能说明：
//****************************************

public class EndGame : MonoBehaviour
{
    [Inject] private EnemyModelManager _enemyModelManager;
    [Inject] private AudioManager _audioManager;

    //测试用
    public bool isEndThisGame = false;
    public Transform EndAnimation;
    public Transform EndUI;



    void Update()
    {
        //Debug.Log("AICityCount：" + AICityCount());
        if (AICityCount() == 0 && !isEndThisGame)
        {
            isEndThisGame = true;
            Invoke("EndThisGame", 1.5f);
        }


    }

    private int AICityCount()
    {
        int AICityCount = 0;

        foreach(int i in _enemyModelManager.CityCount.Values)
        {
            AICityCount += i;
        }
        
        return AICityCount;
    }

    public void EndThisGame()
    {
        _audioManager.StopBGM();

        EndAnimation.gameObject.SetActive(true);
        EndAnimation.SetAsLastSibling();
        Invoke("DisplayEndGameUI", 6.5f);
    }

    private void DisplayEndGameUI()
    {
        EndUI.gameObject.SetActive(true);
        EndUI.SetAsLastSibling();
        EndAnimation.gameObject.SetActive(false);
    }
}
