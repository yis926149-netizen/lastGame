public class TacticalCardInstance
{
    public TacticalCardSO Config;
    public int Quantity;

    public bool IsEmpty => Quantity <= 0;
}
