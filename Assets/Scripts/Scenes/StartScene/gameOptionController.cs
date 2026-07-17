using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using TMPro;


public class gameOptionController : StartSceneUIController
{
    /// <summary>
    /// option界面的初始化设置
    /// </summary>
    public GameObject LsideBarBut;
    //左侧栏按钮的文本
    public List<string> LsideBarButText;
    //左侧栏按钮组
    [HideInInspector]
    public List<GameObject> LsideBarButG;
    //游戏选项界面
    [HideInInspector]
    public GameObject gameOption;
    //开始界面
    [HideInInspector]
    public GameObject open;
    //开始界面预制体
    public GameObject openPerfab;
    //开始界面的背景大字
    //[HideInInspector]
    public GameObject open_BackRround;

    /// <summary>
    /// option界面的mainbody部分
    /// </summary>
    //六种UI控件的预制体
    public GameObject DropDown;
    public GameObject InputField;
    public GameObject Slider;
    public GameObject Toggle;
    public GameObject Text;
    public GameObject Bind;
    //输错UI控件名时的空物体
    public GameObject nothing;
    [Header("interface0")]
    [TextArea]
    public string explain = "可选六种UI控件：DropDown(d)、InputField(i)、Slider(s)、Toggle(to)、Bind(b)、Text(te)（输入其小写首字母即可）";
    /// <summary>
    /// option界面mainbody部分的interface0
    /// </summary>
    //interface0界面的UI控件顺序
    public List<string> mainbodyInterface0Type = new List<string>();
    //interface0界面的UI控件组
    private List<GameObject> interface0 = new List<GameObject>();
    //UI控件的文本组
    //DropDowm
    public List<string> DropDown_textTitle0 = new List<string>();//下拉框的标题
    //private int i_dt0 = 0;//对应的索引
    public List<int> eachDropdowmOptionNum0 = new List<int>();//每个下拉框对应的选项数量
    //private int eDON0 = 0;
    public List<string> DropDown_textLabe0 = new List<string>(); //下拉框的选项文本
    //private int i_dl0 = 0;
    //InputField
    public List<string> InputField_text0 = new List<string>();
    //private int i_i0 = 0;
    //Slider
    public List<string> Slider_text0 = new List<string>();
    //private int i_s0 = 0;
    //Toggle
    public List<string> Toggle_text0 = new List<string>();
    //private int i_t0 = 0;
    //Bind
    public List<string> Bind_pritext0 = new List<string>(); //绑定框内容文本
    public List<string> Bind_label0 = new List<string>();   //绑定框标签文本
    //Text
    public List<string> Text_text0 = new List<string>();

    /// <summary>
    /// option界面mainbody部分的interface1
    /// </summary>
    [Header("interface1")]
    //interface0界面的UI控件顺序
    public List<string> mainbodyInterface1Type = new List<string>();
    //interface0界面的UI控件组
    private List<GameObject> interface1 = new List<GameObject>();
    //UI控件的文本组
    //DropDowm
    public List<string> DropDown_textTitle1 = new List<string>();//下拉框的标题
    public List<int> eachDropdowmOptionNum1 = new List<int>();//每个下拉框对应的选项数量
    public List<string> DropDown_textLabe1 = new List<string>(); //下拉框的选项文本
    //InputField
    public List<string> InputField_text1 = new List<string>();
    //Slider
    public List<string> Slider_text1 = new List<string>();
    //Toggle
    public List<string> Toggle_text1 = new List<string>();
    //Bind
    public List<string> Bind_pritext1 = new List<string>(); //绑定框内容文本
    public List<string> Bind_label1 = new List<string>();   //绑定框标签文本
    //Text
    public List<string> Text_text1 = new List<string>();

    /// <summary>
    /// option界面mainbody部分的interface2
    /// </summary>
    [Header("interface2")]
    //interface0界面的UI控件顺序
    public List<string> mainbodyInterface2Type = new List<string>();
    //interface0界面的UI控件组
    private List<GameObject> interface2 = new List<GameObject>();
    //UI控件的文本组
    //DropDowm
    public List<string> DropDown_textTitle2 = new List<string>();//下拉框的标题
    public List<int> eachDropdowmOptionNum2 = new List<int>();//每个下拉框对应的选项数量
    public List<string> DropDown_textLabe2 = new List<string>(); //下拉框的选项文本
    //InputField
    public List<string> InputField_text2 = new List<string>();
    //Slider
    public List<string> Slider_text2 = new List<string>();
    //Toggle
    public List<string> Toggle_text2 = new List<string>();
    //Bind
    public List<string> Bind_pritext2 = new List<string>(); //绑定框内容文本
    public List<string> Bind_label2 = new List<string>();   //绑定框标签文本
    //Text
    public List<string> Text_text2 = new List<string>();

    /// <summary>
    /// option界面mainbody部分的interface3
    /// </summary>
    [Header("interface3")]
    //interface0界面的UI控件顺序
    public List<string> mainbodyInterface3Type = new List<string>();
    //interface0界面的UI控件组
    private List<GameObject> interface3 = new List<GameObject>();
    //UI控件的文本组
    //DropDowm
    public List<string> DropDown_textTitle3 = new List<string>();//下拉框的标题
    public List<int> eachDropdowmOptionNum3 = new List<int>();//每个下拉框对应的选项数量
    public List<string> DropDown_textLabe3 = new List<string>(); //下拉框的选项文本
    //InputField
    public List<string> InputField_text3 = new List<string>();
    //Slider
    public List<string> Slider_text3 = new List<string>();
    //Toggle
    public List<string> Toggle_text3 = new List<string>();
    //Bind
    public List<string> Bind_pritext3 = new List<string>(); //绑定框内容文本
    public List<string> Bind_label3 = new List<string>();   //绑定框标签文本
    //Text
    public List<string> Text_text3 = new List<string>();

    /// <summary>
    /// option界面mainbody部分的interface4
    /// </summary>
    [Header("interface4")]
    //interface0界面的UI控件顺序
    public List<string> mainbodyInterface4Type = new List<string>();
    //interface0界面的UI控件组
    private List<GameObject> interface4 = new List<GameObject>();
    //UI控件的文本组
    //DropDowm
    public List<string> DropDown_textTitle4 = new List<string>();//下拉框的标题
    public List<int> eachDropdowmOptionNum4 = new List<int>();//每个下拉框对应的选项数量
    public List<string> DropDown_textLabe4 = new List<string>(); //下拉框的选项文本
    //InputField
    public List<string> InputField_text4 = new List<string>();
    //Slider
    public List<string> Slider_text4 = new List<string>();
    //Toggle
    public List<string> Toggle_text4 = new List<string>();
    //Bind
    public List<string> Bind_pritext4 = new List<string>(); //绑定框内容文本
    public List<string> Bind_label4 = new List<string>();   //绑定框标签文本
    //Text
    public List<string> Text_text4 = new List<string>();

    /// <summary>
    /// option界面mainbody部分的interface5
    /// </summary>
    [Header("interface5")]
    //interface0界面的UI控件顺序
    public List<string> mainbodyInterface5Type = new List<string>();
    //interface0界面的UI控件组
    private List<GameObject> interface5 = new List<GameObject>();
    //UI控件的文本组
    //DropDowm
    public List<string> DropDown_textTitle5 = new List<string>();//下拉框的标题
    public List<int> eachDropdowmOptionNum5 = new List<int>();//每个下拉框对应的选项数量
    public List<string> DropDown_textLabe5 = new List<string>(); //下拉框的选项文本
    //InputField
    public List<string> InputField_text5 = new List<string>();
    //Slider
    public List<string> Slider_text5 = new List<string>();
    //Toggle
    public List<string> Toggle_text5 = new List<string>();
    //Bind
    public List<string> Bind_pritext5 = new List<string>(); //绑定框内容文本
    public List<string> Bind_label5 = new List<string>();   //绑定框标签文本
    //Text
    public List<string> Text_text5 = new List<string>();

    /// <summary>
    /// option界面mainbody部分的interface6
    /// </summary>
    [Header("interface6")]
    //interface0界面的UI控件顺序
    public List<string> mainbodyInterface6Type = new List<string>();
    //interface0界面的UI控件组
    private List<GameObject> interface6 = new List<GameObject>();
    //UI控件的文本组
    //DropDowm
    public List<string> DropDown_textTitle6 = new List<string>();//下拉框的标题
    public List<int> eachDropdowmOptionNum6 = new List<int>();//每个下拉框对应的选项数量
    public List<string> DropDown_textLabe6 = new List<string>(); //下拉框的选项文本
    //InputField
    public List<string> InputField_text6 = new List<string>();
    //Slider
    public List<string> Slider_text6 = new List<string>();
    //Toggle
    public List<string> Toggle_text6 = new List<string>();
    //Bind
    public List<string> Bind_pritext6 = new List<string>(); //绑定框内容文本
    public List<string> Bind_label6 = new List<string>();   //绑定框标签文本
    //Text
    public List<string> Text_text6 = new List<string>();

    //整个gameOption所有控件加起来的数量
    //private List<int> interfacesUiContrlSum = new List<int>();
    //private int ifUiCSIndex = 0;//索引

    /// <summary>
    /// option界面mainbody部分的interface5  按键绑定_弹窗（mainbody5 UI控件 事件）Test!!!
    /// </summary>
    public Button one;
    public Button two;

    void Awake()
    {
        /// <summary>
        /// option界面的初始化设置
        /// </summary>
        //先让其他界面失活
        open = GameObject.Find("open");//开始界面
        gameOption = GameObject.Find("gameOption");//游戏选择界面
        //open_BackRround = GameObject.Find("open_BackRround");//开始界面的背景大字
        //实例化外左侧栏按钮
        for (int i = 0; i < LsideBarButText.Count; i++)
        {
            //实例化按钮，并将其父物体设置为左侧栏
            LsideBarButG.Add(Instantiate(LsideBarBut, transform.GetChild(0).GetChild(2).transform));
            //设置左侧栏按钮的名字（方便UIController的字典）
            LsideBarButG[i].gameObject.name = "LsideBarBut" + i;
            //设置左侧栏按钮位置
            LsideBarButG[i].transform.position += new Vector3(0, i*(-40), 0);
            //设置左侧栏按钮文本
            LsideBarButG[i].transform.GetChild(0).GetComponent<Text>().text = LsideBarButText[i];
            //为左侧栏按钮加上 UIControl
            LsideBarButG[i].AddComponent<UIControl>();
            //左侧栏按钮的id
            LsideBarButG[i].transform.GetComponent<UIControl>().LsideBarButId = i;
            //为左侧栏按钮添加事件
            LsideBarButG[i].transform.GetComponent<UIControl>().AddButtonClickEvent(LsideBarButG[i].transform.GetComponent<UIControl>().changeSecTopBarText);//次顶栏文本切换
            LsideBarButG[i].transform.GetComponent<UIControl>().AddButtonClickEvent(LsideBarButG[i].transform.GetComponent<UIControl>().changeMainbodyInterface);//mainbody的interface界面切换
            LsideBarButG[i].transform.GetComponent<UIControl>().AddButtonClickEvent(LsideBarButG[i].transform.GetComponent<UIControl>().PlayButtonSoundEffect);//次顶栏文本切换
        }
        //为次顶栏按钮添加 UIControl
        GameObject backBut = transform.GetChild(0).GetChild(1).GetChild(2).gameObject;
        backBut.AddComponent<UIControl>();
        //为次顶栏按钮添加事件
        backBut.transform.GetComponent<UIControl>().AddButtonClickEvent(backBut.transform.GetComponent<UIControl>().backToOpen);
        backBut.transform.GetComponent<UIControl>().AddButtonClickEvent(backBut.transform.GetComponent<UIControl>().PlayButtonSoundEffect);

        /// <summary>
        /// option界面mainbody部分的所有interface的UI控件的实例化
        /// </summary>
        //没必要做成循环了，写成函数得了
        //interface0
        InsInterfaceUIContrl
            (
            mainbodyInterface0Type,
            interface0,
            DropDown_textTitle0,
            eachDropdowmOptionNum0,
            DropDown_textLabe0,
            InputField_text0,
            Slider_text0,
            Toggle_text0,
            Bind_pritext0,
            Bind_label0,
            Text_text0,
            "interface0"
            );
        //interface1
        InsInterfaceUIContrl
            (
            mainbodyInterface1Type,
            interface1,
            DropDown_textTitle1,
            eachDropdowmOptionNum1,
            DropDown_textLabe1,
            InputField_text1,
            Slider_text1,
            Toggle_text1,
            Bind_pritext1,
            Bind_label1,
            Text_text1,
            "interface1"
            );

        //interface2
        InsInterfaceUIContrl
            (
            mainbodyInterface2Type,
            interface2,
            DropDown_textTitle2,
            eachDropdowmOptionNum2,
            DropDown_textLabe2,
            InputField_text2,
            Slider_text2,
            Toggle_text2,
            Bind_pritext2,
            Bind_label2,
            Text_text2,
            "interface2"
            );

        //interface3
        InsInterfaceUIContrl
            (
            mainbodyInterface3Type,
            interface3,
            DropDown_textTitle3,
            eachDropdowmOptionNum3,
            DropDown_textLabe3,
            InputField_text3,
            Slider_text3,
            Toggle_text3,
            Bind_pritext3,
            Bind_label3,
            Text_text3,
            "interface3"
            );

        //interface4
        InsInterfaceUIContrl
            (
            mainbodyInterface4Type,
            interface4,
            DropDown_textTitle4,
            eachDropdowmOptionNum4,
            DropDown_textLabe4,
            InputField_text4,
            Slider_text4,
            Toggle_text4,
            Bind_pritext4,
            Bind_label4,
            Text_text4,
            "interface4"
            );

        //interface5
        InsInterfaceUIContrl
            (
            mainbodyInterface5Type,
            interface5,
            DropDown_textTitle5,
            eachDropdowmOptionNum5,
            DropDown_textLabe5,
            InputField_text5,
            Slider_text5,
            Toggle_text5,
            Bind_pritext5,
            Bind_label5,
            Text_text5,
            "interface5"
            );

        //interface6
        InsInterfaceUIContrl
            (
            mainbodyInterface6Type,
            interface6,
            DropDown_textTitle6,
            eachDropdowmOptionNum6,
            DropDown_textLabe6,
            InputField_text6,
            Slider_text6,
            Toggle_text6,
            Bind_pritext6,
            Bind_label6,
            Text_text6,
            "interface6"
            );

        //全部UI控件都实例化完成后。除默认interface界面，都失活
        for (int i = 1; i <= 6; i++)
        {
            GameObject itf = GameObject.Find("interface" + i);
            if (itf != null)
            {
                itf.SetActive(false);
            }
            else
            {
                Debug.LogWarning($"[gameOptionController] 未找到 \"interface{i}\"，无法将其失活，请检查预制体层级或命名。", this);
            }
        }


    }


    void Update()
    {

    }

    /// <summary>
    /// 获取目标物体上的 UIControl，没有则添加（避免直接 GetComponent 返回 null 导致空引用）
    /// </summary>
    private static UIControl GetOrAddUIControl(GameObject target)
    {
        return target.TryGetComponent<UIControl>(out var control)
            ? control
            : target.AddComponent<UIControl>();
    }

    /// <summary>
    /// 实例化一个interface界面的所有UI控件的方法
    /// </summary>
    private void InsInterfaceUIContrl(
        List<string> mainbodyInterfaceType,
        List<GameObject> m_interface,
        List<string> DropDown_textTitle,
        List<int> eachDropdowmOptionNum,
        List<string> DropDown_textLabe,
        List<string> InputField_text,
        List<string> Slider_text,
        List<string> Toggle_text,
        List<string> Bind_pritext,
        List<string> Bind_label,
        List<string> Text_text,

    string parents,

        int i_dt = 0,
        int eDON = 0,
        int i_dl = 0,
        int i_i = 0,
        int i_s = 0,
        int i_to = 0,
        int i_b = 0,
        int i_bl = 0,
        int i_te = 0
        )
    {
        //父物体只查一次，找不到直接返回并告警（原先每个分支都 GameObject.Find(parents)，既低效又缺乏判空）
        GameObject parentObj = GameObject.Find(parents);
        if (parentObj == null)
        {
            Debug.LogError($"[gameOptionController] 未找到父界面 \"{parents}\"，跳过其 UI 控件实例化。", this);
            return;
        }
        Transform parentTf = parentObj.transform;

        //实例化UI控件，并将其父物体设置为m_interface
        for (int i = 0; i < mainbodyInterfaceType.Count; i++)
        {
            //current 指向本次循环真正实例化出来的控件对象，避免用 m_interface[i] 的“下标==循环序号”耦合：
            //一旦跳过未知类型（不再往列表塞共享 nothing），下标耦合就会错位甚至越界。
            GameObject current;

            if (mainbodyInterfaceType[i] == "d")
            {
                current = Instantiate(DropDown, parentTf);
                if (eDON < eachDropdowmOptionNum.Count)  //设置单个下拉框的选项个数
                {
                    for (int j = 0; j < eachDropdowmOptionNum[eDON]; j++)  //设置单个下拉框的每个选项
                    {
                        TMP_Dropdown.OptionData option = new TMP_Dropdown.OptionData();
                        current.transform.GetComponent<TMP_Dropdown>().options.Add(option);
                    }
                }
                else  //下拉框选项的数量设置不足
                {
                    Debug.Log("下拉框选项数量设置不足");
                }
            }
            else if (mainbodyInterfaceType[i] == "i")
            {
                current = Instantiate(InputField, parentTf);
            }
            else if (mainbodyInterfaceType[i] == "s")
            {
                current = Instantiate(Slider, parentTf);
            }
            else if (mainbodyInterfaceType[i] == "to")
            {
                current = Instantiate(Toggle, parentTf);
                if (current.TryGetComponent<Image>(out var toggleImage))
                {
                    toggleImage.color = new Vector4(0.2588235f, 0.4156863f, 0.7294118f, 0.2313726f);  //初始化Toggle按钮
                }
                GetOrAddUIControl(current).AddButtonClickEvent(GetOrAddUIControl(current).Toggle_test);  //给Toggle按钮添加事件
            }
            else if(mainbodyInterfaceType[i] == "b")
            {
                current = Instantiate(Bind, parentTf);
                //Bind 预制体依赖固定的子节点结构（GetChild(1)/(2) 为主/次绑定按钮），先校验子节点数量再接线
                if (current.transform.childCount >= 3)
                {
                    UIControl priBtn = GetOrAddUIControl(current.transform.GetChild(1).gameObject);
                    UIControl secBtn = GetOrAddUIControl(current.transform.GetChild(2).gameObject);
                    priBtn.AddButtonClickEvent(priBtn.Pop_up);
                    secBtn.AddButtonClickEvent(secBtn.Pop_up);
                    secBtn.AddButtonClickEvent(secBtn.PlayButtonSoundEffect);
                }
                else
                {
                    Debug.LogError($"[gameOptionController] Bind 预制体子节点不足（需要至少 3 个，实际 {current.transform.childCount} 个），" +
                                   $"界面 \"{parents}\" 第 {i} 项跳过按钮接线。", current);
                }
            }
            else if (mainbodyInterfaceType[i] == "te")
            {
                current = Instantiate(Text, parentTf);
            }
            else
            {
                //未知类型码：输出定位信息并跳过，绝不把共享的 nothing 占位对象当作有效控件反复改名/移动/加组件
                Debug.LogError($"[gameOptionController] 未知 UI 控件类型码 \"{mainbodyInterfaceType[i]}\"（界面 \"{parents}\"，第 {i} 项），已跳过。", this);
                continue;
            }

            m_interface.Add(current);
            //设置UI控件的名字（方便UIController的字典）
            current.gameObject.name = parents + "_UI" + i;
            //设置UI控件位置
            current.transform.position += new Vector3(0, i * (-35), 0);
            //设置UI控件文本
            if (mainbodyInterfaceType[i] == "d")  //设置下拉框文本
            {
                if (i_dt < DropDown_textTitle.Count)
                {
                    current.transform.GetChild(0).GetComponent<Text>().text = DropDown_textTitle[i_dt++];//设置下拉框的标题文本
                    if (eDON < eachDropdowmOptionNum.Count)  //对应的下拉框设有选项个数
                    {
                        for (int j = 0; j < eachDropdowmOptionNum[eDON]; j++)  //单个下拉框的每个选项
                        {
                            if (i_dl < DropDown_textLabe.Count)  //设置单个下拉框的单个选项文本
                            {
                                current.transform.GetComponent<TMP_Dropdown>().options[j].text = DropDown_textLabe[i_dl++];
                            }
                            else  //下拉框的选项文本量设置不足
                            {
                                current.transform.GetComponent<TMP_Dropdown>().options[j].text = "Please enter text";
                            }
                        }
                    }
                    else  //下拉框选项的数量设置不足
                    {
                        Debug.Log("下拉框选项数量设置不足");
                    }
                }
                else  //下拉框标题的文本设置不足
                {
                    current.transform.GetChild(0).GetComponent<Text>().text = "请输入文本";
                    //以下同上理
                    if (eDON < eachDropdowmOptionNum.Count)  //对应的下拉框设有选项个数
                    {
                        for (int j = 0; j < eachDropdowmOptionNum[eDON]; j++)  //单个下拉框的每个选项
                        {
                            if (i_dl < DropDown_textLabe.Count)  //设置单个下拉框的单个选项文本
                            {
                                current.transform.GetComponent<TMP_Dropdown>().options[j].text = DropDown_textLabe[i_dl++];
                            }
                            else  //下拉框的选项文本量设置不足
                            {
                                current.transform.GetComponent<TMP_Dropdown>().options[j].text = "Please enter text";
                            }
                        }
                    }
                    else  //下拉框选项的数量设置不足
                    {
                        Debug.Log("下拉框选项数量设置不足");
                    }
                }
                eDON++;  //一次循环中有关下拉框的代码全部执行完毕后，索引++
            }
            else if (mainbodyInterfaceType[i] == "i")  //设置输入框
            {
                if (i_i < InputField_text.Count)
                {
                    current.transform.GetChild(1).GetComponent<Text>().text = InputField_text[i_i++];
                }
                else
                {
                    current.transform.GetChild(1).GetComponent<Text>().text = "请输入文本";
                }
            }
            else if (mainbodyInterfaceType[i] == "s")  //设置滑条
            {
                if (i_s < Slider_text.Count)
                {
                    current.transform.GetChild(2).GetComponent<Text>().text = Slider_text[i_s++];
                }
                else
                {
                    current.transform.GetChild(2).GetComponent<Text>().text = "请输入文本";
                }
            }
            else if (mainbodyInterfaceType[i] == "to")  //设置选择框
            {
                if (i_to < Toggle_text.Count)
                {
                    current.transform.GetChild(1).GetComponent<Text>().text = Toggle_text[i_to++];
                }
                else
                {
                    current.transform.GetChild(1).GetComponent<Text>().text = "请输入文本";
                }
            }
            else if (mainbodyInterfaceType[i] == "b")  //设置绑定框
            {
                //Bind 深层子节点访问前再次校验层级，避免结构改变时 GetChild 越界
                if (current.transform.childCount >= 2 && current.transform.GetChild(1).childCount >= 2)
                {
                    //设置绑定框内容文本
                    if (i_b < Bind_pritext.Count)
                    {
                        current.transform.GetChild(1).GetChild(1).GetComponent<Text>().text = Bind_pritext[i_b++];
                    }
                    else
                    {
                        current.transform.GetChild(1).GetChild(1).GetComponent<Text>().text = "请输入文本";
                    }
                    //设置绑定框标签文本
                    if (i_bl < Bind_label.Count)
                    {
                        current.transform.GetChild(0).GetComponent<Text>().text = Bind_label[i_bl++];
                    }
                    else
                    {
                        current.transform.GetChild(0).GetComponent<Text>().text = "请输入文本";
                    }
                }
                else
                {
                    Debug.LogError($"[gameOptionController] Bind 预制体层级不满足文本设置要求（界面 \"{parents}\"，第 {i} 项），已跳过文本设置。", current);
                }
            }
            else if (mainbodyInterfaceType[i] == "te") //设置文本
            {
                if (i_te < Text_text.Count)
                {
                    current.transform.GetChild(0).GetComponent<Text>().text = Text_text[i_te++];
                }
                else
                {
                    current.transform.GetChild(0).GetComponent<Text>().text = "请输入文本";
                }
            }
            //为 UI控件 加上 UIControl
            GetOrAddUIControl(current);
        }
    }

}
