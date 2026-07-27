using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TalentCardPool", menuName = "Game Data/Talent Cards/Talent Card Pool")]
public class TalentCardPoolSO : ScriptableObject
{
    [Tooltip("天赋卡池（所有可抽取的天赋卡）")]
    public List<TalentCardConfigSO> cards = new();
}
