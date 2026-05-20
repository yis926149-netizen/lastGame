using DG.Tweening;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Zenject;
//using HighlightingSystem;
//using UnityEditor;

//****************************************
//创建人：易生
//功能说明：
//****************************************

public class UIControl : MonoBehaviour
{
    [Inject] private AudioManager _audioManager;
    [Inject] private DiContainer _container;

    //用于判断该脚本属于哪个页面
    public StartSceneUIController controller;

    void Awake()
    {
        //以向上查找的方式获取父页面
        if (transform.parent != null)
        {
            //获取父物体的UIController脚本
            controller = transform.GetComponentInParent<StartSceneUIController>();
            //若父物体存在该脚本
            if(controller != null )
            {
                //有脚本，找到了父页面，添加该组件到其字典内
                controller.ControlDic.Add(transform.name, this);
                //Debug.Log(this + "找到了父页面"+ controller);
            }
            else
            {
                Debug.Log(this + "没找到父页面");
            }
        }
    }

    void OnEnable()
    {
        if (_audioManager == null)
        {
            _audioManager = FindObjectOfType<AudioManager>();
            if (_audioManager == null)
            {
                Debug.LogError("找不到 AudioManager！请检查场景中是否存在 + 是否DontDestroyOnLoad");
            }
            else
            {
                Debug.LogWarning("自动注入失败，使用 FindObjectOfType 补救获得 AudioManager");
            }
        }
    }

    void Update()
    {
        //这是游戏设定界面的interface的按钮绑定的那个
        if (GameObject.Find("Pop up"))
        {
            string input = Input.inputString;//键盘输入的帧监测（只会监测一帧）
            if (input != "")
            {
                mainbody5_text = input;
                if (BindIndex == 0)
                {
                    GameObject.Find("priButton").transform.GetChild(1).GetComponent<Text>().text = mainbody5_text;
                }
                else if (BindIndex == 1)
                {
                    GameObject.Find("secButton").transform.GetChild(1).GetComponent<Text>().text = mainbody5_text;
                }
                Destroy(GameObject.Find("black_Pop up blocker"));
                Destroy(GameObject.Find("Pop up"));
            }
        }
    }

    public void AddButtonClickEvent(UnityAction action)
    {
        Button btn = GetComponent<Button>();
        if (btn != null )
        {
            btn.onClick.AddListener(action);
        }
    }
    /// <summary>
    /// 开始界面首级按钮动画
    /// </summary>
    //用于记录首级按钮的id
    public int firstButId;
    //用于记录该首级按钮的被点击按钮
    public GameObject thisFirButBeClicked;
    //记录首级按钮的左侧栏
    public GameObject LSideBar;
    //记录次级按钮的右侧栏
    public GameObject RSideBar;
    private int SecOptionOrderMax;
    private int SecOptionOrderMin;
    //开场动画中的按钮被点击后
    public void BeClickedAni()
    {
        openController parent = transform.GetComponentInParent<openController>();
        //开关覆盖物
        Invoke("OpenCovering", 0.1f);
        Invoke("CloseCovering", 1.2f);//所有动画流程也就1.1秒左右（）
        //当UI按钮被按下后其整体向左移(按下某个具体按钮时，只有一个函数会响应)
        if (parent.preBeClicked == -1)
        {
            //记录这次伸出的（被点击的）id
            parent.preBeClicked = firstButId;
            //整体左移(直接移动面板)
            controller.transform.DOMove(controller.transform.transform.position + new Vector3(-200, 0, 0), 0.5f);
            //首级按钮 的缩入
            transform.DOMove(transform.position + new Vector3(-292.0339f, 0, 0), 0.1f);
            gameObject.SetActive(false);
            //首级被点击按钮 的伸出 
            //Debug.Log("firstButtonBeClicked" + firstButId);
            thisFirButBeClicked.SetActive(true);
            thisFirButBeClicked.transform.DOMove(thisFirButBeClicked.transform.position - new Vector3(-200, 0, 0), 0.5f);

            //展开次级按钮界面
            SecOptionAssign(firstButId, out SecOptionOrderMin, out SecOptionOrderMax); //设置计数器
            //获取右边栏
            parent.RSideBar.SetActive(true);
            RSideBar = parent.RSideBar;
            //设置右边栏位置
            if (parent.secButNum[firstButId] % 2 == 0)
            {
                RSideBar.transform.localPosition = new Vector3(transform.localPosition.x + 220, transform.GetComponent<Transform>().localPosition.y - (parent.secButNum[firstButId] * 17.5f), transform.localPosition.z);
            }
            else
            {
                RSideBar.transform.localPosition = new Vector3(transform.localPosition.x + 220, transform.GetComponent<Transform>().localPosition.y - ((parent.secButNum[firstButId]-1) * 17.5f), transform.localPosition.z);
            }
            //右边栏动画，完成后接次级按钮动;
            RSideBar.transform.DOScaleY(parent.secButNum[firstButId] * 0.072f, 0.5f).OnComplete(SecOptionAni);
            return;
        }

        //收回前一次被点击的
        SecOptionAssign(parent.preBeClicked, out SecOptionOrderMin, out SecOptionOrderMax);
        SecOptionAniBack();
        parent.firstButG[parent.preBeClicked].SetActive(true);
        parent.firstButG[parent.preBeClicked].transform.DOMove(parent.firstButG[parent.preBeClicked].transform.position + new Vector3(220, 0, 0), 0.1f);
        parent.firstButBeClickedG[parent.preBeClicked].transform.GetComponent<Image>().DOColor(new Color(1,1,1,0),0.5f);
        parent.firstButBeClickedG[parent.preBeClicked].transform.DOMove(parent.firstButBeClickedG[parent.preBeClicked].transform.position + new Vector3(-400, 0, 0), 0.5f)
            .OnComplete(() => { parent.firstButBeClickedG[parent.preBeClicked].SetActive(false); });
        //伸出这一次被点击的
        Invoke("invoke1", 0.601f);
        transform.DOMove(transform.position + new Vector3(-220, 0, 0), 0.1f).OnComplete(() => { gameObject.SetActive(false); });
        parent.firstButBeClickedG[firstButId].SetActive(true);
        parent.firstButBeClickedG[firstButId].transform.GetComponent<Image>().color = new Color(1, 1, 1, 1);
        parent.firstButBeClickedG[firstButId].transform.DOMove(parent.firstButBeClickedG[firstButId].transform.position + new Vector3(400, 0, 0), 0.5f);
        //展开次级按钮界面
        //重新设置计数器
        SecOptionAssign(firstButId, out SecOptionOrderMin, out SecOptionOrderMax);
        //获取右边栏
        parent.RSideBar.SetActive(true);
        RSideBar = parent.RSideBar;
        //设置右边栏位置
        if (parent.secButNum[firstButId] % 2 == 0)
        {
            RSideBar.transform.localPosition = new Vector3(transform.localPosition.x + 220, transform.GetComponent<Transform>().localPosition.y - (parent.secButNum[firstButId] * 17.5f), transform.localPosition.z);
        }
        else
        {
            RSideBar.transform.localPosition = new Vector3(transform.localPosition.x + 220, transform.GetComponent<Transform>().localPosition.y - ((parent.secButNum[firstButId] - 1) * 17.5f), transform.localPosition.z);
        }
        //右边栏动画，完成后接次级按钮动;
        RSideBar.transform.DOScaleY(parent.secButNum[firstButId] * 0.072f, 0.5f).OnComplete(SecOptionAni);
    }

    private void invoke1()
    {
        openController parent = transform.GetComponentInParent<openController>();
        parent.preBeClicked = firstButId;
    }

    //次级按钮开始动画
    private void SecOptionAni()
    {
        //结束递归条件
        if (SecOptionOrderMin >= SecOptionOrderMax)
        {
            return;
        }
        openController parent = transform.GetComponentInParent<openController>();
        parent.secButG[SecOptionOrderMin].SetActive(true);
        //循环体
        parent.secButG[SecOptionOrderMin].transform.DOMove(parent.secButG[SecOptionOrderMin].transform.position + new Vector3(200, 0, 0), 0.1f).OnComplete(SecOptionAni);
        SecOptionOrderMin++;
    }

        //收回次级按钮动画
    private void SecOptionAniBack()
    {
        openController parent = transform.GetComponentInParent<openController>();
        for (int i = SecOptionOrderMin; i < SecOptionOrderMax; i++)
        {
            parent.secButG[i].transform.position = parent.secButG[i].transform.position + new Vector3(-200, 0, 0);
            parent.secButG[i].SetActive(false);
        }
    }

    //给计数器赋值
    private void SecOptionAssign(int firstButId, out int SecOptionOrderMin, out int SecOptionOrderMax)
    {
        //重置计数器
        SecOptionOrderMax = 0;
        SecOptionOrderMin = 0;
        //计算逻辑
        openController parent = transform.GetComponentInParent<openController>();
        for (int i = 0; i <= firstButId; i++)
        {
            if(i< parent.secButNum.Count)
            {
                SecOptionOrderMax += parent.secButNum[i];
            }
        }
        SecOptionOrderMin = SecOptionOrderMax - parent.secButNum[firstButId];
    }

    public void OpenCovering()
    {
        openController parent = transform.GetComponentInParent<openController>();
        parent.Covering.SetActive(true);
    }

    public void CloseCovering()
    {
        openController parent = transform.GetComponentInParent<openController>();
        parent.Covering.SetActive(false);
    }

    /// <summary>
    /// 切换至游戏选项界面
    /// </summary>

    public void ToGameOptionsInterface()
    {
        openController parent = transform.GetComponentInParent<openController>();
        //销毁旧开始界面
        Destroy(parent.gameObject);
        StartSceneUIManager.Instance.ControllerDic.Remove(parent.gameObject.name);//删掉 ControllerDic 字典的键值
        //以下的不用销毁
        GameObject.Find("open_BackRround").SetActive(false);
        parent.gameOption.SetActive(true);
    }

    /// <summary>
    /// 游戏选项界面内方法
    /// </summary>

    /// <summary>
    /// 左侧栏按钮方法
    /// </summary>
    //用于记录游戏选项界面的左侧栏按钮的id
    public int LsideBarButId;
    //次顶栏文本的切换
    public void changeSecTopBarText()
    {
        GameObject.Find("secTopBarText").GetComponent<Text>().text = transform.GetChild(0).GetComponent<Text>().text;
    }

    //mainbody的interface界面的切换
    //记录下一次interface
    private int nextInterface;
    public void changeMainbodyInterface()
    {        
        nextInterface = this.LsideBarButId;  //点击某个左侧按钮后，记录该按钮
        //Debug.Log("preInterface：" + preInterface);
        //Debug.Log("nextInterface："+ nextInterface);
        //所有界面都失活得了
        for(int i=0; i < GameObject.Find("mainBody").transform.childCount; i++)
        {
            GameObject.Find("mainBody").transform.GetChild(i).gameObject.SetActive(false);
        }
        //下一次界面激活
        GameObject.Find("mainBody").transform.GetChild(nextInterface).gameObject.SetActive(true); //左侧按钮的id跟interface界面的子物体顺序相同
    }



    //返回open界面
    
    public void backToOpen()
    {     
        gameOptionController parent = (gameOptionController)StartSceneUIManager.Instance.GetInterface("gameOption");
        //实例化一个新开始界面（重置它的全部东西）
        GameObject open_interface = Instantiate(parent.openPerfab, parent.gameObject.transform.parent.transform);
        open_interface.name = "open";
        //以下的不用实例化

        //测试
        GameObject e = GameObject.Find("Canvas").transform.GetChild(0).gameObject;
        e.SetActive(true);
        GameObject o = e.transform.GetChild(0).gameObject;
        o.SetActive(true);

        parent.gameOption.SetActive(false);
    }
    

    //option界面的Toggle脚本（自己用按钮改的）
    public Color Toggle_color1;
    public Color Toggle_color2;
    public GameObject CheckMark;
    public void Toggle_test()
    {
        Debug.Log("按下去咯~");
        //CheckMark = this.transform.GetChild(2).transform.GetChild(1).gameObject;
        if (GetComponent<Image>().color == Toggle_color1)
        {
            GetComponent<Image>().color = Toggle_color2;
            CheckMark.SetActive(true);
        }
        else
        {
            GetComponent<Image>().color = Toggle_color1;
            CheckMark.SetActive(false);
        }
    }

    //弹窗（mainbody5 UI控件 事件）
    public GameObject Blocker;
    public GameObject Popup;
    private string mainbody5_text;
    private int BindIndex; //用来判断哪个点击的是绑定框
    public void Pop_up()
    {
        //实例化弹窗
        GameObject blocker;
        GameObject popup;
        blocker = Instantiate(Blocker, GameObject.Find("Canvas").transform);
        popup = Instantiate(Popup, GameObject.Find("Canvas").transform);

        _container.InjectGameObject(popup);
        _container.InjectGameObject(blocker);

        blocker.name = "black_Pop up blocker";
        popup.name = "Pop up";
        //为弹窗按钮添加事件
        popup.transform.GetChild(3).transform.GetChild(0).GetComponent<UIControl>().AddButtonClickEvent(Popup_cancle);
        popup.transform.GetChild(4).transform.GetChild(0).GetComponent<UIControl>().AddButtonClickEvent(Popup_remove);
        popup.transform.GetChild(4).transform.GetChild(0).GetComponent<UIControl>().AddButtonClickEvent(PlayButtonSoundEffect);
        //用来判断哪个点击的是绑定框
        if (this.gameObject.name == "priButton")
        {
            BindIndex = 0;
            GameObject.Find("secButton").transform.GetComponent<UIControl>().BindIndex = 0;
        }
        else if(this.gameObject.name == "secButton")
        {
            BindIndex = 1;
            GameObject.Find("priButton").transform.GetComponent<UIControl>().BindIndex = 1;
        }
        //设置绑定框对应的弹窗title
        popup.transform.GetChild(1).transform.GetComponent<Text>().text= this.transform.parent.transform.GetChild(0).GetComponent<Text>().text;
    }

    //弹窗按钮事件_cancle    //暂时写为返回上次页面
    public void Popup_cancle()
    {
        Destroy(GameObject.Find("black_Pop up blocker"));
        Destroy(GameObject.Find("Pop up"));
    }

    //弹窗按钮事件_remove    //暂时写为返回上次页面
    public void Popup_remove()
    {
        Destroy(GameObject.Find("black_Pop up blocker"));
        Destroy(GameObject.Find("Pop up"));
    }


    /// <summary>
    /// 切换至游戏内场景
    /// </summary>
    public void ToGameScene()
    {
        _audioManager.StopBGM();

        SceneManager.LoadScene(1);

        //_audioManager.PlayBGM("Theme_Mistery_But_Then_Happy_Loop");
    }

    //播放按钮音效
    public void PlayButtonSoundEffect()
    {
        _audioManager.PlaySFX("Retro2");
    }

}


