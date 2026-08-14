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
    [Inject] private GameLoop _gameLoop;
    [Inject] private IMapDataService _mapDataService;
    [Inject] private GlobalTimerService _globalTimer;
    [Inject] private ArenaEventManager _arenaEventManager;

    public bool isEndThisGame = false;
    public bool neverWin;
    public Transform VictoryAnimation;
    public Transform DefeatAnimation;
    public Transform VictoryUI;
    public Transform DefeatUI;
    public EndGameResult Result { get; private set; }

    public bool IsVictory =>
        Result == EndGameResult.Victory || Result == EndGameResult.Draw;

    public Transform CurrentEndAnimation =>
        IsVictory ? VictoryAnimation : DefeatAnimation;

    public Transform CurrentEndUI =>
        IsVictory ? VictoryUI : DefeatUI;

    private bool _initializationComplete;
    private bool _playerHasOwnedCity;
    private BuildingController _playerMainCity;
    private BuildingController _aiMainCity;

    private void Start()
    {
        if (_globalTimer != null)
            _globalTimer.OnTimeout += HandleTimeout;
    }

    private void HandleTimeout()
    {
        if (!_initializationComplete || isEndThisGame) return;

        int playerCells = 0;
        int aiCells = 0;

        foreach (var cell in _mapDataService.GetAllCells())
        {
            if (cell == null) continue;
            int owner = cell.Player_City_Index.Key;
            if (owner == 0) playerCells++;
            else if (owner >= 1) aiCells++;
        }

        EndGameResult result;
        if (playerCells > aiCells)
            result = EndGameResult.Victory;
        else if (aiCells > playerCells)
            result = EndGameResult.Defeat;
        else
            result = EndGameResult.Draw;

        Debug.Log($"[EndGame] Timeout: Player={playerCells} AI={aiCells} → {result}");
        BeginEndGame(result);
    }



    void Update()
    {
        if (!_initializationComplete || isEndThisGame) return;

        if (_playerMainCity != null && _aiMainCity != null)
        {
            Result = EvaluateMainCityHealth(
                _playerMainCity.buildingData?.currentHp ?? 0f,
                _aiMainCity.buildingData?.currentHp ?? 0f);
            if (Result != EndGameResult.None)
            {
                BeginEndGame(Result);
            }
            return;
        }

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
            BeginEndGame(Result);
        }
    }

    public void RegisterMainCity(int playerIndex, BuildingController controller)
    {
        if (controller == null) return;

        if (playerIndex == 0)
            _playerMainCity = controller;
        else if (playerIndex == 1)
            _aiMainCity = controller;
    }

    public bool TryEndFromMainCity(BuildingController controller)
    {
        if (!_initializationComplete || isEndThisGame || controller == null)
            return false;
        if (controller != _playerMainCity && controller != _aiMainCity)
            return false;

        EndGameResult result = EvaluateMainCityHealth(
            _playerMainCity?.buildingData?.currentHp ?? 0f,
            _aiMainCity?.buildingData?.currentHp ?? 0f);
        if (result == EndGameResult.None) return false;
        if (neverWin && result == EndGameResult.Victory) return false;

        BeginEndGame(result);
        return true;
    }

    public static EndGameResult EvaluateMainCityHealth(float playerHp, float aiHp)
    {
        bool playerDestroyed = playerHp <= 0f;
        bool aiDestroyed = aiHp <= 0f;
        if (playerDestroyed && aiDestroyed) return EndGameResult.Draw;
        if (aiDestroyed) return EndGameResult.Victory;
        if (playerDestroyed) return EndGameResult.Defeat;
        return EndGameResult.None;
    }

    private void BeginEndGame(EndGameResult result)
    {
        if (isEndThisGame || result == EndGameResult.None) return;
        if (neverWin && result == EndGameResult.Victory) return;

        Result = result;
        isEndThisGame = true;
        Invoke(nameof(EndThisGame), 1.5f);
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
        _gameLoop.SetPaused(true);
        _audioManager.StopBGM();

        // 【竞技场-阶段二】对局结束强制收尾：释放 VisibilityLease、注销并销毁宝箱
        _arenaEventManager?.Shutdown();

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
        _gameLoop.SetPaused(false);

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
        if (_globalTimer != null)
            _globalTimer.OnTimeout -= HandleTimeout;
    }
}

public enum EndGameResult
{
    None,
    Victory,
    Defeat,
    Draw
}
