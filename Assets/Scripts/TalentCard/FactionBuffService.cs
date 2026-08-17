using System.Collections.Generic;
using System;
using UnityEngine;

public class FactionBuffService : IFactionBuffService
{
    private readonly Dictionary<int, List<Buff>> _buffs = new();

    public event Action OnBuffsChanged;

    public float GetStatMultiplier(int faction, string statId)
    {
        if (!_buffs.TryGetValue(faction, out var buffs)) return 1f;

        float accumulated = 1f;
        for (int i = 0; i < buffs.Count; i++)
        {
            accumulated *= buffs[i].GetStatMultiplier(statId);
        }
        return accumulated;
    }

    public float GetStatAddition(int faction, string statId)
    {
        if (!_buffs.TryGetValue(faction, out var buffs)) return 0f;

        float accumulated = 0f;
        for (int i = 0; i < buffs.Count; i++)
        {
            accumulated += buffs[i].GetStatAddition(statId);
        }
        return accumulated;
    }

    public bool HasBuff(int faction, string buffId)
    {
        if (!_buffs.TryGetValue(faction, out var buffs)) return false;

        for (int i = 0; i < buffs.Count; i++)
        {
            if (buffs[i].id == buffId) return true;
        }
        return false;
    }

    public void AddBuff(int faction, Buff buff)
    {
        if (buff == null) return;

        if (!_buffs.TryGetValue(faction, out var buffs))
        {
            buffs = new List<Buff>();
            _buffs[faction] = buffs;
        }
        buffs.Add(buff);
        Debug.Log($"[FactionBuffService] Buff added: faction={faction}, id={buff.id}, total buffs={buffs.Count}");
        OnBuffsChanged?.Invoke();
    }
}
