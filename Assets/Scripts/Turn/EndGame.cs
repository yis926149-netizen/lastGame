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
    [Inject] private GameFlowConfigProvider _gameFlow;

    public bool forceVictory = false;   // 强制胜利开关：Play 模式下勾选，下一帧强制触发胜利结算（触发后自动复位）
    public bool forceDefeat = false;    // 强制失败开关：Play 模式下勾选，下一帧强制触发失败结算（触发后自动复位）
    public bool neverWin;
    private bool _ended;                  // 内部“已结算”锁，防止重复触发/重复结算
    public Transform VictoryAnimation;
    public Transform DefeatAnimation;
    public Transform VictoryUI;
    public Transform DefeatUI;

    [Tooltip("胜利界面的缩放入场动画播放器（ScaleInEffectPlayer 组件），仅在胜利时播放列表第 0-6 个动画")]
    public ScaleInEffectPlayer victoryScaleIn;

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
        if (!_initializationComplete || _ended) return;

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
        if (!_initializationComplete || _ended) return;

        if (forceVictory)
        {
            forceVictory = false;
            BeginEndGame(EndGameResult.Victory, force: true);
            return;
        }

        if (forceDefeat)
        {
            forceDefeat = false;
            BeginEndGame(EndGameResult.Defeat, force: true);
            return;
        }

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
        if (!_initializationComplete || _ended || controller == null)
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

    private void BeginEndGame(EndGameResult result, bool force = false)
    {
        if (_ended || result == EndGameResult.None) return;
        if (!force && neverWin && result == EndGameResult.Victory) return;

        Result = result;
        _ended = true;
        Invoke(nameof(EndThisGame), _gameFlow.SettlementDelaySeconds);
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

    [ContextMenu("Debug/Force Victory")]
    private void DebugForceVictory()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[EndGame] 请在 Play 模式下使用 Force Victory。", this);
            return;
        }
        BeginEndGame(EndGameResult.Victory, force: true);
    }

    [ContextMenu("Debug/Force Defeat")]
    private void DebugForceDefeat()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[EndGame] 请在 Play 模式下使用 Force Defeat。", this);
            return;
        }
        BeginEndGame(EndGameResult.Defeat);
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
        Invoke(nameof(DisplayEndGameUI), _gameFlow.EndGameUiDelaySeconds);
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

        // 缩放入场动画：胜利 UI 激活后同时播放列表第 0-6 个动画（仅胜利界面独有）
        if (IsVictory) victoryScaleIn?.Play(0, 1, 2, 3, 4, 5, 6);

        var animation = CurrentEndAnimation;
        if (animation != null)
        {
            animation.gameObject.SetActive(false);
        }
    }

    public void HideEndUI()
    {
        _gameLoop.SetPaused(false);

        // 取消尚未触发的延迟缩放入场动画，避免玩家继续游戏后 A 节点被意外激活
        victoryScaleIn?.Stop(0, 1, 2, 3, 4, 5, 6);

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
