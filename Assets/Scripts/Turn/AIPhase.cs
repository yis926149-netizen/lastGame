using Zenject;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

public class AIPhase : IPhase
{
    private readonly IAIManager _aiManager;   // 需要抽象 IAIManager

    [Inject]
    public AIPhase(IAIManager aiManager)
    {
        _aiManager = aiManager;
    }

    public void Enter()
    {
        // 进入 AI 阶段，实际执行由 GameStateMachine 的 ProcessAIPhase 驱动
    }

    // 将 IAIManager.ExecuteAITurn (IEnumerator) 包装为 Task，
    // 在 _aiManager 上启动协程并在完成时设置 TaskCompletionSource
    public Task RunAITurn()
    {
        var tcs = new TaskCompletionSource<bool>();

        IEnumerator Runner()
        {
            yield return _aiManager.ExecuteAITurn();
            tcs.SetResult(true);
        }

        // _aiManager 是 MonoBehaviour，可以启动协程
        _aiManager.StartCoroutine(Runner());

        return tcs.Task;
    }

    public bool CanExit() => true;         // 由外部决定何时退出

    public void Exit()
    {
        // AI 阶段结束时的清理（如有需要）
    }
}
