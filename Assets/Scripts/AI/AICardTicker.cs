using Zenject;

/// <summary>
/// AI 卡牌定时器：每 N 秒调用一次卡牌管线，驱动 AI 抽卡和出牌。
/// 与 AIAutoExplorer 共享 LastActionTime，确保 AI 各操作间隔至少 1 秒。
/// </summary>
public class AICardTicker : ITickable
{
    private readonly AICardBrain _cardBrain;
    private readonly AIManager _aiManager;
    private readonly AIPlayerState _aiState;
    private readonly GameLoop _gameLoop;
    private float _timer;
    private const float CardInterval = 1.5f;

    public AICardTicker(AICardBrain cardBrain, AIManager aiManager, AIPlayerState aiState, GameLoop gameLoop)
    {
        _cardBrain = cardBrain;
        _aiManager = aiManager;
        _aiState = aiState;
        _gameLoop = gameLoop;
    }

    public void Tick()
    {
        if (_gameLoop != null && _gameLoop.IsPaused) return;
        if (_aiManager.AIDisabled) return;
        if (UnityEngine.Time.time - _aiState.LastActionTime < 1f) return;
        _timer += UnityEngine.Time.deltaTime;
        if (_timer < CardInterval) return;
        _timer = 0f;
        if (_cardBrain.RunCardPipeline())
            _aiState.LastActionTime = UnityEngine.Time.time;
    }
}
