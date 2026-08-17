using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GameConfig
{
    /// <summary>
    /// 自动生成的普通卡池数据库 SO（只读）。
    /// 由 GameConfig.Editor 导入器从 game-config.json 写入，禁止手改。
    /// 保底卡由 guaranteedFirst 标记的条目决定，不再依赖手工拖引用列表。
    /// </summary>
    public sealed class NormalCardPoolDatabaseSO : ScriptableObject
    {
        [SerializeField] private NormalCardPoolEntry[] cards = Array.Empty<NormalCardPoolEntry>();
        [SerializeField] private string guaranteedFirstCardId = "";

        private Dictionary<string, NormalCardPoolEntry> byCardId;
        private List<NormalCardPoolEntry> enabledCards;

        public IReadOnlyList<NormalCardPoolEntry> Cards => cards;

        /// <summary>首张保底卡的稳定 ID（unit.* / building.*）；无保底卡时为空串。</summary>
        public string GuaranteedFirstCardId => guaranteedFirstCardId;

        /// <summary>当前可抽取的卡（enabled=true），顺序与 Excel 一致。</summary>
        public IReadOnlyList<NormalCardPoolEntry> EnabledCards
        {
            get
            {
                EnsureLookup();
                return enabledCards;
            }
        }

        public bool TryGetCard(string cardId, out NormalCardPoolEntry entry)
        {
            EnsureLookup();
            return byCardId.TryGetValue(cardId, out entry);
        }

        /// <summary>由导入器调用：整体替换数据、推导保底卡并重建索引。</summary>
        public void ReplaceAll(NormalCardPoolEntry[] data)
        {
            cards = data ?? Array.Empty<NormalCardPoolEntry>();
            byCardId = null;
            enabledCards = null;

            var guaranteed = cards.FirstOrDefault(c => c is not null && c.guaranteedFirst);
            guaranteedFirstCardId = guaranteed?.cardId ?? "";

            EnsureLookup();
        }

        private void EnsureLookup()
        {
            if (byCardId is not null && enabledCards is not null)
                return;

            byCardId = new Dictionary<string, NormalCardPoolEntry>(StringComparer.Ordinal);
            enabledCards = new List<NormalCardPoolEntry>();
            foreach (var card in cards)
            {
                if (card is null)
                    continue;
                if (!string.IsNullOrEmpty(card.cardId))
                    byCardId[card.cardId] = card;
                if (card.enabled)
                    enabledCards.Add(card);
            }
        }
    }
}
