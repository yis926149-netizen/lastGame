using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
//****************************************
//创建人：易生
//功能说明：
//****************************************

public class Test : MonoBehaviour
{
    //public Button btn;
    void Start()
    {
        AddButtonClickEvent(test);
    }


    void Update()
    {
        
    }

    public void AddButtonClickEvent(UnityAction action)
    {
        Button btn = GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.AddListener(action);
        }
    }

    public void test()
    {
        //Debug.Log("测试~测试~");
        if(GetComponent<Image>().color == Color.white)
        {
            GetComponent<Image>().color = Color.blue;
        }
        else
        {
            GetComponent<Image>().color = Color.white;
        }
    }

}
