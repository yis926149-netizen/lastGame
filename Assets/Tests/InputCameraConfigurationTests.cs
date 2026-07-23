using System.IO;
using NUnit.Framework;
using UnityEditor;

public class InputCameraConfigurationTests
{
    [TestCase("Assets/Scripts/Infrastructure/Installers/GameInstaller.cs")]
    [TestCase("Assets/Scripts/Controllers/CameraController.cs")]
    public void GameScene_ReferencesCurrentScriptGuid(string scriptPath)
    {
        string guid = AssetDatabase.AssetPathToGUID(scriptPath);
        string scene = File.ReadAllText("Assets/Scenes/GameScene.unity");

        Assert.IsNotEmpty(guid);
        StringAssert.Contains($"guid: {guid}", scene);
    }

    [Test]
    public void GameScene_CameraRotationRaycastIncludesMapLayer()
    {
        string scene = File.ReadAllText("Assets/Scenes/GameScene.unity");

        StringAssert.IsMatch(@"rotationRaycastLayers:\s+serializedVersion: 2\s+m_Bits: 64", scene);
    }

    [Test]
    public void CameraController_HasSingleTickOwner()
    {
        string inputHandler = File.ReadAllText("Assets/Scripts/Core/Services/PlayerInputHandler.cs");
        string installer = File.ReadAllText("Assets/Scripts/Infrastructure/Installers/GameInstaller.cs");

        StringAssert.DoesNotContain("_cameraController.Tick()", inputHandler);
        StringAssert.Contains("BindInterfacesAndSelfTo<CameraController>()", installer);
    }

    [Test]
    public void InputCode_DoesNotDependOnUndefinedUnitLayers()
    {
        string inputHandler = File.ReadAllText("Assets/Scripts/Core/Services/PlayerInputHandler.cs");
        string uiController = File.ReadAllText("Assets/Scripts/UI/UIController.cs");

        StringAssert.DoesNotContain("LayerMask.GetMask(\"Units\"", inputHandler + uiController);
        StringAssert.DoesNotContain("LayerMask.GetMask(\"Buildings\"", inputHandler + uiController);
        StringAssert.DoesNotContain("LayerMask.GetMask(\"EnemyUnit\"", inputHandler + uiController);
    }
}
