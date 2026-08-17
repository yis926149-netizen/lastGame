using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameConfig
{
    /// <summary>
    /// 自动生成的地图资源数值数据库 SO（只读）。
    /// 由 GameConfig.Editor 导入器从 game-config.json 写入，禁止手改。
    /// </summary>
    public sealed class MapResourceBalanceDatabaseSO : ScriptableObject
    {
        [SerializeField] private MapResourceBalanceData[] resources = Array.Empty<MapResourceBalanceData>();

        private Dictionary<string, MapResourceBalanceData> byId;
        private List<MapResourceBalanceData> enabledResources;

        public IReadOnlyList<MapResourceBalanceData> Resources => resources;

        /// <summary>当前启用的资源（enabled=true），顺序与 Excel 稳定 ID 排序一致。</summary>
        public IReadOnlyList<MapResourceBalanceData> EnabledResources
        {
            get { EnsureLookup(); return enabledResources; }
        }

        public bool TryGetResource(string resourceId, out MapResourceBalanceData resource)
        {
            EnsureLookup();
            return byId.TryGetValue(resourceId, out resource);
        }

        public void ReplaceAll(MapResourceBalanceData[] data)
        {
            resources = data ?? Array.Empty<MapResourceBalanceData>();
            byId = null;
            enabledResources = null;
            EnsureLookup();
        }

        private void EnsureLookup()
        {
            if (byId is not null && enabledResources is not null)
                return;

            byId = new Dictionary<string, MapResourceBalanceData>(StringComparer.Ordinal);
            enabledResources = new List<MapResourceBalanceData>();
            foreach (var resource in resources)
            {
                if (resource is null)
                    continue;
                if (!string.IsNullOrEmpty(resource.resourceId))
                    byId[resource.resourceId] = resource;
                if (resource.enabled)
                    enabledResources.Add(resource);
            }
        }
    }
}
