using System.Collections;

//****************************************
//创建人：易生
//功能说明：AI 管理器对外接口。消费方（GameFlowManager / AIPhase）只依赖此接口，
//         具体实现为 Assets/Scripts/AI/AIManager.cs。
//****************************************

public interface IAIManager
{
    // AI 初始化（开局时调用）
    void AIInit();

    // 执行一次 AI 回合（协程）
    IEnumerator ExecuteAITurn();
}
