using System;

public interface IGameStateMachine
{
    int CurrentTurn { get; }
    IPhase CurrentPhase { get; }
    void StartGame();                // ��Ϸ��ʼʱ����
    void EndTurn();                  // ��ҵ���������غϡ�ʱ����

    /// <summary>
    /// 当前阶段发生变化时触发（进入任一阶段后）。
    /// UI 可据此在非玩家阶段禁用“下一回合”按钮。
    /// </summary>
    event Action PhaseChanged;
}