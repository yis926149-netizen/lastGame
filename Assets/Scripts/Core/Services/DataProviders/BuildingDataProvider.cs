using System.Collections.Generic;
using UnityEngine;

public interface IBuildingDataProvider
{
    GameObject GetCityModel();
    GameObject GetBuildingPrefab(int buildingId);
    float GetBuildingBaseHP(int buildingId);

    void SetBuildingBaseHP(float hp);
    Sprite GetBuildingCards(int buildingId);

    int GetBuildingCardsCount();
}

public class BuildingDataProvider : IBuildingDataProvider
{
    private BuildingDatabaseSO _buildingDatabase;

    public BuildingDataProvider(BuildingDatabaseSO buildingDatabase)
    {
        _buildingDatabase = buildingDatabase;
    }

    public GameObject GetCityModel() => _buildingDatabase.CityModel[0];
    public GameObject GetBuildingPrefab(int buildingId) => _buildingDatabase.buildingModels[buildingId];
    public float GetBuildingBaseHP(int buildingId) => _buildingDatabase.buildingBaseHP[buildingId];
    public void SetBuildingBaseHP(float hp)
    {
        //_buildingDatabase.buildingBaseHP
        for(int i = 0; i < _buildingDatabase.buildingBaseHP.Count; i++)
        {
            _buildingDatabase.buildingBaseHP[i] = hp;
        }
    }

    public Sprite GetBuildingCards(int buildingId) => _buildingDatabase.buildingCards[buildingId];

    public int GetBuildingCardsCount() => _buildingDatabase.buildingCards.Count;
}