using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//****************************************
//创建人：易生
//功能说明：
//****************************************

public class StartSceneUIController : MonoBehaviour
{
    //管理该UI界面上的所有控件
    public Dictionary<string, UIControl> ControlDic = new Dictionary<string, UIControl>();

   void Start()
    {
        //给本页面的所有子控件添加UIControl
        foreach (Transform tran in transform)
        {
            //若该子物体无UIControl
            if (!tran.gameObject.GetComponent<UIControl>())
            {
                //添加脚本
                tran.gameObject.AddComponent<UIControl>();
            }
        }

        //让本页面载入UI管理类单例的字典中
        if (StartSceneUIManager.Instance == null)
        {
            Debug.LogError($"[StartSceneUIController] 无法注册页面 \"{transform.name}\"：StartSceneUIManager.Instance 为空。", this);
            return;
        }

        var controllerDic = StartSceneUIManager.Instance.ControllerDic;
        //防重复注册：快速连点可能在旧实例注册前又创建同名页面，直接 Add 会抛 ArgumentException
        if (controllerDic.TryGetValue(transform.name, out var existing))
        {
            if (existing == this)
            {
                return;
            }
            Debug.LogWarning($"[StartSceneUIController] 已存在同名页面 \"{transform.name}\"，销毁本次重复创建的实例。", this);
            Destroy(gameObject);
            return;
        }
        controllerDic.Add(transform.name, this);
    }


}
