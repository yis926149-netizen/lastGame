namespace GameConfig
{
    /// <summary>
    /// 运行期只读访问生成配置的契约。
    /// 阶段2 由现有 Provider 实现此接口，并按稳定 ID 合并到既有数据通路。
    /// </summary>
    public interface IGameBalanceDatabase
    {
        UnitBalanceData GetUnit(string unitId);
    }
}
