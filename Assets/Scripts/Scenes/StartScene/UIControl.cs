using DG.Tweening;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Zenject;
using UIToolkitDemo;
//using HighlightingSystem;
//using UnityEditor;

//****************************************
//创建人：易生
//功能说明：
//****************************************

public class UIControl : MonoBehaviour
{
    [Inject] private AudioManager _audioManager;

    //用于判断该脚本属于哪个页面
    public StartSceneUIController controller;

    void Awake()
    {
        if (_audioManager == null)
        {
            ProjectContext.Instance.Container.Inject(this);
        }

        //返回 StartScene（场景重新加载）时复位场景加载锁，静态字段不会随场景卸载自动重置
        _isLoadingGameScene = false;

        //以向上查找的方式获取父页面
        if (transform.parent != null)
        {
            //获取父物体的UIController脚本
            controller = transform.GetComponentInParent<StartSceneUIController>();
            //若父物体存在该脚本
            if(controller != null )
            {
                //有脚本，找到了父页面，添加该组件到其字典内（防重复注册，避免同名控件触发 ArgumentException）
                if (controller.ControlDic.ContainsKey(transform.name))
                {
                    Debug.LogWarning($"[UIControl] 页面 \"{controller.name}\" 已存在同名控件 \"{transform.name}\"，跳过重复注册。", this);
                }
                else
                {
                    controller.ControlDic.Add(transform.name, this);
                }
                //Debug.Log(this + "找到了父页面"+ controller);
            }
            else
            {
                Debug.Log(this + "没找到父页面");
            }
        }
    }

    void Update()
    {
        //只有按键绑定弹窗的两个绑定按钮（priButton/secButton）才需要监听键盘输入；
        //其余大量控件（按钮、滑条、开关、输入框）都挂了 UIControl，若都执行会每帧多次全场景 Find("Pop up")，
        //既是无谓的性能开销，也让普通控件执行了本应只属于弹窗的逻辑。
        if (name != "priButton" && name != "secButton")
        {
            return;
        }

        //这是游戏设定界面的interface的按钮绑定的那个
        if (GameObject.Find("Pop up"))
        {
            string input = Input.inputString;//键盘输入的帧监测（只会监测一帧）
            if (input != "")
            {
                mainbody5_text = input;
                if (BindIndex == 0)
                {
                    SetBindButtonText("priButton", mainbody5_text);
                }
                else if (BindIndex == 1)
                {
                    SetBindButtonText("secButton", mainbody5_text);
                }
                Destroy(GameObject.Find("black_Pop up blocker"));
                Destroy(GameObject.Find("Pop up"));
            }
        }
    }

    //按名称找到绑定按钮并写入其显示文本（对每一层查找结果判空，避免层级/命名改变时空引用崩溃）
    private static void SetBindButtonText(string buttonName, string text)
    {
        GameObject btn = GameObject.Find(buttonName);
        if (btn == null || btn.transform.childCount < 2)
        {
            Debug.LogWarning($"[UIControl] 未找到绑定按钮 \"{buttonName}\" 或其子节点不足，无法更新绑定文本。");
            return;
        }
        Text label = btn.transform.GetChild(1).GetComponent<Text>();
        if (label == null)
        {
            Debug.LogWarning($"[UIControl] 绑定按钮 \"{buttonName}\" 的子节点缺少 Text 组件。");
            return;
        }
        label.text = text;
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
        OpenCovering();
        CancelInvoke("CloseCovering");
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
            DOTween.Kill(parent.secButG[i].transform);
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
        if (parent == null || parent.gameOption == null)
        {
            return;
        }
        //防重复点击：gameOption 已激活说明已切换过，避免重复销毁 open 与重复激活
        if (parent.gameOption.activeSelf)
        {
            return;
        }
        DOTween.Kill(parent.gameObject.transform);
        Destroy(parent.gameObject);
        //删掉 ControllerDic 字典的键值（单例可能因初始化时序未就绪，先判空）
        if (StartSceneUIManager.Instance != null)
        {
            StartSceneUIManager.Instance.ControllerDic.Remove(parent.gameObject.name);
        }
        //以下的不用销毁
        GameObject openBack = GameObject.Find("open_BackRround");
        if (openBack != null)
        {
            openBack.SetActive(false);
        }
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
        GameObject secTopBar = GameObject.Find("secTopBarText");
        if (secTopBar == null)
        {
            Debug.LogWarning("[UIControl] 未找到 \"secTopBarText\"，无法切换次顶栏文本。", this);
            return;
        }
        secTopBar.GetComponent<Text>().text = transform.GetChild(0).GetComponent<Text>().text;
    }

    //mainbody的interface界面的切换
    //记录下一次interface
    private int nextInterface;
    public void changeMainbodyInterface()
    {
        nextInterface = this.LsideBarButId;  //点击某个左侧按钮后，记录该按钮
        //Debug.Log("preInterface：" + preInterface);
        //Debug.Log("nextInterface："+ nextInterface);
        GameObject mainBody = GameObject.Find("mainBody");
        if (mainBody == null)
        {
            Debug.LogWarning("[UIControl] 未找到 \"mainBody\"，无法切换 interface 界面。", this);
            return;
        }
        //所有界面都失活得了
        for(int i=0; i < mainBody.transform.childCount; i++)
        {
            mainBody.transform.GetChild(i).gameObject.SetActive(false);
        }
        //下一次界面激活（左侧按钮的id跟interface界面的子物体顺序相同）
        if (nextInterface < 0 || nextInterface >= mainBody.transform.childCount)
        {
            Debug.LogWarning($"[UIControl] interface 索引 {nextInterface} 超出 mainBody 子物体范围（{mainBody.transform.childCount}），已忽略。", this);
            return;
        }
        mainBody.transform.GetChild(nextInterface).gameObject.SetActive(true);
    }



    //返回open界面
    
    public void backToOpen()
    {
        //防重复点击：已存在 open 界面时不再重复实例化，否则会出现两个同名 open，
        //其 StartSceneUIController.Start() 重复向 ControllerDic 注册同名键而抛 ArgumentException
        if (GameObject.Find("open") != null)
        {
            return;
        }
        if (StartSceneUIManager.Instance == null)
        {
            Debug.LogError("[UIControl] backToOpen 时 StartSceneUIManager.Instance 为空，无法返回开始界面。", this);
            return;
        }
        gameOptionController parent = (gameOptionController)StartSceneUIManager.Instance.GetInterface("gameOption");
        if (parent == null)
        {
            Debug.LogError("[UIControl] backToOpen 找不到 gameOption 页面，无法返回开始界面。");
            return;
        }
        //实例化一个新开始界面（重置它的全部东西）
        GameObject open_interface = Instantiate(parent.openPerfab, parent.gameObject.transform.parent.transform);
        open_interface.name = "open";
        //以下的不用实例化

        //测试
        GameObject canvas = GameObject.Find("Canvas");
        if (canvas != null && canvas.transform.childCount > 0)
        {
            GameObject e = canvas.transform.GetChild(0).gameObject;
            e.SetActive(true);
            if (e.transform.childCount > 0)
            {
                GameObject o = e.transform.GetChild(0).gameObject;
                o.SetActive(true);
            }
        }
        else
        {
            Debug.LogWarning("[UIControl] backToOpen 未找到 Canvas 或其子物体，跳过背景大字恢复。", this);
        }

        DOTween.Kill(parent.gameOption.transform);
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
        //防重复点击：已存在弹窗时不再创建第二个（否则按名称关闭只能处理其中一份，留下死弹窗）
        if (GameObject.Find("Pop up") != null)
        {
            return;
        }

        Transform canvas = GameObject.Find("Canvas")?.transform;
        if (canvas == null)
        {
            Debug.LogError("无法打开按键绑定弹窗：场景中未找到 Canvas。");
            return;
        }

        if (Blocker == null || Popup == null)
        {
            Debug.LogError("无法打开按键绑定弹窗：Blocker 或 Popup 预制体未配置。", this);
            return;
        }

        // 弹窗不依赖注入服务，直接实例化即可。动态创建的 UIControl 会自行获取 AudioManager。
        GameObject blocker = Instantiate(Blocker, canvas);
        GameObject popup = Instantiate(Popup, canvas);

        if (popup == null || blocker == null)
        {
            if (popup != null) Destroy(popup);
            if (blocker != null) Destroy(blocker);
            Debug.LogError("无法打开按键绑定弹窗：弹窗实例化失败。", this);
            return;
        }

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
    //防止快速连点重复加载场景（静态锁，加载后即使按钮仍在也不会再次触发）
    private static bool _isLoadingGameScene;
    public void ToGameScene()
    {
        if (_isLoadingGameScene)
        {
            return;
        }
        _isLoadingGameScene = true;

        if (_audioManager != null)
        {
            _audioManager.StopBGM();
        }

        SceneManager.LoadScene(1);

        //_audioManager.PlayBGM("Theme_Mistery_But_Then_Happy_Loop");
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        Debug.Log("[UIControl] 退出游戏仅在 Player 中生效。");
#elif UNITY_WEBGL && !UNITY_EDITOR
        Debug.Log("[UIControl] 微信小游戏无法通过 Application.Quit 退出，请使用微信返回手势。");
#else
        Application.Quit();
#endif
    }

    //播放按钮音效
    public void PlayButtonSoundEffect()
    {
        _audioManager.PlaySFX("Retro2");
    }

}


