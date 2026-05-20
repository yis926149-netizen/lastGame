[System.Serializable]
public class UnitData
{
    //编号
    public int id;
    //名字
    public string unitName;
    //移动力
    public float MovementPoints;
    //血量
    public int hp;
    //攻击范围
    public int BasicAttackRange;
    //攻击力
    public int BasicAttackValue;
    //防御力
    public float Defense;
    //视野范围 - 判断效果如寻路
    public float ViewPoints;

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
}
