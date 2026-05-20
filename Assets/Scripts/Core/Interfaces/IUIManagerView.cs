public interface IUIManagerView
{
    void SetTechPoints(int points);
    void SetCulturePoints(int points);
    void SetPhase(string phaseName);                // 可选，用于显示当前回合阶段
    void ShowUnitInfoPanel(CharacterData data);
    void HideUnitInfoPanel();

    void RefreshUnitInfoPanel(CharacterData data);
}