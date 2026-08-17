using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameConfig
{
    /// <summary>
    /// 自动生成的单位平衡数据库 SO（只读）。
    /// 由 GameConfig.Editor 导入器从 game-config.json 写入，禁止手改。
    /// </summary>
    public sealed class UnitBalanceDatabaseSO : ScriptableObject
    {
        [SerializeField] private UnitBalanceData[] units = Array.Empty<UnitBalanceData>();

        private Dictionary<string, UnitBalanceData> byId;
        private Dictionary<int, UnitBalanceData> byLegacyId;

        public IReadOnlyList<UnitBalanceData> Units => units;

        public bool TryGetUnit(string unitId, out UnitBalanceData unit)
        {
            EnsureLookup();
            return byId.TryGetValue(unitId, out unit);
        }

        /// <summary>按迁移期旧整数 ID 查询（Provider 整数 ID 兼容层用）。</summary>
        public bool TryGetByLegacyId(int legacyId, out UnitBalanceData unit)
        {
            EnsureLookup();
            return byLegacyId.TryGetValue(legacyId, out unit);
        }

        /// <summary>由导入器调用：整体替换数据并重建索引。</summary>
        public void ReplaceAll(UnitBalanceData[] data)
        {
            units = data ?? Array.Empty<UnitBalanceData>();
            byId = null;
            byLegacyId = null;
            EnsureLookup();
        }

        private void EnsureLookup()
        {
            if (byId is not null && byLegacyId is not null)
                return;

            byId = new Dictionary<string, UnitBalanceData>(StringComparer.Ordinal);
            byLegacyId = new Dictionary<int, UnitBalanceData>();
            foreach (var unit in units)
            {
                if (!string.IsNullOrEmpty(unit.unitId))
                    byId[unit.unitId] = unit;
                byLegacyId[unit.legacyId] = unit;
            }
        }
    }
}
