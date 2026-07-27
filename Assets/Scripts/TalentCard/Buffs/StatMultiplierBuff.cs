public class StatMultiplierBuff : Buff
{
    private readonly string _statId;
    private readonly float _multiplier;

    public StatMultiplierBuff(string id, string statId, float multiplier)
    {
        this.id = id;
        _statId = statId;
        _multiplier = multiplier;
    }

    public override float GetStatMultiplier(string statId) =>
        statId == _statId ? _multiplier : 1f;
}
