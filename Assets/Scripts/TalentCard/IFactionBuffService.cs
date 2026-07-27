using System;

public interface IFactionBuffService
{
    float GetStatMultiplier(int faction, string statId);
    float GetStatAddition(int faction, string statId);
    void AddBuff(int faction, Buff buff);
    event Action OnBuffsChanged;
}
