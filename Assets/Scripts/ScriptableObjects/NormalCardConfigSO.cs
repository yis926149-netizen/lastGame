using UnityEngine;

//****************************************
//功能说明：普通卡配置统一基类（单位卡 / 建筑卡共用）。
//         普通卡池 NormalCardPoolSO 直接保存本类型的多态引用列表。
//****************************************
[CreateAssetMenu(fileName = "NormalCardConfig", menuName = "Game Data/Normal Cards/Normal Card Config")]
public abstract class NormalCardConfigSO : ScriptableObject
{
    [Tooltip("卡面图")]
    public Sprite cardSprite;
}
