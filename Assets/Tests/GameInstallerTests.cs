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

        // 注：_unitDatabaseSO/_buildingDatabaseSO 等通过 GameInstaller.cs.meta 的 defaultReferences 自动赋值，不在缺失列表。
        StringAssert.Contains("_publicBuildingSO", exception.Message);
        StringAssert.Contains("_targetUICanvas", exception.Message);
        StringAssert.Contains("_normalCardPoolSO", exception.Message);
        StringAssert.Contains(nameof(MapGenerator), exception.Message);
        Assert.IsFalse(container.HasBinding<UnitDatabaseSO>());
    }
}
