//****************************************
//功能说明：AI 共享随机源。所有 AI 子服务（工厂/卡牌脑/战术脑）共用同一个 System.Random，
//         保证拆分后 RNG 推进序列与拆分前单实例行为完全一致。
//         SeedService.GetRandom("AI") 每次都 new 新实例，故不能各子服务各自取；
//         这里懒加载缓存一份共享（首次访问时 SeedService 已 Initialize）。
//****************************************

public class AIRandomProvider
{
    private System.Random _random;

    public System.Random Random => _random ??= SeedService.GetRandom("AI");
}
