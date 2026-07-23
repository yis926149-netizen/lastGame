using UnityEngine;
using Zenject;

public class GlobalServicesInstaller : MonoInstaller
{
    [SerializeField] private AudioManager _audioManager;

    public override void InstallBindings()
    {
        Container.Bind<AudioManager>()
            .FromInstance(_audioManager)
            .AsSingle();
    }
}
