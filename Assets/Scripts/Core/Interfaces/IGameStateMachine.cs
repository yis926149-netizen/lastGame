public interface IGameStateMachine
{
    int CurrentTurn { get; }
    IPhase CurrentPhase { get; }
    void StartGame();                // 游戏开始时调用
    void EndTurn();                  // 玩家点击“结束回合”时调用
}