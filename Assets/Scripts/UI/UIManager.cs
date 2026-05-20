using UnityEngine;
using UnityEngine.UI;
using Zenject;

public class UIManager : MonoBehaviour, IUIManagerView
{
    [Header("UI References")]
    public GameObject nextTurnButtonGameObject;
    public Text TechPoints;
    public Text CulturePoints;

    [Header("Unit Info Panel")]
    public GameObject unitInfoPanel;
    public Text unitNameText;

    [Inject] private IUnitDataProvider _unitDataProvider;
    [Inject] private UIConfigSO _uiConfigSO;

    public void SetTechPoints(int points)
    {
        if (TechPoints != null) TechPoints.text = points.ToString();
    }

    public void SetCulturePoints(int points)
    {
        if (CulturePoints != null) CulturePoints.text = points.ToString();
    }

    public void SetPhase(string phaseName) { }

    public void ShowUnitInfoPanel(CharacterData data)
    {
        if (unitInfoPanel == null || data?.unitData == null) return;

        unitInfoPanel.SetActive(true);

        // 1. 名称
        Text unitNameText = unitInfoPanel.transform.GetChild(1).GetComponent<Text>();
        unitNameText.text = data.unitData.unitName;

        // 2. 卡面
        Transform unitCard = unitInfoPanel.transform.GetChild(0);
        unitCard.GetComponent<Image>().sprite = _unitDataProvider.GetCard(data.unitData.id);

        // 3. 技能图标
        Image skillIcon = unitInfoPanel.transform.GetChild(3).GetChild(0).GetComponent<Image>();
        skillIcon.sprite = _unitDataProvider.GetSkillIcon(data.unitData.id);

        // 4. 强制覆盖 4 条属性行
        Transform statsParent = unitInfoPanel.transform.GetChild(2); 
        if (statsParent != null)
        {
            //移动
            float movePoints = data.unitMovementController.currentMovementPoints;
            statsParent.GetChild(0).GetChild(1).GetComponent<Text>().text = $"{movePoints:F1}";                   //数值
            statsParent.GetChild(0).GetChild(2).GetComponent<Text>().text = "移动力";                             //标签
            statsParent.GetChild(0).GetChild(0).GetComponent<Image>().sprite = _uiConfigSO.movementPointsIcon;    //条目图标

            //攻击
            statsParent.GetChild(1).GetChild(1).GetComponent<Text>().text = $"{data.currentAttackValue}";
            statsParent.GetChild(1).GetChild(2).GetComponent<Text>().text = "攻击力";
            statsParent.GetChild(1).GetChild(0).GetComponent<Image>().sprite = _uiConfigSO.meleeAttackPointsIcon;

            //防御
            statsParent.GetChild(2).GetChild(1).GetComponent<Text>().text = $"{data.Defense:F1}";
            statsParent.GetChild(2).GetChild(2).GetComponent<Text>().text = "防御力";
            statsParent.GetChild(2).GetChild(0).GetComponent<Image>().sprite = _uiConfigSO.defenseIcon;
            //血量
            statsParent.GetChild(3).GetChild(1).GetComponent<Text>().text = $"{data.currentHp:F0}/{data.unitData.hp}";
            statsParent.GetChild(3).GetChild(2).GetComponent<Text>().text = "血量";
            statsParent.GetChild(3).GetChild(0).GetComponent<Image>().sprite = _uiConfigSO.healthIcon;
        }
    }

    public void HideUnitInfoPanel()
    {
        if (unitInfoPanel != null)
            unitInfoPanel.SetActive(false);
    }

    public void RefreshUnitInfoPanel(CharacterData data)
    {
        if (unitInfoPanel == null || !unitInfoPanel.activeSelf || data == null)
            return;

        Transform statsParent = unitInfoPanel.transform.GetChild(2);
        // 移动力
        statsParent.GetChild(0).GetChild(1).GetComponent<Text>().text = $"{data.unitMovementController.currentMovementPoints:F1}";
        // 攻击力
        statsParent.GetChild(1).GetChild(1).GetComponent<Text>().text = $"{data.currentAttackValue}";
        // 防御力
        statsParent.GetChild(2).GetChild(1).GetComponent<Text>().text = $"{data.Defense:F1}";
        // 血量
        statsParent.GetChild(3).GetChild(1).GetComponent<Text>().text = $"{data.currentHp:F0}/{data.unitData.hp}";
    }
}