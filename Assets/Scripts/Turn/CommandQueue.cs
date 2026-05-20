using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//****************************************
//创建人：易生
//功能说明：
//****************************************

public static class CommandQueue
{
    static Queue<ICommand> Queue = new();
    static Queue<ICommand> SettlementQueue = new();

    public static void Submit(ICommand cmd, Enums.CommandQueueType commandQueueType)
    {
        if(commandQueueType == Enums.CommandQueueType.Unit)
        {
            if (cmd.EnqueueValidate())
                Queue.Enqueue(cmd);
        }
        if (commandQueueType == Enums.CommandQueueType.Settlement)
        {
            if (cmd.EnqueueValidate())
                SettlementQueue.Enqueue(cmd);
        }

    }

    public static void ExecuteAll(Enums.CommandQueueType commandQueueType)
    {
        if (commandQueueType == Enums.CommandQueueType.Unit)
        {
            while (Queue.Count > 0)
            {
                ICommand cmd = Queue.Dequeue();
                if (!cmd.DequeueValidate()) { continue; }
                cmd.Execute();
            }
        }
        if(commandQueueType == Enums.CommandQueueType.Settlement)
        {
            while (SettlementQueue.Count > 0)
            {
                ICommand cmd = SettlementQueue.Dequeue();
                if (!cmd.DequeueValidate()) { continue; }
                cmd.Execute();
            }
        }
           
    }
}

