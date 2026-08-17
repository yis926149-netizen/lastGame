using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameConfig
{
    /// <summary>
    /// 自动生成的公共建筑数值数据库 SO（只读）。
    /// 由 GameConfig.Editor 导入器从 game-config.json 写入，禁止手改。
    /// prefab/markerIcon 等资源引用保留在手工资源 SO（PublicBuildingSO）。
    /// </summary>
    public sealed class PublicBuildingBalanceDatabaseSO : ScriptableObject
    {
        [SerializeField] private PublicBuildingBalanceData[] buildings = Array.Empty<PublicBuildingBalanceData>();

        private Dictionary<string, PublicBuildingBalanceData> byId;
        private Dictionary<int, PublicBuildingBalanceData> byLegacyId;
        private List<PublicBuildingBalanceData> enabledBuildings;

        public IReadOnlyList<PublicBuildingBalanceData> Buildings => buildings;

        /// <summary>当前启用的公共建筑（enabled=true）。</summary>
        public IReadOnlyList<PublicBuildingBalanceData> EnabledBuildings
        {
            get { EnsureLookup(); return enabledBuildings; }
        }

        public bool TryGetBuilding(string buildingId, out PublicBuildingBalanceData building)
        {
            EnsureLookup();
            return byId.TryGetValue(buildingId, out building);
        }

        /// <summary>按迁移期旧整数索引查询（buildings 数组下标）。</summary>
        public bool TryGetByLegacyId(int legacyId, out PublicBuildingBalanceData building)
        {
            EnsureLookup();
            return byLegacyId.TryGetValue(legacyId, out building);
        }

        public void ReplaceAll(PublicBuildingBalanceData[] data)
        {
            buildings = data ?? Array.Empty<PublicBuildingBalanceData>();
            byId = null;
            byLegacyId = null;
            enabledBuildings = null;
            EnsureLookup();
        }

        private void EnsureLookup()
        {
            if (byId is not null && byLegacyId is not null && enabledBuildings is not null)
                return;

            byId = new Dictionary<string, PublicBuildingBalanceData>(StringComparer.Ordinal);
            byLegacyId = new Dictionary<int, PublicBuildingBalanceData>();
            enabledBuildings = new List<PublicBuildingBalanceData>();
            foreach (var building in buildings)
            {
                if (building is null)
                    continue;
                if (!string.IsNullOrEmpty(building.buildingId))
                    byId[building.buildingId] = building;
                byLegacyId[building.legacyId] = building;
                if (building.enabled)
                    enabledBuildings.Add(building);
            }
        }
    }
}
