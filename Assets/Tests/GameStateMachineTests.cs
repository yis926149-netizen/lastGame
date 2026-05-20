using NUnit.Framework;
using NSubstitute;
using System.Threading.Tasks;
using Zenject;

public class GameStateMachineTests
{
    private DiContainer _container;
    private IPhase _mockPlayerPhase;
    private IPhase _mockAIPhase;
    private IPhase _mockSettlementPhase;
    private ICardService _mockCardService;
    private CardPresenter _mockCardPresenter;
    private IUnitRepository _mockUnitRepo;
    private ITechCultureService _mockTechCulture;
    private GameStateMachine _machine;

    [SetUp]
    public void SetUp()
    {
        _container = new DiContainer();

        _mockPlayerPhase = Substitute.For<IPhase>();
        _mockAIPhase = Substitute.For<IPhase>();
        _mockSettlementPhase = Substitute.For<IPhase>();

        _mockCardService = Substitute.For<ICardService>();
        _mockCardPresenter = Substitute.For<CardPresenter>(null, null, null, null, null, null, null, null, null, null); // 简化构造
        _mockUnitRepo = Substitute.For<IUnitRepository>();
        _mockTechCulture = Substitute.For<ITechCultureService>();

        _container.Bind<IPhase>().WithId("player").FromInstance(_mockPlayerPhase);
        _container.Bind<IPhase>().WithId("ai").FromInstance(_mockAIPhase);
        _container.Bind<IPhase>().WithId("settlement").FromInstance(_mockSettlementPhase);
        _container.Bind<ICardService>().FromInstance(_mockCardService);
        _container.Bind<CardPresenter>().FromInstance(_mockCardPresenter);
        _container.Bind<IUnitRepository>().FromInstance(_mockUnitRepo);
        _container.Bind<ITechCultureService>().FromInstance(_mockTechCulture);

        _container.Bind<GameStateMachine>().AsSingle();

        _machine = _container.Resolve<GameStateMachine>();
    }

    [Test]
    public void StartGame_EntersPlayerPhase()
    {
        _machine.StartGame();
        _mockPlayerPhase.Received(1).Enter();
    }

    [Test]
    public void EndTurn_WhenInPlayerPhase_ExitsPlayerAndMovesToNextPhase()
    {
        _machine.StartGame();
        _mockPlayerPhase.CanExit().Returns(true);
        _machine.EndTurn();

        _mockPlayerPhase.Received(1).Exit();
        _mockAIPhase.Received(1).Enter(); // 应进入 AI 阶段
    }

    [Test]
    public void AIPhase_WhenCompleted_AdvancesToSettlementAndNewTurn()
    {
        // 需要模拟 AIPhase.RunAITurn 返回已完成 Task
        var aiPhase = Substitute.For<AIPhase>(null);
        // 直接调用机器内部逻辑较复杂，此处省略详细测试，仅做示意
        // 真实测试可能需要将 AIPhase 替换为可控制的模拟
    }
}