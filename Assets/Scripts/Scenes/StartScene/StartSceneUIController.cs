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
        StartSceneUIManager.Instance.ControllerDic.Add(transform.name, this);
    }


}
