using System.Collections.Generic;

public class AIPlayerState
{
    public AICardState Card = new AICardState();

    /// <summary>上次 AI 操作时间戳（探索或出牌），用于 1 秒操作间隔</summary>
    public float LastActionTime;
}

public class AICardState
{
    // 【Excel 数值化】AI 手牌上限与玩家共享，迁移至 CoreGameplayConfigProvider。
    public static int MaxHandCards => CoreGameplayConfigProvider.HandCardLimit;

    /// <summary>AI 手牌（普通卡配置引用；允许同一配置多张，等价有放回抽取）。</summary>
    public List<NormalCardConfigSO> HandCards = new List<NormalCardConfigSO>();

    /// <summary>AI 预告牌（null 表示空）。</summary>
    public NormalCardConfigSO NextCard = null;

    public bool HasGivenFirstTurnSettler = false;
    public bool HasDealtThisTurn = false;
}
