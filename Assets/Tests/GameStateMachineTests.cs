using System.Collections.Generic;
using System.Reflection;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using Zenject;

public class GameStateMachineTests
{
    private DiContainer _container;
    private PlayerPhase _playerPhase;
    private AIPhase _aiPhase;
    private SettlementPhase _settlementPhase;
    private GameStateMachine _machine;
    private GameObject _playerPhaseObject;
    private GameObject _settlementPhaseObject;
    private GameObject _aiManagerObject;

    [SetUp]
    public void SetUp()
    {
        _container = new DiContainer();

        var unitRepository = Substitute.For<IUnitRepository>();
        unitRepository.AllPlayerUnits.Returns(new Dictionary<GameObject, CharacterData>());

        _playerPhaseObject = new GameObject("PlayerPhase");
        _playerPhase = _playerPhaseObject.AddComponent<PlayerPhase>();
        _container.Bind<IUnitRepository>().FromInstance(unitRepository);
        _container.Inject(_playerPhase);

        _settlementPhaseObject = new GameObject("SettlementPhase");
        _settlementPhase = _settlementPhaseObject.AddComponent<SettlementPhase>();

        _aiManagerObject = new GameObject("AIManager");
        var aiManager = _aiManagerObject.AddComponent<AIManager>();
        _aiPhase = new AIPhase(aiManager);

        _container.Bind<PlayerPhase>().FromInstance(_playerPhase);
        _container.Bind<AIPhase>().FromInstance(_aiPhase);
        _container.Bind<SettlementPhase>().FromInstance(_settlementPhase);
        _container.Bind<ICardService>().FromInstance(Substitute.For<ICardService>());
        _container.Bind<CardPresenter>().FromInstance(new CardPresenter());
        _container.Bind<ITechCultureService>().FromInstance(Substitute.For<ITechCultureService>());
        _container.Bind<GameStateMachine>().AsSingle();

        _machine = _container.Resolve<GameStateMachine>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_playerPhaseObject);
        Object.DestroyImmediate(_settlementPhaseObject);
        Object.DestroyImmediate(_aiManagerObject);
    }

    [Test]
    public void NewGameStateMachine_StartsAtTurnOne()
    {
        Assert.AreEqual(1, _machine.CurrentTurn);
    }

    [Test]
    public void StartGame_EntersPlayerPhase()
    {
        _machine.StartGame();

        Assert.AreSame(_playerPhase, _machine.CurrentPhase);
    }

    [Test]
    public void StartGame_ResetsTurnAndPhase()
    {
        typeof(GameStateMachine)
            .GetField("_currentTurn", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(_machine, 5);
        typeof(GameStateMachine)
            .GetField("_currentPhaseIndex", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(_machine, 2);

        _machine.StartGame();

        Assert.AreEqual(1, _machine.CurrentTurn);
        Assert.AreSame(_playerPhase, _machine.CurrentPhase);
    }
}
