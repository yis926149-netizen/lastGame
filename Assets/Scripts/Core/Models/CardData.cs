using UnityEngine;

public class CardData
{
    public int ID { get; set; }
    public Sprite CardSprite { get; set; }
    public bool IsUnit { get; set; }      // true=单位卡，false=建筑卡

    // 可扩展其他属性

}