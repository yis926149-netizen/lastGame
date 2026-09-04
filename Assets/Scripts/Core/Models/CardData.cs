using UnityEngine;

/// <summary>
/// 卡牌通用视图数据（普通卡与战术卡共用）。
/// 普通卡：NormalCardConfig 非空，ID/IsUnit/CardSprite 派生自配置。
/// 战术卡：NormalCardConfig 为空，ID 为负数槽位（-(slotIndex+1)），字段显式设置。
/// </summary>
public class CardData
{
    /// <summary>兼容/调试用 ID：普通卡=单位ID或建筑ID；战术卡=负数槽位ID。不再承担复合卡 ID 业务语义。</summary>
    public int ID { get; set; }
    public Sprite CardSprite { get; set; }

    /// <summary>金币不足时显示的灰版卡面；为 null 时不切图，沿用 CardSprite。战术卡不使用。</summary>
    public Sprite GrayCardSprite { get; set; }

    public bool IsUnit { get; set; }      // true=单位卡，false=建筑卡

    /// <summary>使用价格（默认 10），普通卡由配置派生，战术卡可显式设置。</summary>
    public int CardCost { get; set; } = 10;

    /// <summary>普通卡配置引用（单位/建筑）。战术卡为 null。</summary>
    public NormalCardConfigSO NormalCardConfig { get; set; }

    // 可扩展其他属性
}
