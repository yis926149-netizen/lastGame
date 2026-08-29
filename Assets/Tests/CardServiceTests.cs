using System.Collections.Generic;
using System.Reflection;
using GameConfig;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using Zenject;

public class CardServiceTests
{
    private DiContainer _container;
    private IUIConfigProvider _mockUiConfig;
    private ICardUnlockRuleProvider _mockUnlockRules;
    private IFactionBuffService _mockFactionBuff;
    private CardService _service;
    private NormalCardPoolSO _pool;
    private static CoreGameplayConfigDatabaseSO _gameplayDb;

    [SetUp]
    public void SetUp()
    {
        SeedService.Initialize(12345);
        EnsureCoreGameplayConfigured();
        _pool = ScriptableObject.CreateInstance<NormalCardPoolSO>();
        var unit0 = ScriptableObject.CreateInstance<UnitConfigSO>();
        unit0.unitData = new UnitData(0, "Settler", 1, 20, 1, 0, 1, 2);
        var unit1 = ScriptableObject.CreateInstance<UnitConfigSO>();
        unit1.unitData = new UnitData(1, "Warrior", 1, 20, 1, 5, 1, 2);
        _pool.cards = new List<NormalCardConfigSO> { unit0, unit1 };
        _pool.guaranteedFirstCard = unit0;

        _container = new DiContainer();

        _mockUiConfig = Substitute.For<IUIConfigProvider>();
        _mockUiConfig.CardSlotSpacing.Returns(125f);
        _mockUiConfig.NextCardSlotGap.Returns(50f);

        _mockUnlockRules = Substitute.For<ICardUnlockRuleProvider>();
        _mockUnlockRules.GetUnlockedCards().Returns(new List<NormalCardConfigSO> { unit0, unit1 });
        _mockUnlockRules.GetGuaranteedFirstCard().Returns(unit0);
        _mockFactionBuff = Substitute.For<IFactionBuffService>();

        _container.Bind<IUIConfigProvider>().FromInstance(_mockUiConfig);
        _container.Bind<ICardUnlockRuleProvider>().FromInstance(_mockUnlockRules);
        _container.Bind<IFactionBuffService>().FromInstance(_mockFactionBuff);
        _container.Bind<ICardService>().To<CardService>().AsSingle();

        _service = _container.Resolve<ICardService>() as CardService;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_pool);
    }

    [Test]
    public void GenerateNextCard_FirstCallReturnsGuaranteedSettler()
    {
        NormalCardConfigSO card = _service.GenerateNextCard();
        Assert.IsNotNull(card);
        Assert.IsTrue(card is UnitConfigSO);
        Assert.AreEqual(0, ((UnitConfigSO)card).Id);
    }

    [Test]
    public void GenerateNextCard_SubsequentCallsBelongToPool()
    {
        _service.GenerateNextCard(); // 消耗保底
        for (int i = 0; i < 10; i++)
        {
            NormalCardConfigSO card = _service.GenerateNextCard();
            Assert.IsNotNull(card);
            CollectionAssert.Contains(new List<NormalCardConfigSO>(_pool.cards), card);
        }
    }

    [Test]
    public void RegisterCardView_OccupiesSlot()
    {
        for (int slot = 0; slot < 5; slot++)
        {
            _service.RegisterCardView(slot, Substitute.For<ICardView>());
        }
        Assert.AreEqual(-1, _service.GetFirstEmptySlot());
    }

    [Test]
    public void RemoveCard_FreesSlot()
    {
        var view = Substitute.For<ICardView>();
        _service.RegisterCardView(0, view);
        _service.RemoveCard(0);
        Assert.AreEqual(0, _service.GetFirstEmptySlot());
    }

    [Test]
    public void GetSlotOffset_AddsNextCardGapBeforeHandSlots()
    {
        Assert.AreEqual(new Vector2(175f, 0f), _service.GetSlotOffset(0));
        Assert.AreEqual(new Vector2(300f, 0f), _service.GetSlotOffset(1));
        Assert.AreEqual(new Vector2(425f, 0f), _service.GetSlotOffset(2));
    }

    [Test]
    public void ShiftSlotsRight_EmptyHand_KeepsAllSlotsEmptyAndDropsNothing()
    {
        _service.ShiftSlotsRight(out ICardView dropped);

        Assert.IsNull(dropped);
        Assert.AreEqual(0, _service.GetFirstEmptySlot());
        ICardView[] slots = GetSlots();
        for (int i = 0; i < slots.Length; i++)
            Assert.IsNull(slots[i], $"slot {i} 应为空");
    }

    [Test]
    public void ShiftSlotsRight_PartialHand_ShiftsRightAndLeavesSlot0Empty()
    {
        var v0 = Substitute.For<ICardView>();
        var v1 = Substitute.For<ICardView>();
        _service.RegisterCardView(0, v0);
        _service.RegisterCardView(1, v1);

        _service.ShiftSlotsRight(out ICardView dropped);

        Assert.IsNull(dropped, "未满手牌时不应挤出卡");
        Assert.AreEqual(0, _service.GetFirstEmptySlot(), "slot 0 应被清空");
        ICardView[] slots = GetSlots();
        Assert.IsNull(slots[0]);
        Assert.AreSame(v0, slots[1], "原 slot 0 应右移到 slot 1");
        Assert.AreSame(v1, slots[2], "原 slot 1 应右移到 slot 2");
    }

    [Test]
    public void ShiftSlotsRight_FullHand_DropsLastAndShiftsRestRight()
    {
        ICardView[] before = GetSlots();
        int count = before.Length;
        var views = new ICardView[count];
        for (int i = 0; i < count; i++)
        {
            views[i] = Substitute.For<ICardView>();
            _service.RegisterCardView(i, views[i]);
        }

        _service.ShiftSlotsRight(out ICardView dropped);

        Assert.AreSame(views[count - 1], dropped, "满手牌时原末位卡应被挤出");
        Assert.AreEqual(0, _service.GetFirstEmptySlot(), "slot 0 应被清空");
        ICardView[] after = GetSlots();
        Assert.IsNull(after[0]);
        for (int i = 1; i < count; i++)
            Assert.AreSame(views[i - 1], after[i], $"slot {i} 应持有原 slot {i - 1} 的卡");
    }

    /// <summary>CardService 构造依赖 CoreGameplayConfigProvider.HandCardLimit；测试内保证已配置为 5。</summary>
    private static void EnsureCoreGameplayConfigured()
    {
        if (_gameplayDb != null) return;

        _gameplayDb = ScriptableObject.CreateInstance<CoreGameplayConfigDatabaseSO>();
        _gameplayDb.ReplaceAll(new[] { new CoreGameplayConfigData { handCardLimit = 5 } });
        CoreGameplayConfigProvider.Configure(_gameplayDb);
    }

    /// <summary>反射读取私有 _slots，用于断言 ShiftSlotsRight 的最终顺序。</summary>
    private ICardView[] GetSlots()
    {
        FieldInfo field = typeof(CardService).GetField("_slots",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "CardService._slots 字段未找到");
        return (ICardView[])field.GetValue(_service);
    }
}
