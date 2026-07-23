using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class UnitRepositoryTests
{
    private UnitRepository _repo;
    private GameObject _unitObj;
    private CharacterData _data;

    [SetUp]
    public void SetUp()
    {
        _repo = new UnitRepository();
        _unitObj = new GameObject();
        var unitData = new UnitData(0, "Test Unit", 5f, 100, 1, 10, 3f, 2f);
        _data = new CharacterData(0, _unitObj, null, unitData);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_unitObj);
    }

    [Test]
    public void AddPlayerUnit_UnitAddedToDictionary()
    {
        _repo.AddPlayerUnit(_unitObj, _data);
        Assert.IsTrue(_repo.AllPlayerUnits.ContainsKey(_unitObj));
        Assert.AreEqual(_data, _repo.GetPlayerUnit(_unitObj));
    }

    [Test]
    public void RemovePlayerUnit_UnitRemovedAndEventFired()
    {
        bool eventFired = false;
        _repo.OnPlayerUnitRemoved += (obj) => eventFired = true;
        _repo.AddPlayerUnit(_unitObj, _data);
        _repo.RemovePlayerUnit(_unitObj);
        Assert.IsFalse(_repo.AllPlayerUnits.ContainsKey(_unitObj));
        Assert.IsTrue(eventFired);
    }

    [Test]
    public void AddEnemyUnit_UnitAddedToGroup()
    {
        int aiIndex = 1;
        _repo.AddEnemyUnit(aiIndex, _unitObj, _data);
        var group = _repo.GetEnemyUnitGroup(aiIndex);
        Assert.IsTrue(group.ContainsKey(_unitObj));
    }

    [Test]
    public void TryGetEnemyUnit_ReturnsTrueIfExists()
    {
        _repo.AddEnemyUnit(1, _unitObj, _data);
        bool result = _repo.TryGetEnemyUnit(_unitObj, out var foundData);
        Assert.IsTrue(result);
        Assert.AreEqual(_data, foundData);
    }

    [Test]
    public void AddEnemyUnit_RemovesExistingPlayerRegistration()
    {
        _repo.AddPlayerUnit(_unitObj, _data);
        _repo.AddEnemyUnit(1, _unitObj, _data);

        Assert.IsFalse(_repo.AllPlayerUnits.ContainsKey(_unitObj));
        Assert.IsTrue(_repo.GetEnemyUnitGroup(1).ContainsKey(_unitObj));
    }

    [Test]
    public void AddEnemyUnit_MovesRegistrationBetweenGroups()
    {
        _repo.AddEnemyUnit(1, _unitObj, _data);
        _repo.AddEnemyUnit(2, _unitObj, _data);

        Assert.IsFalse(_repo.GetEnemyUnitGroup(1).ContainsKey(_unitObj));
        Assert.IsTrue(_repo.GetEnemyUnitGroup(2).ContainsKey(_unitObj));
    }
}
