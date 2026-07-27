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
    [Inject] private PlayerModelManager _playerModelManager;
    [Inject] private AudioManager _audioManager;

    public bool isEndThisGame = false;
    public bool neverWin;
    public Transform VictoryAnimation;
    public Transform DefeatAnimation;
    public Transform VictoryUI;
    public Transform DefeatUI;
    public EndGameResult Result { get; private set; }

    public Transform CurrentEndAnimation =>
        Result == EndGameResult.Defeat ? DefeatAnimation : VictoryAnimation;

    public Transform CurrentEndUI =>
        Result == EndGameResult.Defeat ? DefeatUI : VictoryUI;

    private bool _initializationComplete;
    private bool _playerHasOwnedCity;



    void Update()
    {
        if (!_initializationComplete || isEndThisGame) return;

        if (_playerModelManager.CityCount > 0)
        {
            _playerHasOwnedCity = true;
        }

        Result = EvaluateResult(
            _initializationComplete,
            _playerHasOwnedCity,
            _playerModelManager.CityCount,
            AICityCount());
        if (neverWin && Result == EndGameResult.Victory)
        {
            Result = EndGameResult.None;
        }
        if (Result != EndGameResult.None)
        {
            isEndThisGame = true;
            Invoke(nameof(EndThisGame), 1.5f);
        }
    }

    public void MarkInitializationComplete()
    {
        _initializationComplete = true;
    }

    public static EndGameResult EvaluateResult(
        bool initializationComplete,
        bool playerHasOwnedCity,
        int playerCityCount,
        int aiCityCount)
    {
        if (!initializationComplete) return EndGameResult.None;
        if (playerHasOwnedCity && playerCityCount <= 0 && aiCityCount <= 0) return EndGameResult.Draw;
        if (aiCityCount <= 0) return EndGameResult.Victory;
        if (playerHasOwnedCity && playerCityCount <= 0) return EndGameResult.Defeat;
        return EndGameResult.None;
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

        var animation = CurrentEndAnimation;
        if (animation == null)
        {
            Debug.LogWarning($"[EndGame] {Result} animation is not configured; displaying the result UI immediately.", this);
            DisplayEndGameUI();
            return;
        }

        animation.gameObject.SetActive(true);
        animation.SetAsLastSibling();
        Invoke(nameof(DisplayEndGameUI), 6.5f);
    }

    private void DisplayEndGameUI()
    {
        var ui = CurrentEndUI;
        if (ui == null)
        {
            Debug.LogError($"[EndGame] {Result} UI is not configured.", this);
            return;
        }

        ui.gameObject.SetActive(true);
        ui.SetAsLastSibling();

        var animation = CurrentEndAnimation;
        if (animation != null)
        {
            animation.gameObject.SetActive(false);
        }
    }

    public void HideEndUI()
    {
        var animation = CurrentEndAnimation;
        if (animation != null)
        {
            animation.gameObject.SetActive(false);
        }

        var ui = CurrentEndUI;
        if (ui != null)
        {
            ui.gameObject.SetActive(false);
        }
    }

    private void OnDisable()
    {
        CancelInvoke();
    }
}

public enum EndGameResult
{
    None,
    Victory,
    Defeat,
    Draw
}
