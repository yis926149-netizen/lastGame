public class StatAdditiveBuff : Buff
{
    private readonly string _statId;
    private readonly float _value;

    public StatAdditiveBuff(string id, string statId, float value)
    {
        this.id = id;
        _statId = statId;
        _value = value;
    }

    public override float GetStatAddition(string statId) =>
        statId == _statId ? _value : 0f;
}
