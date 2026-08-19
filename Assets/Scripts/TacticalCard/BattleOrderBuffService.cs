using System.Collections.Generic;
using Zenject;

/// <summary>
/// 战术卡「战斗号令」临时增益服务。
/// 攻击：直接改写 CharacterData.currentAttackValue（所有伤害公式与信息面板共用此值）。
/// 移速：通过 CharacterData.moveSpeedMultiplier 在移动请求时倍增实时移动速度。
/// 计时使用 GameLoop.GameTime（暂停冻结），到期自动还原。
/// </summary>
public class BattleOrderBuffService : ITickable
{
    private readonly GameLoop _gameLoop;
    private readonly Dictionary<CharacterData, ActiveBuff> _active = new Dictionary<CharacterData, ActiveBuff>();

    public BattleOrderBuffService(GameLoop gameLoop)
    {
        _gameLoop = gameLoop;
    }

    /// <summary>对一批单位施加战斗号令增益；已生效的单位刷新剩余时间，不叠加。</summary>
    public void Apply(IEnumerable<CharacterData> units, float attackMultiplier, float speedMultiplier, float duration)
    {
        if (units == null) return;

        float expiry = _gameLoop.GameTime + duration;
        foreach (CharacterData data in units)
        {
            if (data == null) continue;

            if (_active.TryGetValue(data, out ActiveBuff existing))
            {
                existing.Expiry = expiry;
                continue;
            }

            float originalAttack = data.currentAttackValue;
            data.currentAttackValue = originalAttack * attackMultiplier;
            data.moveSpeedMultiplier = speedMultiplier;
            _active[data] = new ActiveBuff { OriginalAttack = originalAttack, Expiry = expiry };
        }
    }

    public void Tick()
    {
        if (_active.Count == 0) return;

        float now = _gameLoop.GameTime;
        List<CharacterData> expired = null;
        foreach (KeyValuePair<CharacterData, ActiveBuff> kv in _active)
        {
            if (kv.Value.Expiry <= now)
            {
                if (expired == null) expired = new List<CharacterData>();
                expired.Add(kv.Key);
            }
        }

        if (expired == null) return;
        foreach (CharacterData data in expired)
        {
            if (!_active.TryGetValue(data, out ActiveBuff buff)) continue;
            if (data != null)
            {
                data.currentAttackValue = buff.OriginalAttack;
                data.moveSpeedMultiplier = 1f;
            }
            _active.Remove(data);
        }
    }

    private sealed class ActiveBuff
    {
        public float OriginalAttack;
        public float Expiry;
    }
}
