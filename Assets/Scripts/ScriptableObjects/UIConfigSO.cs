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

    //移动力点数图标
    public Sprite movementPointsIcon;
    //攻击力点数图标
    public Sprite meleeAttackPointsIcon;

    public Sprite defenseIcon;   // 防御力图标
    public Sprite healthIcon;    // 血量图标

}