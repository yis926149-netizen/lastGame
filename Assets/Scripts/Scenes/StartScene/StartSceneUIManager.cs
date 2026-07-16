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
        //单例重复保护：若已存在其他实例，销毁本次重复对象，避免 Instance 被后来者覆盖导致字典引用错乱
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[StartSceneUIManager] 场景中已存在实例，销毁重复的 \"{name}\"。", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        //本单例仅属于当前场景，不常驻，故不调用 DontDestroyOnLoad
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
        //确定该页面和该组件是否存在（两级 TryGetValue，避免索引器在键缺失时抛 KeyNotFoundException）
        if (ControllerDic.TryGetValue(controllerName, out var controller) &&
            controller.ControlDic.TryGetValue(controlName, out var control))
        {
            return control;
        }

        Debug.LogWarning($"[StartSceneUIManager] GetControl 未找到控件：页面 \"{controllerName}\"，控件 \"{controlName}\"，请检查 Inspector 配置或命名。");
        return null;
    }

}
