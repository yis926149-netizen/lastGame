using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameConfig
{
    /// <summary>
    /// 自动生成的建筑平衡数据库 SO（只读）。
    /// 由 GameConfig.Editor 导入器从 game-config.json 写入，禁止手改。
    /// </summary>
    public sealed class BuildingBalanceDatabaseSO : ScriptableObject
    {
        [SerializeField] private BuildingBalanceData[] buildings = Array.Empty<BuildingBalanceData>();

        private Dictionary<string, BuildingBalanceData> byId;
        private Dictionary<int, BuildingBalanceData> byLegacyId;

        public IReadOnlyList<BuildingBalanceData> Buildings => buildings;

        public bool TryGetBuilding(string buildingId, out BuildingBalanceData building)
        {
            EnsureLookup();
            return byId.TryGetValue(buildingId, out building);
        }

        /// <summary>按迁移期旧整数 ID 查询（Provider 整数 ID 兼容层用）。</summary>
        public bool TryGetByLegacyId(int legacyId, out BuildingBalanceData building)
        {
            EnsureLookup();
            return byLegacyId.TryGetValue(legacyId, out building);
        }

        /// <summary>由导入器调用：整体替换数据并重建索引。</summary>
        public void ReplaceAll(BuildingBalanceData[] data)
        {
            buildings = data ?? Array.Empty<BuildingBalanceData>();
            byId = null;
            byLegacyId = null;
            EnsureLookup();
        }

        private void EnsureLookup()
        {
            if (byId is not null && byLegacyId is not null)
                return;

            byId = new Dictionary<string, BuildingBalanceData>(StringComparer.Ordinal);
            byLegacyId = new Dictionary<int, BuildingBalanceData>();
            foreach (var building in buildings)
            {
                if (!string.IsNullOrEmpty(building.buildingId))
                    byId[building.buildingId] = building;
                byLegacyId[building.legacyId] = building;
            }
        }
    }
}
