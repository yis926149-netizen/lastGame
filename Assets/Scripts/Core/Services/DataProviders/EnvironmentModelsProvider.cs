using UnityEngine;

public interface IEnvironmentModelsProvider
{
    GameObject GetLandFormPrefab(int environmentModelId);
    GameObject GetResourcePrefab(int environmentModelId);
    GameObject GetReapAnimalsEffect();
    GameObject GetReapPlantsEffect();
    GameObject GetReapMineralsEffect();
    GameObject GetReapChestEffect();
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

    public GameObject GetReapAnimalsEffect() => _environmentModels.reapAnimalsEffect;
    public GameObject GetReapPlantsEffect() => _environmentModels.reapPlantsEffect;
    public GameObject GetReapMineralsEffect() => _environmentModels.reapMineralsEffect;
    public GameObject GetReapChestEffect() => _environmentModels.reapChestEffect;
}