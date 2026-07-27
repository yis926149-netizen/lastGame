public class Buff
{
    public string id;

    public virtual float GetStatMultiplier(string statId) => 1f;
    public virtual float GetStatAddition(string statId) => 0f;
}
