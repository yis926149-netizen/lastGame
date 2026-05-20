using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//****************************************
//创建人：易生
//功能说明：
//****************************************

public class StartSceneUIManager : MonoBehaviour
{
    public static StartSceneUIManager Instance;
    //用字典管理所有的UI界面
    public Dictionary<string, StartSceneUIController> ControllerDic = new Dictionary<string, StartSceneUIController>();

    void Awake()
    {
        Instance = this;
    }

    //获取某一页面
    public StartSceneUIController GetInterface(string controllerName)
    {
        //确定该页面和该组件是否存在
        if (ControllerDic.ContainsKey(controllerName))
        {
            return ControllerDic[controllerName];
        }
        else
        {
            return null;
        }
    }

    //获取某一页面的某一组件
    public UIControl GetControl(string controllerName, string controlName)
    {
        //确定该页面和该组件是否存在
        if (ControllerDic.ContainsKey(controllerName) && ControllerDic[controllerName].ControlDic[controlName])
        {
            return ControllerDic[controllerName].ControlDic[controlName];
        }
        else
        {
            return null;
        }
    }

}
