using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameConfig
{
    /// <summary>
    /// 自动生成的地图地貌数值数据库 SO（只读）。
    /// 由 GameConfig.Editor 导入器从 game-config.json 写入，禁止手改。
    /// </summary>
    public sealed class MapLandFormBalanceDatabaseSO : ScriptableObject
    {
        [SerializeField] private MapLandFormBalanceData[] landForms = Array.Empty<MapLandFormBalanceData>();

        private Dictionary<string, MapLandFormBalanceData> byId;
        private List<MapLandFormBalanceData> enabledLandForms;

        public IReadOnlyList<MapLandFormBalanceData> LandForms => landForms;

        /// <summary>当前启用的地貌（enabled=true），顺序与 Excel 稳定 ID 排序一致。</summary>
        public IReadOnlyList<MapLandFormBalanceData> EnabledLandForms
        {
            get { EnsureLookup(); return enabledLandForms; }
        }

        public bool TryGetLandForm(string landFormId, out MapLandFormBalanceData landForm)
        {
            EnsureLookup();
            return byId.TryGetValue(landFormId, out landForm);
        }

        public void ReplaceAll(MapLandFormBalanceData[] data)
        {
            landForms = data ?? Array.Empty<MapLandFormBalanceData>();
            byId = null;
            enabledLandForms = null;
            EnsureLookup();
        }

        private void EnsureLookup()
        {
            if (byId is not null && enabledLandForms is not null)
                return;

            byId = new Dictionary<string, MapLandFormBalanceData>(StringComparer.Ordinal);
            enabledLandForms = new List<MapLandFormBalanceData>();
            foreach (var landForm in landForms)
            {
                if (landForm is null)
                    continue;
                if (!string.IsNullOrEmpty(landForm.landFormId))
                    byId[landForm.landFormId] = landForm;
                if (landForm.enabled)
                    enabledLandForms.Add(landForm);
            }
        }
    }
}
