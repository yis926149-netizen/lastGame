using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;

public class CardGenerationRuleTests
{
    private ICardUnlockRuleProvider _unlockProvider;
    private IFactionBuffService _factionBuff;
    private UnitConfigSO _unit;
    private BuildingConfigSO _building;

    [SetUp]
    public void SetUp()
    {
        _unit = ScriptableObject.CreateInstance<UnitConfigSO>();
        _building = ScriptableObject.CreateInstance<BuildingConfigSO>();

        _unlockProvider = Substitute.For<ICardUnlockRuleProvider>();
        _unlockProvider.GetUnlockedCards().Returns(
            new List<NormalCardConfigSO> { _unit, _building });
        _unlockProvider.GetGuaranteedFirstCard().Returns(_unit);
        _factionBuff = Substitute.For<IFactionBuffService>();
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(_unit);
        UnityEngine.Object.DestroyImmediate(_building);
    }

    [Test]
    public void GenerateNextCard_UnitProbabilityTalentUsesTenfoldUnitWeight()
    {
        _factionBuff.HasBuff(0, "0").Returns(true);

        NormalCardConfigSO card = Generate(new FixedRandom(9));

        Assert.AreSame(_unit, card);
    }

    [Test]
    public void GenerateNextCard_BuildingProbabilityTalentUsesTenfoldBuildingWeight()
    {
        _factionBuff.HasBuff(0, "1").Returns(true);

        NormalCardConfigSO card = Generate(new FixedRandom(1));

        Assert.AreSame(_building, card);
    }

    [Test]
    public void GenerateNextCard_OtherTalentDoesNotChangeUniformDraw()
    {
        _factionBuff.HasBuff(0, "2").Returns(true);

        NormalCardConfigSO card = Generate(new FixedRandom(1));

        Assert.AreSame(_building, card);
    }

    [Test]
    public void GenerateNextCard_FirstCardStillUsesGuaranteedCard()
    {
        _factionBuff.HasBuff(0, "1").Returns(true);
        bool hasGivenFirstCard = false;

        NormalCardConfigSO card = CardGenerationRule.GenerateNextCard(
            true,
            ref hasGivenFirstCard,
            _unlockProvider,
            new FixedRandom(1),
            _factionBuff,
            0);

        Assert.AreSame(_unit, card);
        Assert.IsTrue(hasGivenFirstCard);
    }

    private NormalCardConfigSO Generate(System.Random random)
    {
        bool hasGivenFirstCard = true;
        return CardGenerationRule.GenerateNextCard(
            false,
            ref hasGivenFirstCard,
            _unlockProvider,
            random,
            _factionBuff,
            0);
    }

    private sealed class FixedRandom : System.Random
    {
        private readonly int _value;

        public FixedRandom(int value)
        {
            _value = value;
        }

        public override int Next(int maxValue)
        {
            return Math.Min(_value, maxValue - 1);
        }
    }
}
