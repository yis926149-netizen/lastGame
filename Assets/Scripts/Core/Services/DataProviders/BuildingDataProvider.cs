using System.Collections.Generic;
using UnityEngine;

public interface IBuildingDataProvider
{
    GameObject GetCityModel();
    GameObject GetBuildingPrefab(int buildingId);
    float GetBuildingBaseHP(int buildingId);

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
    public Sprite GetBuildingCards(int buildingId) => _buildingDatabase.buildingCards[buildingId];

    public int GetBuildingCardsCount() => _buildingDatabase.buildingCards.Count;
}
