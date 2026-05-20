using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "EnvironmentModels", menuName = "Game/EnvironmentModels")]
public class EnvironmentModelsSO : ScriptableObject
{
    public List<GameObject> landFormModels;      // °´ Enums.LandFormType Ë³Ðò
    public List<GameObject> resourceModels;       // °´ Enums.ResourceType Ë³Ðò
    public GameObject reapAnimalsEffect;
    public GameObject reapPlantsEffect;
    public GameObject reapMineralsEffect;
    public GameObject reapChestEffect;
}