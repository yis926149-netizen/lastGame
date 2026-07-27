using System.Collections.Generic;

public class AIPlayerState
{
    public AICardState Card = new AICardState();

    /// <summary>上次 AI 操作时间戳（探索或出牌），用于 1 秒操作间隔</summary>
    public float LastActionTime;
}

public class AICardState
{
    public const int MaxHandCards = 5;

    public List<int> HandCardIds = new List<int>();
    public int NextCardId = -1;
    public bool HasGivenFirstTurnSettler = false;
    public bool HasDealtThisTurn = false;
}
