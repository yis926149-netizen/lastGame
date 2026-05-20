using UnityEngine;
using Zenject;

public class GlobalServicesInstaller : MonoInstaller
{
    [SerializeField] private AudioManager audioManagerPrefabOrInstance; 

    public override void InstallBindings()
    {
        Container.Bind<AudioManager>()
                 .FromComponentOn(audioManagerPrefabOrInstance.gameObject)
                 .AsSingle()
                 .NonLazy();


    }
}