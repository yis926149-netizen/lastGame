using System.Collections.Generic;
using UnityEngine;

//****************************************
//功能说明：普通卡池（玩家与 AI 共享）。与天赋卡 TalentCardPoolSO 同方向的普通卡对象化。
//         卡池内容完全由 cards 决定；首张保底卡（单位卡或建筑卡）由 guaranteedFirstCard 配置。
//****************************************
[CreateAssetMenu(fileName = "NormalCardPool", menuName = "Game Data/Normal Cards/Normal Card Pool")]
public class NormalCardPoolSO : ScriptableObject
{
    [Tooltip("普通卡池（所有可抽取的普通卡：单位卡 + 建筑卡）")]
    public List<NormalCardConfigSO> cards = new();

    [Tooltip("首张保底卡（单位卡或建筑卡均可），替代 CardGenerationRule 中的 return 0")]
    public NormalCardConfigSO guaranteedFirstCard;
}
