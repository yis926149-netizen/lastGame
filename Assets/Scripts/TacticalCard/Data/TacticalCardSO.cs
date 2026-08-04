using UnityEngine;

[CreateAssetMenu(fileName = "TacticalCard", menuName = "Game Data/Tactical Cards/Tactical Card")]
public class TacticalCardSO : ScriptableObject
{
    [Header("显示")]
    [Tooltip("唯一 ID")]
    public string cardId;

    [Tooltip("名称")]
    public string cardName;

    [Tooltip("描述")]
    [TextArea(3, 6)]
    public string description;

    [Tooltip("卡面图")]
    public Sprite cardSprite;

    [Header("效果")]
    [Tooltip("效果类型")]
    public TacticalEffectType effectType;

    [Tooltip("效果参数")]
    public TacticalCardEffect effect;
}
