using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TacticalCardDatabase", menuName = "Game Data/Tactical Cards/Tactical Card Database")]
public class TacticalCardDatabaseSO : ScriptableObject
{
    [Tooltip("所有战术卡牌定义")]
    public List<TacticalCardSO> cards = new();
}
