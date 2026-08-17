using GameConfig;

//****************************************
//功能说明：天赋效果描述格式化器。
//         由效果数值（effectType/statId/value）生成精确文案，消除“+25% 与 ×4”这类文案/数值不一致。
//         Excel 数值未生成时回退 Legacy 人工文案。
//****************************************
public static class TalentDescriptionFormatter
{
    /// <summary>由 Excel 数值生成精确描述；balance 为 null 时返回空串。</summary>
    public static string Format(TalentCardBalanceData balance)
    {
        if (balance == null)
            return "";

        string stat = StatDisplayName(balance.statId);
        if (balance.effectType == "StatMultiplier")
            return $"{stat} ×{balance.value:0.#}";
        if (balance.effectType == "StatAddition")
            return $"{stat} +{balance.value:0.#}";

        return balance.description ?? "";
    }

    private static string StatDisplayName(string statId)
    {
        switch (statId)
        {
            case "damage": return "攻击力";
            case "defense": return "防御力";
            case "gold": return "金币获取";
            case "buildingHp": return "建筑生命值";
            default: return statId;
        }
    }
}
