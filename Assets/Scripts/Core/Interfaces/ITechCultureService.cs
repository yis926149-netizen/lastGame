using System;

public interface ITechCultureService
{
    float TechPoints { get; }
    float CulturePoints { get; }
    int TechLevel { get; }
    int CultureLevel { get; }
    event Action OnTechPointsChanged;
    event Action OnCulturePointsChanged;

    /// <summary> �����Ƽ�/�Ļ��������Ĳ��Ŷ��� </summary>
    void TriggerProgressAnimation();

    void AddPointsPerTurn();
    void AddTechPointsPerTurn(float points);
    void AddCulturePointsPerTurn(float points);
}
