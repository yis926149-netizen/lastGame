using UnityEngine;

//****************************************
// 【断供方案-阶段2】建筑失能门控：所有建筑统一查询"是否功能正常"。
// 定义：IsFunctional = 建筑所在格归属 == 建筑阵营 且 后勤畅通。
// 阵营真相源：BuildingBase.Player_City_Index.Key（易主时由迁移函数同步）。
// 挂载：BuildingBase.Start 统一附加；公共建筑挂根格，阵营取 PlayerIndex。
//****************************************

public class BuildingSupplyGate : MonoBehaviour
{
    private IMapDataService _mapDataService;
    private ILogisticsService _logisticsService;
    private BuildingBase _building;

    /// <summary>建筑所在格（首帧按世界坐标解析；易主后由 Retarget 显式重定向）</summary>
    private HexCellData _cell;

    /// <summary>当前是否功能正常（失能 = 断供）</summary>
    public bool IsFunctional { get; private set; }

    public void Initialize(IMapDataService mapDataService, ILogisticsService logisticsService)
    {
        _mapDataService = mapDataService;
        _logisticsService = logisticsService;
        _building = GetComponent<BuildingBase>();

        if (_logisticsService != null)
            _logisticsService.LogisticsChanged += Refresh;

        Refresh();
    }

    /// <summary>
    /// 易主（占领/吞并）后由 BuildingTransferService 调用：
    /// 显式重定向所在格（建筑可能跨格或位置未变），阵营由 BuildingBase 已同步的归属读取。
    /// </summary>
    public void Retarget(HexCellData cell)
    {
        if (cell != null) _cell = cell;
        Refresh();
    }

    public void Refresh()
    {
        if (_cell == null && _mapDataService != null)
            _cell = _mapDataService.GetCellByWorldPosition(transform.position);

        // 服务缺失时回退为功能正常（保持旧行为，与 BarracksSpawner 的 null 兼容一致）
        if (_logisticsService == null)
        {
            IsFunctional = true;
            return;
        }

        int factionId = _building != null ? _building.Player_City_Index.Key : -1;
        IsFunctional = _cell != null && factionId >= 0 &&
                       _logisticsService.IsOwnedBy(_cell, factionId) &&
                       _logisticsService.IsLogisticsConnected(_cell, factionId);
    }

    private void OnDestroy()
    {
        if (_logisticsService != null)
            _logisticsService.LogisticsChanged -= Refresh;
    }
}
