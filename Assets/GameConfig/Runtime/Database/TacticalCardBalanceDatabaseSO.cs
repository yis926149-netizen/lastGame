using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameConfig
{
    /// <summary>
    /// 自动生成的战术卡数值数据库 SO（只读）。
    /// 由 GameConfig.Editor 导入器从 game-config.json 写入，禁止手改。
    /// </summary>
    public sealed class TacticalCardBalanceDatabaseSO : ScriptableObject
    {
        [SerializeField] private TacticalCardBalanceData[] cards = Array.Empty<TacticalCardBalanceData>();

        private Dictionary<string, TacticalCardBalanceData> byId;
        private Dictionary<int, TacticalCardBalanceData> byLegacyId;
        private List<TacticalCardBalanceData> enabledCards;

        public IReadOnlyList<TacticalCardBalanceData> Cards => cards;

        /// <summary>当前启用的战术卡（enabled=true），顺序与 Excel 稳定 ID 排序一致。</summary>
        public IReadOnlyList<TacticalCardBalanceData> EnabledCards
        {
            get { EnsureLookup(); return enabledCards; }
        }

        public bool TryGetCard(string cardId, out TacticalCardBalanceData card)
        {
            EnsureLookup();
            return byId.TryGetValue(cardId, out card);
        }

        /// <summary>按迁移期旧整数 ID 查询（整数 ID 兼容层用）。</summary>
        public bool TryGetByLegacyId(int legacyId, out TacticalCardBalanceData card)
        {
            EnsureLookup();
            return byLegacyId.TryGetValue(legacyId, out card);
        }

        public void ReplaceAll(TacticalCardBalanceData[] data)
        {
            cards = data ?? Array.Empty<TacticalCardBalanceData>();
            byId = null;
            byLegacyId = null;
            enabledCards = null;
            EnsureLookup();
        }

        private void EnsureLookup()
        {
            if (byId is not null && byLegacyId is not null && enabledCards is not null)
                return;

            byId = new Dictionary<string, TacticalCardBalanceData>(StringComparer.Ordinal);
            byLegacyId = new Dictionary<int, TacticalCardBalanceData>();
            enabledCards = new List<TacticalCardBalanceData>();
            foreach (var card in cards)
            {
                if (card is null)
                    continue;
                if (!string.IsNullOrEmpty(card.cardId))
                    byId[card.cardId] = card;
                byLegacyId[card.legacyId] = card;
                if (card.enabled)
                    enabledCards.Add(card);
            }
        }
    }
}
