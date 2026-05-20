using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "UnitDatabase", menuName = "Game/UnitDatabase")]
public class UnitDatabaseSO : ScriptableObject
{
    [Header("单位模型预制体（索引与ID一致）")]
    public List<GameObject> unitModels;

    [Header("单位数据")]
    public List<UnitData> unitDatas;

    [Header(" -    单位卡面")]
    public List<Sprite> Cards;

    [Header("单位图标")]
    public List<Sprite> unitIcons;

    [Header("技能图标")]
    public List<Sprite> skillIcons;
}