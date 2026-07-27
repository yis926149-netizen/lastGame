using Zenject;

public class GoldIncomeService : ITickable
{
    private readonly GoldWallet _wallet;
    private readonly IFactionBuffService _factionBuff;
    private readonly GameLoop _gameLoop;
    private float _accumulator;

    public float IncomeInterval { get; set; } = 1f;

    public GoldIncomeService(GoldWallet wallet, IFactionBuffService factionBuff, GameLoop gameLoop)
    {
        _wallet = wallet;
        _factionBuff = factionBuff;
        _gameLoop = gameLoop;
    }

    public void Tick()
    {
        if (_gameLoop != null && _gameLoop.IsPaused) return;

        _accumulator += UnityEngine.Time.deltaTime;
        if (_accumulator < IncomeInterval) return;
        _accumulator -= IncomeInterval;

        int playerIncome = UnityEngine.Mathf.RoundToInt(
            _wallet.PassiveIncomePerTick * _factionBuff.GetStatMultiplier(0, "gold"));
        int aiIncome = UnityEngine.Mathf.RoundToInt(
            _wallet.PassiveIncomePerTick * _factionBuff.GetStatMultiplier(1, "gold"));

        _wallet.AddGold(0, playerIncome);
        _wallet.AddGold(1, aiIncome);
    }
}
