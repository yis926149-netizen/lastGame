using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameConfig
{
    /// <summary>
    /// 自动生成的天赋卡数值数据库 SO（只读）。
    /// 由 GameConfig.Editor 导入器从 game-config.json 写入，禁止手改。
    /// </summary>
    public sealed class TalentCardBalanceDatabaseSO : ScriptableObject
    {
        [SerializeField] private TalentCardBalanceData[] talents = Array.Empty<TalentCardBalanceData>();

        private Dictionary<string, TalentCardBalanceData> byId;
        private Dictionary<int, TalentCardBalanceData> byLegacyId;
        private List<TalentCardBalanceData> enabledTalents;

        public IReadOnlyList<TalentCardBalanceData> Talents => talents;

        /// <summary>当前启用的天赋卡（enabled=true），顺序与 Excel 稳定 ID 排序一致。</summary>
        public IReadOnlyList<TalentCardBalanceData> EnabledTalents
        {
            get { EnsureLookup(); return enabledTalents; }
        }

        public bool TryGetTalent(string talentId, out TalentCardBalanceData talent)
        {
            EnsureLookup();
            return byId.TryGetValue(talentId, out talent);
        }

        /// <summary>按迁移期旧整数 ID 查询（整数 ID 兼容层用）。</summary>
        public bool TryGetByLegacyId(int legacyId, out TalentCardBalanceData talent)
        {
            EnsureLookup();
            return byLegacyId.TryGetValue(legacyId, out talent);
        }

        public void ReplaceAll(TalentCardBalanceData[] data)
        {
            talents = data ?? Array.Empty<TalentCardBalanceData>();
            byId = null;
            byLegacyId = null;
            enabledTalents = null;
            EnsureLookup();
        }

        private void EnsureLookup()
        {
            if (byId is not null && byLegacyId is not null && enabledTalents is not null)
                return;

            byId = new Dictionary<string, TalentCardBalanceData>(StringComparer.Ordinal);
            byLegacyId = new Dictionary<int, TalentCardBalanceData>();
            enabledTalents = new List<TalentCardBalanceData>();
            foreach (var talent in talents)
            {
                if (talent is null)
                    continue;
                if (!string.IsNullOrEmpty(talent.talentId))
                    byId[talent.talentId] = talent;
                byLegacyId[talent.legacyId] = talent;
                if (talent.enabled)
                    enabledTalents.Add(talent);
            }
        }
    }
}
