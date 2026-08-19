using UnityEngine;

namespace GameConfig
{
    /// <summary>
    /// 自动生成的表现配置数据库 SO（只读，单行）。
    /// 由 GameConfig.Editor 导入器从 game-config.json 写入，禁止手改。
    /// </summary>
    public sealed class FeelConfigDatabaseSO : ScriptableObject
    {
        [SerializeField] private FeelConfigData config;

        public FeelConfigData Config => config;

        public void ReplaceAll(FeelConfigData[] data)
        {
            config = (data != null && data.Length > 0) ? data[0] : null;
        }
    }
}
