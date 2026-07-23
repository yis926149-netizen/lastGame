using System.Collections.Generic;

//****************************************
//功能说明：抽卡生成规则——玩家与 AI 共享的单一实现。
//         此前 CardService.GenerateNextCardID 与 AICardBrain.GenerateCardId 是镜像重复，
//         真正重复的核心是"取解锁池 → 随机抽一张 → 空池回退 0"。此处收敛为单一来源，杜绝漂移。
//         首张移民卡保底的"时机"两侧不同（玩家=回合1；AI=开局），故作为参数由调用方传入，
//         语义上都表示"开局首次发牌保底移民卡"。
//****************************************

public static class CardGenerationRule
{
    /// <summary>生成下一张卡的 ID。</summary>
    /// <param name="giveFirstSettler">当前是否处于"应保底发移民卡"的时机（玩家=回合1；AI=开局 true）。</param>
    /// <param name="hasGivenFirstSettler">是否已发过保底移民卡（按引用读写；各方各自持有该标志）。</param>
    public static int GenerateNextCardId(
        bool giveFirstSettler,
        ref bool hasGivenFirstSettler,
        int techLevel,
        int cultureLevel,
        ICardUnlockRuleProvider unlockProvider,
        System.Random random)
    {
        if (giveFirstSettler && !hasGivenFirstSettler)
        {
            hasGivenFirstSettler = true;
            return 0; // 移民卡
        }

        List<int> unlockedIds = unlockProvider.GetUnlockedCardIds(techLevel, cultureLevel);
        if (unlockedIds == null || unlockedIds.Count == 0) return 0;

        return unlockedIds[random.Next(unlockedIds.Count)];
    }
}
