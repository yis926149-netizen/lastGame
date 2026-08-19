using GameConfig;

//****************************************
//功能说明：地图生成参数提供者（阶段6：Excel 唯一主源）。
//         Perlin 噪声频率/八度/持续与竞技场半径/动画时长仅由 Excel 读取；
//         Excel 未生成/未绑定时抛异常，暴露配置缺失。
//****************************************
public class MapGenConfigProvider
{
    private readonly MapGenConfigDatabaseSO _database;

    public MapGenConfigProvider(MapGenConfigDatabaseSO database = null)
    {
        _database = database;
    }

    public MapGenConfigData Config
    {
        get
        {
            if (_database?.Config == null)
                throw new System.InvalidOperationException(
                    "[MapGen] Excel 地图生成参数未加载：请先运行 工具/游戏配置/导入并校验，并在 GameInstaller 绑定 MapGenConfigDatabaseSO。");
            return _database.Config;
        }
    }

    public float PerlinFrequency => Config.perlinFrequency;
    public int PerlinOctaves => Config.perlinOctaves;
    public float PerlinPersistence => Config.perlinPersistence;
    public int ArenaRadius => Config.arenaRadius;
    public float ArenaRiseDurationSeconds => Config.arenaRiseDurationSeconds;
}
