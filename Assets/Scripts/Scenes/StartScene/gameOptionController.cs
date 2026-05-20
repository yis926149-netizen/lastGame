using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
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
        /*
        //整个gameOption所有控件加起来的数量
        interfacesUiContrlSum.Add(mainbodyInterface0Type.Count);
        interfacesUiContrlSum.Add(mainbodyInterface1Type.Count);
        interfacesUiContrlSum.Add(mainbodyInterface2Type.Count);
        interfacesUiContrlSum.Add(mainbodyInterface3Type.Count);
        interfacesUiContrlSum.Add(mainbodyInterface4Type.Count);
        interfacesUiContrlSum.Add(mainbodyInterface5Type.Count);
        interfacesUiContrlSum.Add(mainbodyInterface6Type.Count);
        */
        /*
        List<string> mainbodyInterfaceType,
        List<GameObject> m_interface,
        List<string> DropDown_textTitle,
        List<int> eachDropdowmOptionNum,
        List<string> DropDown_textLabe,
        List<string> InputField_text,
        List<string> Slider_text,
        List<string> Toggle_text,
        int m_interfacesUiContrlSum,
        string parents,

        int i_dt = 0,
        int eDON = 0,       
        int i_dl = 0,
        int i_i = 0,
        int i_s = 0,
        int i_t = 0
        */
        //还要在外面再套一层循环
        /*
        //for (int k= 0;k< interfacesUiContrlSum.Count; k++)
        //{
        //(一次循环即完成一个interface的UI控件的实例化)
        //实例化预设的UI控件

        for (int i = 0; i < mainbodyInterface0Type.Count; i++)
        {

            //实例化UI控件，并将其父物体设置为interface0  
            if (mainbodyInterface0Type[i] == "d")
            {
                interface0.Add(Instantiate(DropDown, GameObject.Find("interface0").transform));
                if (eDON0 < eachDropdowmOptionNum0.Count)  //设置单个下拉框的选项个数
                {
                    for (int j = 0; j < eachDropdowmOptionNum0[eDON0]; j++)  //设置单个下拉框的每个选项
                    {
                        TMP_Dropdown.OptionData option = new TMP_Dropdown.OptionData();
                        interface0[i].transform.GetComponent<TMP_Dropdown>().options.Add(option);
                    }
                }
                else  //下拉框选项的数量设置不足
                {
                    Debug.Log("下拉框选项数量设置不足");
                }
            }
            else if (mainbodyInterface0Type[i] == "i")
            {
                interface0.Add(Instantiate(InputField, GameObject.Find("interface0").transform));
            }
            else if (mainbodyInterface0Type[i] == "s")
            {
                interface0.Add(Instantiate(Slider, GameObject.Find("interface0").transform));
            }
            else if (mainbodyInterface0Type[i] == "t")
            {
                interface0.Add(Instantiate(Toggle, GameObject.Find("interface0").transform));
                interface0[i].GetComponent<Image>().color = new Vector4(0.2588235f, 0.4156863f, 0.7294118f, 0.2313726f);  //初始化Toggle按钮
                interface0[i].GetComponent<UIControl>().AddButtonClickEvent(interface0[i].transform.GetComponent<UIControl>().Toggle_test);  //给Toggle按钮添加事件
            }
            else
            {
                Debug.Log("UI控件名输入错误");
                interface0.Add(nothing);
            }
            //设置interface0的UI控件的名字（方便UIController的字典）
            interface0[i].gameObject.name = "interface0_UI" + i;
            //设置interface0的UI控件位置
            interface0[i].transform.position += new Vector3(0, i * (-35), 0);
            //设置UI控件文本
            if (mainbodyInterface0Type[i] == "d")  //设置下拉框文本
            {
                if (i_dt0 < DropDown_textTitle0.Count)
                {
                    interface0[i].transform.GetChild(0).GetComponent<Text>().text = DropDown_textTitle0[i_dt0++];//设置下拉框的标题文本
                    if (eDON0 < eachDropdowmOptionNum0.Count)  //对应的下拉框设有选项个数
                    {
                        for (int j = 0; j < eachDropdowmOptionNum0[eDON0]; j++)  //单个下拉框的每个选项
                        {
                            if (i_dl0 < DropDown_textLabe0.Count)  //设置单个下拉框的单个选项文本
                            {
                                interface0[i].transform.GetComponent<TMP_Dropdown>().options[j].text = DropDown_textLabe0[i_dl0++];
                            }
                            else  //下拉框的选项文本量设置不足
                            {
                                interface0[i].transform.GetComponent<TMP_Dropdown>().options[j].text = "Please enter text";
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
                    interface0[i].transform.GetChild(0).GetComponent<Text>().text = "请输入文本";
                    //以下同上理
                    if (eDON0 < eachDropdowmOptionNum0.Count)  //对应的下拉框设有选项个数
                    {
                        for (int j = 0; j < eachDropdowmOptionNum0[eDON0]; j++)  //单个下拉框的每个选项
                        {
                            if (i_dl0 < DropDown_textLabe0.Count)  //设置单个下拉框的单个选项文本
                            {
                                interface0[i].transform.GetComponent<TMP_Dropdown>().options[j].text = DropDown_textLabe0[i_dl0++];
                            }
                            else  //下拉框的选项文本量设置不足
                            {
                                interface0[i].transform.GetComponent<TMP_Dropdown>().options[j].text = "Please enter text";
                            }
                        }
                    }
                    else  //下拉框选项的数量设置不足
                    {
                        Debug.Log("下拉框选项数量设置不足");
                    }
                }
                eDON0++;  //一次循环中有关下拉框的代码全部执行完毕后，索引++
            }
            else if (mainbodyInterface0Type[i] == "i")  //设置输入框
            {
                if (i_i0 < InputField_text0.Count)
                {
                    interface0[i].transform.GetChild(1).GetComponent<Text>().text = InputField_text0[i_i0++];
                }
                else
                {
                    interface0[i].transform.GetChild(1).GetComponent<Text>().text = "请输入文本";
                }
            }
            else if (mainbodyInterface0Type[i] == "s")  //设置滑条
            {
                if (i_s0 < Slider_text0.Count)
                {
                    interface0[i].transform.GetChild(2).GetComponent<Text>().text = Slider_text0[i_s0++];
                }
                else
                {
                    interface0[i].transform.GetChild(2).GetComponent<Text>().text = "请输入文本";
                }
            }
            else if (mainbodyInterface0Type[i] == "t")  //设置选择框
            {
                if (i_t0 < Toggle_text0.Count)
                {
                    interface0[i].transform.GetChild(1).GetComponent<Text>().text = Toggle_text0[i_t0++];
                }
                else
                {
                    interface0[i].transform.GetChild(1).GetComponent<Text>().text = "请输入文本";
                }
            }
            else
            {
                Debug.Log("该UI控件名为空");
            }
            //为 interface0的UI控件 加上 UIControl
            if (interface0[i].GetComponent<UIControl>() == null)
            {
                interface0[i].AddComponent<UIControl>();
            }
            //左侧栏按钮的id
            //LsideBarButG[i].transform.GetComponent<UIControl>().LsideBarButId = i;
            //为左侧栏按钮添加事件
            //LsideBarButG[i].transform.GetComponent<UIControl>().AddButtonClickEvent(LsideBarButG[i].transform.GetComponent<UIControl>().changeSecTopBarText);

    }
        
        //}
        //设置
        */
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
        GameObject.Find("interface1").SetActive(false);
        GameObject.Find("interface2").SetActive(false);
        GameObject.Find("interface3").SetActive(false);
        GameObject.Find("interface4").SetActive(false);
        GameObject.Find("interface5").SetActive(false);
        GameObject.Find("interface6").SetActive(false);


    }


    void Update()
    {

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
        //实例化UI控件，并将其父物体设置为m_interface
        for (int i = 0; i < mainbodyInterfaceType.Count; i++)
        {
            if (mainbodyInterfaceType[i] == "d")
            {
                m_interface.Add(Instantiate(DropDown, GameObject.Find(parents).transform));
                if (eDON < eachDropdowmOptionNum.Count)  //设置单个下拉框的选项个数
                {
                    for (int j = 0; j < eachDropdowmOptionNum[eDON]; j++)  //设置单个下拉框的每个选项
                    {
                        TMP_Dropdown.OptionData option = new TMP_Dropdown.OptionData();
                        m_interface[i].transform.GetComponent<TMP_Dropdown>().options.Add(option);
                    }
                }
                else  //下拉框选项的数量设置不足
                {
                    Debug.Log("下拉框选项数量设置不足");
                }
            }
            else if (mainbodyInterfaceType[i] == "i")
            {
                m_interface.Add(Instantiate(InputField, GameObject.Find(parents).transform));
            }
            else if (mainbodyInterfaceType[i] == "s")
            {
                m_interface.Add(Instantiate(Slider, GameObject.Find(parents).transform));
            }
            else if (mainbodyInterfaceType[i] == "to")
            {
                m_interface.Add(Instantiate(Toggle, GameObject.Find(parents).transform));
                m_interface[i].GetComponent<Image>().color = new Vector4(0.2588235f, 0.4156863f, 0.7294118f, 0.2313726f);  //初始化Toggle按钮
                m_interface[i].GetComponent<UIControl>().AddButtonClickEvent(m_interface[i].transform.GetComponent<UIControl>().Toggle_test);  //给Toggle按钮添加事件
            }
            else if(mainbodyInterfaceType[i] == "b")
            {
                GameObject UI = Instantiate(Bind, GameObject.Find(parents).transform);
                UI.transform.GetChild(1).GetComponent<UIControl>().AddButtonClickEvent(UI.transform.GetChild(1).transform.GetComponent<UIControl>().Pop_up);
                UI.transform.GetChild(2).GetComponent<UIControl>().AddButtonClickEvent(UI.transform.GetChild(2).transform.GetComponent<UIControl>().Pop_up);
                UI.transform.GetChild(2).GetComponent<UIControl>().AddButtonClickEvent(UI.transform.GetChild(2).transform.GetComponent<UIControl>().PlayButtonSoundEffect);
                m_interface.Add(UI);
            }
            else if (mainbodyInterfaceType[i] == "te")
            {
                m_interface.Add(Instantiate(Text, GameObject.Find(parents).transform));
            }
            else
            {
                Debug.Log("UI控件名输入错误");
                m_interface.Add(nothing);
            }
            //设置interface0的UI控件的名字（方便UIController的字典）
            m_interface[i].gameObject.name = parents + "_UI" + i;
            //设置interface0的UI控件位置
            m_interface[i].transform.position += new Vector3(0, i * (-35), 0);
            //设置UI控件文本
            if (mainbodyInterfaceType[i] == "d")  //设置下拉框文本
            {
                if (i_dt < DropDown_textTitle.Count)
                {
                    m_interface[i].transform.GetChild(0).GetComponent<Text>().text = DropDown_textTitle[i_dt++];//设置下拉框的标题文本
                    if (eDON < eachDropdowmOptionNum.Count)  //对应的下拉框设有选项个数
                    {
                        for (int j = 0; j < eachDropdowmOptionNum[eDON]; j++)  //单个下拉框的每个选项
                        {
                            if (i_dl < DropDown_textLabe.Count)  //设置单个下拉框的单个选项文本
                            {
                                m_interface[i].transform.GetComponent<TMP_Dropdown>().options[j].text = DropDown_textLabe[i_dl++];
                            }
                            else  //下拉框的选项文本量设置不足
                            {
                                m_interface[i].transform.GetComponent<TMP_Dropdown>().options[j].text = "Please enter text";
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
                    m_interface[i].transform.GetChild(0).GetComponent<Text>().text = "请输入文本";
                    //以下同上理
                    if (eDON < eachDropdowmOptionNum.Count)  //对应的下拉框设有选项个数
                    {
                        for (int j = 0; j < eachDropdowmOptionNum[eDON]; j++)  //单个下拉框的每个选项
                        {
                            if (i_dl < DropDown_textLabe.Count)  //设置单个下拉框的单个选项文本
                            {
                                m_interface[i].transform.GetComponent<TMP_Dropdown>().options[j].text = DropDown_textLabe[i_dl++];
                            }
                            else  //下拉框的选项文本量设置不足
                            {
                                m_interface[i].transform.GetComponent<TMP_Dropdown>().options[j].text = "Please enter text";
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
                    m_interface[i].transform.GetChild(1).GetComponent<Text>().text = InputField_text[i_i++];
                }
                else
                {
                    m_interface[i].transform.GetChild(1).GetComponent<Text>().text = "请输入文本";
                }
            }
            else if (mainbodyInterfaceType[i] == "s")  //设置滑条
            {
                if (i_s < Slider_text.Count)
                {
                    m_interface[i].transform.GetChild(2).GetComponent<Text>().text = Slider_text[i_s++];
                }
                else
                {
                    m_interface[i].transform.GetChild(2).GetComponent<Text>().text = "请输入文本";
                }
            }
            else if (mainbodyInterfaceType[i] == "to")  //设置选择框
            {
                if (i_to < Toggle_text.Count)
                {
                    m_interface[i].transform.GetChild(1).GetComponent<Text>().text = Toggle_text[i_to++];
                }
                else
                {
                    m_interface[i].transform.GetChild(1).GetComponent<Text>().text = "请输入文本";
                }
            }
            else if (mainbodyInterfaceType[i] == "b")  //设置绑定框
            {
                //设置绑定框内容文本
                if (i_b < Bind_pritext.Count)
                {
                    m_interface[i].transform.GetChild(1).GetChild(1).GetComponent<Text>().text = Bind_pritext[i_b++];
                }
                else
                {
                    m_interface[i].transform.GetChild(1).GetChild(1).GetComponent<Text>().text = "请输入文本";
                }
                //设置绑定框标签文本
                if (i_bl < Bind_label.Count)
                {
                    m_interface[i].transform.GetChild(0).GetComponent<Text>().text = Bind_label[i_bl++];
                }
                else
                {
                    m_interface[i].transform.GetChild(0).GetComponent<Text>().text = "请输入文本";
                }
            }
            else if (mainbodyInterfaceType[i] == "te") //设置绑定框
            {
                if (i_b < Text_text.Count)
                {
                    m_interface[i].transform.GetChild(0).GetComponent<Text>().text = Text_text[i_te++];
                }
                else
                {
                    m_interface[i].transform.GetChild(0).GetComponent<Text>().text = "请输入文本";
                }
            }
            else
            {
                Debug.Log("该UI控件名为空");
            }
            //为 interface0的UI控件 加上 UIControl
            if (m_interface[i].GetComponent<UIControl>() == null)
            {
                m_interface[i].AddComponent<UIControl>();
            }
            //左侧栏按钮的id
            //LsideBarButG[i].transform.GetComponent<UIControl>().LsideBarButId = i;
            //为左侧栏按钮添加事件
            //LsideBarButG[i].transform.GetComponent<UIControl>().AddButtonClickEvent(LsideBarButG[i].transform.GetComponent<UIControl>().changeSecTopBarText);
        }
    }

}
