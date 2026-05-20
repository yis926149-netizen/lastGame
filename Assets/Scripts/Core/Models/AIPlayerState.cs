using System.Collections.Generic;

public class AIPlayerState
{
    public AICardState Card = new AICardState();
    public AITechCultureState TechCulture = new AITechCultureState();
}

public class AICardState
{
    public const int MaxHandCards = 5;

    public List<int> HandCardIds = new List<int>();
    public int NextCardId = -1;
    public bool HasGivenFirstTurnSettler = false;
    public bool HasDealtThisTurn = false;
}

public class AITechCultureState
{
    public float TechPointsPerTurn = 0f;
    public float CulturePointsPerTurn = 0f;

    public float TechAccumulatedPoints = 0f;
    public float CultureAccumulatedPoints = 0f;

    public int TechLevel = 0;
    public int CultureLevel = 0;
}
