using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

public class ConsoleToolbarInjectorTests
{
    private EditorWindow _consoleWindow;

    [SetUp]
    public void SetUp()
    {
        Type consoleWindowType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("UnityEditor.ConsoleWindow", false))
            .FirstOrDefault(type => type != null);

        Assert.That(consoleWindowType, Is.Not.Null);
        _consoleWindow = EditorWindow.GetWindow(consoleWindowType);
        ConsoleToolbarInjector.RemoveFromAll();
    }

    [TearDown]
    public void TearDown()
    {
        ConsoleToolbarInjector.RemoveFromAll();
    }

    [Test]
    public void EnsureInjected_CalledTwice_AddsOneButton()
    {
        ConsoleToolbarInjector.EnsureInjected(_consoleWindow);
        ConsoleToolbarInjector.EnsureInjected(_consoleWindow);

        int buttonCount = _consoleWindow.rootVisualElement
            .Query<Button>(ConsoleToolbarInjector.ButtonName)
            .ToList()
            .Count;

        Assert.That(buttonCount, Is.EqualTo(1));
    }

    [Test]
    public void RemoveFromAll_CalledTwice_RemovesButton()
    {
        ConsoleToolbarInjector.EnsureInjected(_consoleWindow);

        ConsoleToolbarInjector.RemoveFromAll();
        ConsoleToolbarInjector.RemoveFromAll();

        Assert.That(
            _consoleWindow.rootVisualElement.Q<Button>(ConsoleToolbarInjector.ButtonName),
            Is.Null);
    }
}
