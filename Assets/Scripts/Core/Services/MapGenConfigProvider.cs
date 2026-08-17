using GameConfig;

//****************************************
//功能说明：地图生成参数提供者。
//         Perlin 噪声频率/八度/持续与竞技场半径/动画时长优先由 Excel 读取，
//         Excel 未生成时回退 Legacy 默认值（双轨迁移期，阶段6 删除回退）。
//****************************************
public class MapGenConfigProvider
{
    private readonly MapGenConfigDatabaseSO _database;   // Excel 数值（可选）

    public MapGenConfigProvider(MapGenConfigDatabaseSO database = null)
    {
        _database = database;
    }

    public MapGenConfigData Config => _database?.Config;

    public float PerlinFrequency => Config?.perlinFrequency ?? 0.05f;

    public int PerlinOctaves => Config?.perlinOctaves ?? 3;

    public float PerlinPersistence => Config?.perlinPersistence ?? 0.6f;

    public int ArenaRadius => Config?.arenaRadius ?? 3;

    public float ArenaRiseDurationSeconds => Config?.arenaRiseDurationSeconds ?? 1.2f;
}
