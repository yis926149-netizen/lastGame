using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TechTreeIcons", menuName = "Game/TechTreeIcons")]
public class TechTreeIconsSO : ScriptableObject
{
    public List<Sprite> techIcons;
    public List<Sprite> cultureIcons;
}