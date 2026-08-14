using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "UIConfig", menuName = "Game/UIConfig")]
public class UIConfigSO : ScriptableObject
{
    //移动指示器预制体
    public GameObject movementIndicatorPrefab;
    //敌方单位指示器预制体
    public GameObject enemyUnitIndicatorPrefab;
    //卡牌预制体
    public GameObject cardPrefab;
    //战术卡牌预制体
    public GameObject tacticalCardPrefab;
    //普通卡牌卡槽间隔（像素）
    [Tooltip("普通卡牌卡槽间隔（像素）")]
    public float cardSlotSpacing = 150f;
    //次卡槽与首张手牌之间额外增加的间隔（像素）
    [Min(0f)]
    [Tooltip("次卡槽与首张手牌之间额外增加的间隔（像素）；0 表示与普通卡牌等间距")]
    public float nextCardSlotGap = 0f;

    //移动力点数图标
    public Sprite movementPointsIcon;
    //攻击力点数图标
    public Sprite meleeAttackPointsIcon;

    public Sprite defenseIcon;   // 防御力图标
    public Sprite healthIcon;    // 血量图标

}
