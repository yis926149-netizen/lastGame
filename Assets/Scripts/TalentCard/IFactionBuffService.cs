using System;

public interface IFactionBuffService
{
    float GetStatMultiplier(int faction, string statId);
    float GetStatAddition(int faction, string statId);
    bool HasBuff(int faction, string buffId);
    void AddBuff(int faction, Buff buff);
    event Action OnBuffsChanged;
}
