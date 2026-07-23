using System;
using Zenject;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class AIPhase : IPhase
{
    // 依赖具体类 AIManager：既调用 ExecuteAITurn，也借其 MonoBehaviour.StartCoroutine 作协程宿主。
    private readonly AIManager _aiManager;

    [Inject]
    public AIPhase(AIManager aiManager)
    {
        _aiManager = aiManager;
    }

    public void Enter()
    {
        // ���� AI �׶Σ�ʵ��ִ���� GameStateMachine �� ProcessAIPhase ����
    }

    // �� IAIManager.ExecuteAITurn (IEnumerator) ��װΪ Task��
    // �� _aiManager ������Э�̲������ʱ���� TaskCompletionSource
    public Task RunAITurn()
    {
        var tcs = new TaskCompletionSource<bool>();

        IEnumerator Runner()
        {
            var enumerators = new Stack<IEnumerator>();
            bool failed = false;
            try
            {
                IEnumerator turn = _aiManager.ExecuteAITurn();
                if (turn != null)
                {
                    enumerators.Push(turn);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                tcs.TrySetResult(false);
                failed = true;
            }
            if (failed) yield break;

            while (enumerators.Count > 0)
            {
                IEnumerator currentEnumerator = enumerators.Peek();
                bool hasNext = false;
                object yielded = null;
                try
                {
                    hasNext = currentEnumerator.MoveNext();
                    if (hasNext)
                    {
                        yielded = currentEnumerator.Current;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    tcs.TrySetResult(false);
                    failed = true;
                }
                if (failed) yield break;

                if (!hasNext)
                {
                    enumerators.Pop();
                }
                else if (yielded is IEnumerator nestedEnumerator)
                {
                    enumerators.Push(nestedEnumerator);
                }
                else
                {
                    yield return yielded;
                }
            }

            tcs.TrySetResult(true);
        }

        try
        {
            _aiManager.StartCoroutine(Runner());
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            tcs.TrySetResult(false);
        }

        return tcs.Task;
    }

    public bool CanExit() => true;         // ���ⲿ������ʱ�˳�

    public void Exit()
    {
        // AI �׶ν���ʱ��������������Ҫ��
    }
}
