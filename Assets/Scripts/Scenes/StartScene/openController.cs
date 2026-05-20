using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using Unity.VisualScripting;
using UnityEngine.Events;
using static UnityEngine.UI.Button;
using UnityEngine.Serialization;

//****************************************
//创建人：易生
//功能说明：
//****************************************

public class openController : StartSceneUIController
{
    //首级按钮的数量
    public int firstButNum;
    //首级按钮的预制体
    public GameObject firstBut;
    //首级被点击按钮的预制体
    public GameObject firstButBeClicked;
    //首级按钮组
    [HideInInspector]
    public List<GameObject> firstButG = new List<GameObject>();
    //首级被点击按钮组
    [HideInInspector]
    public List<GameObject> firstButBeClickedG = new List<GameObject>();
    //首级按钮的文本
    public List<string> firstButText = new List<string>();
    //首级左侧栏
    public GameObject lSideBar;
    //次级右侧栏
    public GameObject RSideBar;

    //次级按钮数量
    public List<int> secButNum = new List<int>();
    //次级按钮组
    [HideInInspector]
    public List<GameObject> secButG = new List<GameObject>();
    //次级按钮的文本
    public List<string> SecondButText = new List<string>();

    //所有按钮的名字
    //public List<string> allBut = 
    //[FormerlySerializedAs("onClick")]
    //[SerializeField]
    //private ButtonClickedEvent AddEventForOneButton = new ButtonClickedEvent();

    //protected Button()
    //{ }
    //上次被点击的按钮id
    [HideInInspector]
    public int preBeClicked = -1;

    public GameObject Covering;
    [HideInInspector]
    public GameObject gameOption;
    [HideInInspector]
    public GameObject open;
    [HideInInspector]
    public GameObject open_BackRround;
    //开始游戏设定界面的预制体
    public GameObject gameOptionPerfab;
    void Awake()
    {
        //先让其他界面失活
        open = GameObject.Find("open");//开始界面
        gameOption = GameObject.Find("gameOption");//游戏选择界面
        open_BackRround = GameObject.Find("open_BackRround");//开始界面的背景大字
        gameOption.SetActive(false);
        //Debug.Log(secButG.Count);
        //实例化右侧栏,并设置初设定
        RSideBar = Instantiate(lSideBar, transform);
        RSideBar.name = "RSideBar";
        //开局实例化全部选择按钮,并设置初设定
        for (int i = 0; i < firstButNum; i++)
        {
            //首级按钮实例化，并指定父物体
            firstButG.Add(Instantiate(firstBut, transform));
            //各首级按钮的位置
            firstButG[i].transform.localPosition += new Vector3(0, -50 * (i + 1), 0);
            //各首级按钮的文本
            if (i < firstButText.Count)
            {
                firstButG[i].transform.GetChild(0).transform.GetComponent<Text>().text = firstButText[i];
            }
            else
            {
                firstButG[i].transform.GetChild(0).transform.GetComponent<Text>().text = "未命名";
            }
            //各首级按钮的名字（方便UIController的字典）
            firstButG[i].gameObject.name = "firstButton" + i;
            //为各首级按钮加上 UIControl
            firstButG[i].AddComponent<UIControl>();
            //各首级按钮的id
            firstButG[i].transform.GetComponent<UIControl>().firstButId = i;
            //为各首级按钮添加各自的事件
            firstButG[i].transform.GetComponent<UIControl>().AddButtonClickEvent(firstButG[i].transform.GetComponent<UIControl>().BeClickedAni);
            firstButG[i].transform.GetComponent<UIControl>().AddButtonClickEvent(firstButG[i].transform.GetComponent<UIControl>().PlayButtonSoundEffect);

            //被点击的首级按钮（同上例）
            //首级被点击按钮实例化，并指定父物体
            firstButBeClickedG.Add(Instantiate(firstButBeClicked, transform));
            //各首级被点击按钮的位置
            firstButBeClickedG[i].transform.localPosition += new Vector3(-230 - 200, -50 * (i + 1), 0);
            //各首级被点击按钮的名字（方便UIController的字典）
            firstButBeClickedG[i].gameObject.name = "firstButtonBeClicked" + i;

            //对应的次级按钮实例化（次级按钮为首级被点击按钮的子物体）
            if (i < secButNum.Count)
            {
                for (int j = 0; j < secButNum[i]; j++)
                {
                    //实例化次级按钮
                    GameObject secbtu = Instantiate(firstButBeClicked, firstButBeClickedG[i].transform);
                    //设置次级按钮大小
                    secbtu.transform.localScale = new Vector3(0.75f, 0.75f, 0.75f);
                    //设置次级按钮位置
                    secbtu.transform.localPosition = new Vector3(transform.localPosition.x + 310 - 200, transform.GetComponentInParent<Transform>().localPosition.y - (j * 40), transform.localPosition.z);
                    //加入次级按钮组
                    secButG.Add(secbtu);
                    //各首级按钮的文本
                    if (secButG.Count <= SecondButText.Count)
                    {
                        secButG[secButG.Count-1].transform.GetChild(0).transform.GetComponent<Text>().text = SecondButText[secButG.Count-1];
                    }
                    else
                    {
                        secButG[secButG.Count-1].transform.GetChild(0).transform.GetComponent<Text>().text = "未命名";
                    }
                    //改名，方便加字典
                    if (i > 0)
                    {
                        secButG[j + secButNum[i - 1]].name = firstButBeClickedG[i].name + "_secBut" + j;
                    }
                    else
                    {
                        secButG[j].name = firstButBeClickedG[i].name + "_secBut" + j;
                    }
                    //为各次级按钮加上 UIControl
                    if (secButG[j].GetComponent<UIControl>() == null)
                    {
                        secButG[j].AddComponent<UIControl>();
                    }

                }
            }
            //次级按钮全部失活
            foreach(GameObject secbtu in secButG)
            {
                secbtu.SetActive(false);
            }

            //各首级被点击按钮的文本（同点击按钮）
            if (i < firstButText.Count)
            {
                firstButBeClickedG[i].transform.GetChild(0).transform.GetComponent<Text>().text = firstButText[i];
            }
            else
            {
                firstButBeClickedG[i].transform.GetChild(0).transform.GetComponent<Text>().text = "未命名";
            }
            //为各首级被点击按钮加上 UIControl
            firstButBeClickedG[i].AddComponent<UIControl>();
            //将右侧栏赋予各次级按钮
            firstButBeClickedG[i].GetComponent<UIControl>().RSideBar = RSideBar;
            //各首级被点击按钮的id（同点击按钮）
            firstButBeClickedG[i].transform.GetComponent<UIControl>().firstButId = i;
            //将该首级被点击按钮赋予对应的首级按钮
            firstButG[i].transform.GetComponent<UIControl>().thisFirButBeClicked = firstButBeClickedG[i];
            //未被点击时设置为失活状态
            firstButBeClickedG[i].SetActive(false);
        }

        //实例化左侧栏,并设置初设定
        lSideBar = Instantiate(lSideBar, transform);
        lSideBar.name = "lSideBar";
        //左侧栏长短随选择按钮的数量自动缩放
        lSideBar.transform.localScale += new Vector3(0, 0.07f * firstButNum, 0);
        if (firstButNum % 2 == 0)//偶数
        {
            lSideBar.transform.localPosition = new Vector3(-230, (firstButG[(firstButNum) / 2 - 1].transform.localPosition.y + firstButG[(firstButNum) / 2].transform.localPosition.y) / 2, 0);

        }
        else                     //奇数
        {
            lSideBar.transform.localPosition = new Vector3(-230, firstButG[(firstButNum + 1) / 2 - 1].transform.localPosition.y, 0);
        }
        //最后才将右侧栏失活
        RSideBar.SetActive(false);

        //实例化遮挡板并失活
        Covering = Instantiate(Covering, transform);
        Covering.SetActive(false);
        //开启动画
        OpenAni();

        //额外按钮事件
        //游戏选项按钮_切换界面事件_
        firstButG[2].transform.GetComponent<UIControl>().AddButtonClickEvent(firstButG[2].transform.GetComponent<UIControl>().ToGameOptionsInterface);

        //进入游戏
        secButG[4].transform.GetComponent<UIControl>().AddButtonClickEvent(secButG[4].transform.GetComponent<UIControl>().ToGameScene);

        Debug.Log("次级按钮的数量是：" + secButG.Count);
        //所有次级按钮事件
        foreach(GameObject g in secButG)
        {
            if (g.GetComponent<UIControl>() == null)
            {
                g.AddComponent<UIControl>();
            }

            g.transform.GetComponent<UIControl>().AddButtonClickEvent(g.transform.GetComponent<UIControl>().PlayButtonSoundEffect);
        }

    }



    //开场动画
    private void OpenAni()
    {
        //左侧栏到动画开始的位置
        lSideBar.transform.localPosition += new Vector3(0, 650, 0);
        //首级按钮到动画开始的位置,并失活
        foreach (GameObject fbg in firstButG)
        {
            fbg.transform.localPosition += new Vector3(-220, 0, 0);
            fbg.SetActive(false);
        }
        //首级按钮的左边缘动画
        lSideBar.transform.DOMove(lSideBar.transform.position - new Vector3(0, 650, 0), 0.5f).OnComplete(OpenOptionAni);

    }

    //首级按钮的开场动画
    private int fbgIndex = 0;
    private void OpenOptionAni()
    {
        if (fbgIndex >= firstButG.Count)
        {
            return;
        }
        firstButG[fbgIndex].SetActive(true);
        firstButG[fbgIndex].transform.DOMove(firstButG[fbgIndex].transform.position - new Vector3(-220, 0, 0), 0.2f).OnComplete(OpenOptionAni);//迭代
        fbgIndex++;
    }

}
