using System.Collections.Generic;

public class AIPlayerState
{
    public AICardState Card = new AICardState();
}

public class AICardState
{
    public const int MaxHandCards = 5;

    public List<int> HandCardIds = new List<int>();
    public int NextCardId = -1;
    public bool HasGivenFirstTurnSettler = false;
    public bool HasDealtThisTurn = false;
}
