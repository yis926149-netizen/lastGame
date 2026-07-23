public interface IUIManagerView
{
    void SetPhase(string phaseName);
    void ShowUnitInfoPanel(CharacterData data);
    void HideUnitInfoPanel();
    void RefreshUnitInfoPanel(CharacterData data);
}