using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameConfig
{
    /// <summary>
    /// 自动生成的天赋抽卡偏好规则数据库 SO（只读）。
    /// 由 GameConfig.Editor 导入器从 game-config.json 写入，禁止手改。
    /// </summary>
    public sealed class TalentDrawRuleDatabaseSO : ScriptableObject
    {
        [SerializeField] private TalentDrawRuleData[] rules = Array.Empty<TalentDrawRuleData>();

        private List<TalentDrawRuleData> enabledRules;

        public IReadOnlyList<TalentDrawRuleData> Rules => rules;

        /// <summary>当前启用的抽卡偏好规则（enabled=true）。</summary>
        public IReadOnlyList<TalentDrawRuleData> EnabledRules
        {
            get { EnsureLookup(); return enabledRules; }
        }

        public void ReplaceAll(TalentDrawRuleData[] data)
        {
            rules = data ?? Array.Empty<TalentDrawRuleData>();
            enabledRules = null;
            EnsureLookup();
        }

        private void EnsureLookup()
        {
            if (enabledRules is not null)
                return;

            enabledRules = new List<TalentDrawRuleData>();
            foreach (var rule in rules)
            {
                if (rule != null && rule.enabled)
                    enabledRules.Add(rule);
            }
        }
    }
}
