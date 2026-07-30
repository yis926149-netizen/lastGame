using UnityEngine;

public enum TalentEffectType
{
    StatMultiplier = 0,
    StatAddition = 1,
}

public enum TalentStatId
{
    damage = 0,
    defense = 1,
    gold = 2,
    buildingHp = 3,
}

[System.Serializable]
public struct TalentCardEffect
{
    [Tooltip("效果类型")]
    public TalentEffectType type;

    [Tooltip("目标属性ID")]
    public TalentStatId statId;

    [Tooltip("数值（乘数如 1.15 表示 +15%；加数如 1 表示 +1）")]
    public float value;

    public string GetStatIdString()
    {
        return statId.ToString();
    }
}

[CreateAssetMenu(fileName = "TalentCard", menuName = "Game Data/Talent Cards/Talent Card")]
public class TalentCardConfigSO : ScriptableObject
{
    [Header("显示")]
    [Tooltip("天赋卡唯一 ID")]
    public string talentId;

    [Tooltip("类型小图标（CardFrame 左上角 type 位置，60×60）")]
    public Sprite typeIcon;

    [Tooltip("卡牌主图（CardFrame 中央 main 位置，260×260）")]
    public Sprite mainIcon;

    [Tooltip("名称")]
    public string talentName;

    [Tooltip("描述")]
    [TextArea(3, 6)]
    public string description;

    [Header("效果")]
    [Tooltip("天赋效果")]
    public TalentCardEffect effect;
}
