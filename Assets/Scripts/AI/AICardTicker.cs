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
    private readonly AIConfigProvider _aiConfig;
    private float _timer;

    public AICardTicker(AICardBrain cardBrain, AIManager aiManager, AIPlayerState aiState, GameLoop gameLoop, AIConfigProvider aiConfig = null)
    {
        _cardBrain = cardBrain;
        _aiManager = aiManager;
        _aiState = aiState;
        _gameLoop = gameLoop;
        _aiConfig = aiConfig;
    }

    public void Tick()
    {
        if (_gameLoop != null && _gameLoop.IsPaused) return;
        if (_aiManager.AIDisabled) return;
        // 缩放时间基准：x2/x3 时 AI 动作间隔与抽卡节奏同步加速。
        // LastActionTime == 0 视为尚未动作（原 Time.time 基准下首次即放行），跳过间隔门控。
        if (_aiState.LastActionTime > 0f &&
            _gameLoop.GameTime - _aiState.LastActionTime < _aiConfig.GlobalActionMinInterval) return;
        _timer += _gameLoop != null ? _gameLoop.ScaledDeltaTime : UnityEngine.Time.deltaTime;
        if (_timer < _aiConfig.CardPlayInterval) return;
        _timer = 0f;
        if (_cardBrain.RunCardPipeline())
            _aiState.LastActionTime = _gameLoop != null ? _gameLoop.GameTime : UnityEngine.Time.time;
    }
}
