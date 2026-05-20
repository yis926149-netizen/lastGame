using System.Collections;
using System.Collections.Generic;
using System.Windows.Input;
using Unity.VisualScripting;
using UnityEngine;

//****************************************
//创建人：易生
//功能说明：
//****************************************
public interface ICommand
{
    bool EnqueueValidate();
    bool DequeueValidate();
    void Execute();
}

public class MoveCommand : ICommand
{
    //移动的目的地块
    HexCellData hexCellData;
    //需要移动的单位
    GameObject movingObject;
    //移动目的
    Enums.MovementPurpose movementPurpose;

    public MoveCommand(HexCellData hexCellData, GameObject movingObject, Enums.MovementPurpose movementPurpose)
    {
        this.hexCellData = hexCellData;
        this.movingObject = movingObject;
        this.movementPurpose = movementPurpose;
    }

    public bool EnqueueValidate()
    {
        return true;
    }

    public bool DequeueValidate()
    {
        bool validate = true;

        //Debug.Log("你好");
        //若该回合内该单位无其他移动则执行该命令，否则删除该命令
        // Keep legacy behavior: check whether unit already moved this turn via isMoved flag
        if (!movingObject.GetComponent<UnitMovementController>().isMoved)
        {
            //Debug.Log("失败了吗 - 1");
            validate = false;
        }

        return validate;
    }

    public void Execute()
    {
        //选择该单位
        movingObject.GetComponent<UnitMovementController>().characterData.isSelected = true;
    }
}

