using UnityEngine;

public interface IEnvironmentModelsProvider
{
    GameObject GetLandFormPrefab(int environmentModelId);
    GameObject GetResourcePrefab(int environmentModelId);
    GameObject GetReapEffect(Enums.ResourceType resourceType);
}

public class EnvironmentModelsProvider : IEnvironmentModelsProvider
{
    private readonly EnvironmentModelsSO _environmentModels;

    public EnvironmentModelsProvider(EnvironmentModelsSO unitDatabase)
    {
        _environmentModels = unitDatabase;
    }

    /*
    public GameObject GetLandFormPrefab(int environmentModelId)
        => _environmentModels.landFormModels[
            Mathf.Clamp(environmentModelId, 0, _environmentModels.landFormModels.Count - 1)
        ];

    public GameObject GetResourcePrefab(int environmentModelId)
        => _environmentModels.resourceModels[
            Mathf.Clamp(environmentModelId, 0, _environmentModels.resourceModels.Count - 1)
        ];
    */

    public GameObject GetLandFormPrefab(int environmentModelId) => _environmentModels.landFormModels[environmentModelId];

    public GameObject GetResourcePrefab(int environmentModelId) => _environmentModels.resourceModels[environmentModelId];

    public GameObject GetReapEffect(Enums.ResourceType resourceType)
    {
        return resourceType switch
        {
            Enums.ResourceType.Animals => _environmentModels.reapAnimalsEffect,
            Enums.ResourceType.Plants => _environmentModels.reapPlantsEffect,
            Enums.ResourceType.Minerals => _environmentModels.reapMineralsEffect,
            Enums.ResourceType.Chest => _environmentModels.reapChestEffect,
            _ => null,
        };
    }
}