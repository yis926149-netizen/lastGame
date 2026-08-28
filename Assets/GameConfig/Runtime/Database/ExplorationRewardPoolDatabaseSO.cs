using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameConfig
{
    /// <summary>
    /// 自动生成的探索奖励池数据库 SO（只读）。
    /// 由 GameConfig.Editor 导入器从 game-config.json 写入，禁止手改。
    /// Provider 按 rewardType 过滤 EnabledEntries 并按 weight 加权抽取。
    /// </summary>
    public sealed class ExplorationRewardPoolDatabaseSO : ScriptableObject
    {
        [SerializeField] private ExplorationRewardPoolEntry[] entries = Array.Empty<ExplorationRewardPoolEntry>();

        private List<ExplorationRewardPoolEntry> enabledEntries;

        public IReadOnlyList<ExplorationRewardPoolEntry> Entries => entries;

        /// <summary>当前启用的奖励池条目（enabled=true）。</summary>
        public IReadOnlyList<ExplorationRewardPoolEntry> EnabledEntries
        {
            get { EnsureLookup(); return enabledEntries; }
        }

        private void OnEnable()
        {
            // Unity 可能在不重载域的 Play Mode 下复用 ScriptableObject 实例；
            // 运行时缓存不能跨生命周期沿用。
            enabledEntries = null;
        }

        public void ReplaceAll(ExplorationRewardPoolEntry[] data)
        {
            entries = data ?? Array.Empty<ExplorationRewardPoolEntry>();
            enabledEntries = null;
            EnsureLookup();
        }

        private void EnsureLookup()
        {
            if (enabledEntries is not null &&
                (enabledEntries.Count > 0 || entries == null || entries.Length == 0))
                return;

            enabledEntries = new List<ExplorationRewardPoolEntry>();
            foreach (var entry in entries)
            {
                if (entry != null && entry.enabled)
                    enabledEntries.Add(entry);
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[RewardTrace] PoolDatabase EnsureLookup raw={(entries == null ? -1 : entries.Length)} enabled={enabledEntries.Count}");
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    Debug.Log($"[RewardTrace] PoolDatabase entry={(entry == null ? "NULL" : $"{entry.rewardType}:{entry.configId}:enabled={entry.enabled}:weight={entry.weight}")}");
                }
            }
#endif
        }
    }
}
