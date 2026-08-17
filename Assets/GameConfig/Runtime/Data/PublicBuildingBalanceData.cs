using System;

namespace GameConfig
{
    /// <summary>
    /// 公共建筑数值（由 game-config.json 导入的只读数据）。
    /// prefab/markerIcon 等资源引用保留在手工资源 SO（PublicBuildingSO），数值与占格关系进本表。
    /// subHexDirections 为逗号分隔字符串（如 "NE,E,SE"），由 Provider 解析为方向数组。
    /// </summary>
    [Serializable]
    public sealed class PublicBuildingBalanceData
    {
        public string buildingId;
        public int legacyId;
        public string buildingName;
        public bool enabled;
        public float captureHp;
        public float defenseHp;
        public string subHexDirections;
    }
}
