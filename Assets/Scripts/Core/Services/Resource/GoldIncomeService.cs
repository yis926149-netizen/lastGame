using Zenject;

public class GoldIncomeService : ITickable
{
    private readonly GoldWallet _wallet;
    private readonly IFactionBuffService _factionBuff;
    private readonly GameLoop _gameLoop;
    private readonly IMapDataService _mapDataService;
    private readonly ILogisticsService _logisticsService;
    private readonly BuildingDatabaseSO _buildingDatabase;
    private float _accumulator;

    public float IncomeInterval { get; set; } = 1f;

    /// <summary>AI 专属额外金币收入（每结算周期）。固定值、不参与天赋倍率放大，用于平滑增强 AI 竞争力。</summary>
    public int AIIncomeBonusPerTick { get; set; } = 6;

    public GoldIncomeService(
        GoldWallet wallet,
        IFactionBuffService factionBuff,
        GameLoop gameLoop,
        IMapDataService mapDataService,
        ILogisticsService logisticsService,
        BuildingDatabaseSO buildingDatabase = null)
    {
        _wallet = wallet;
        _factionBuff = factionBuff;
        _gameLoop = gameLoop;
        _mapDataService = mapDataService;
        _logisticsService = logisticsService;
        _buildingDatabase = buildingDatabase;
    }

    public void Tick()
    {
        if (_gameLoop != null && _gameLoop.IsPaused) return;

        _accumulator += UnityEngine.Time.deltaTime;
        if (_accumulator < IncomeInterval) return;
        _accumulator -= IncomeInterval;

        _wallet.AddGold(0, GetIncomePerTick(0));
        _wallet.AddGold(1, GetIncomePerTick(1));
    }

    /// <summary>
    /// 获取阵营当前每次被动结算的真实收入：
    /// （基础收入 + 已占领金矿地貌加成 + 金矿建筑收入）× 金币天赋乘数。
    /// 结算与 HUD 共用此方法，避免界面仍只显示基础收入。
    /// </summary>
    public int GetIncomePerTick(int factionId)
    {
        // 【断供方案-阶段6.5】金矿加成只统计"归属 + 后勤畅通"的地块：
        // 断供地区的金矿暂停产金，恢复供应后自动恢复；HUD 与结算共用此方法。
        float mineBonus = LandFormEffectRule.SumGoldIncomeBonus(
            _mapDataService?.GetAllCells(), factionId, _logisticsService);
        float buildingMineIncome = BuildingIncomeRule.SumGoldMineIncome(
            _mapDataService?.GetAllCells(), factionId, _buildingDatabase, _logisticsService);
        float multiplier = _factionBuff != null
            ? _factionBuff.GetStatMultiplier(factionId, "gold")
            : 1f;

        return UnityEngine.Mathf.RoundToInt(
            (_wallet.PassiveIncomePerTick + mineBonus + buildingMineIncome) * multiplier)
            + (factionId == 1 ? AIIncomeBonusPerTick : 0);
    }
}
