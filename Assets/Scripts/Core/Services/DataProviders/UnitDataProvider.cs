using UnityEngine;

public interface IUnitDataProvider
{
    GameObject GetUnitPrefab(int unitId);
    UnitData GetUnitData(int unitId);
    Sprite GetUnitIcon(int unitId);
    float GetUnitIconCount();
    Sprite GetSkillIcon(int unitId);

    Sprite GetCard(int unitId); 
}

public class UnitDataProvider : IUnitDataProvider
{
    private readonly UnitDatabaseSO _unitDatabase;

    public UnitDataProvider(UnitDatabaseSO unitDatabase)
    {
        _unitDatabase = unitDatabase;
    }

    public GameObject GetUnitPrefab(int unitId) => _unitDatabase.unitModels[unitId];
    public UnitData GetUnitData(int unitId) => _unitDatabase.unitDatas[unitId];
    public Sprite GetUnitIcon(int unitId) => _unitDatabase.unitIcons[unitId];
    public float GetUnitIconCount() => _unitDatabase.unitIcons.Count;
    public Sprite GetSkillIcon(int unitId) => _unitDatabase.skillIcons[unitId];

    public Sprite GetCard(int unitId) => _unitDatabase.Cards[unitId];
}