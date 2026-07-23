using NUnit.Framework;
using UnityEngine;
using Zenject;

public class GameInstallerTests
{
    private GameObject _installerObject;

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_installerObject);
    }

    [Test]
    public void InstallBindings_WithMissingReferences_FailsBeforeRegisteringBindings()
    {
        _installerObject = new GameObject("GameInstallerTest");
        var installer = _installerObject.AddComponent<GameInstaller>();
        var container = new DiContainer();
        container.Inject(installer);

        var exception = Assert.Throws<ZenjectException>(() => installer.InstallBindings());

        StringAssert.Contains("_unitDatabaseSO", exception.Message);
        StringAssert.Contains("_buildingDatabaseSO", exception.Message);
        StringAssert.Contains("_targetUICanvas", exception.Message);
        StringAssert.Contains(nameof(MapGenerator), exception.Message);
        Assert.IsFalse(container.HasBinding<UnitDatabaseSO>());
    }
}
