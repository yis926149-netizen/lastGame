[System.Serializable]
public class UnitData
{
    //���
    public int id;
    //����
    public string unitName;
    //�ƶ���
    public float MovementPoints;
    //Ѫ��
    public int hp;
    //������Χ
    public int BasicAttackRange;
    //������
    public int BasicAttackValue;
    //������
    public float Defense;
    //��Ұ��Χ - �ж�Ч����Ѱ·
    public float ViewPoints;

    //【批次 D】攻速间隔（秒），两次攻击结算之间的冷却。默认 1.5f。
    public float AttackInterval = 1.5f;

    public UnitData(int id, string unitName, float MovementPoints, int hp, int attackRange, int attackValue, float viewPoints, float defense)
    {
        this.id = id;
        this.unitName = unitName;
        this.MovementPoints = MovementPoints;
        this.BasicAttackRange = attackRange;
        this.hp = hp;
        this.BasicAttackValue = attackValue;
        ViewPoints = viewPoints;
        Defense = defense;
    }

    public UnitData(UnitData source)
        : this(
            source.id,
            source.unitName,
            source.MovementPoints,
            source.hp,
            source.BasicAttackRange,
            source.BasicAttackValue,
            source.ViewPoints,
            source.Defense)
    {
        AttackInterval = source.AttackInterval;
    }
}
