using System;

public interface ITechCultureService
{
    float TechPoints { get; }
    float CulturePoints { get; }
    int TechLevel { get; }
    int CultureLevel { get; }
    event Action OnTechPointsChanged;
    event Action OnCulturePointsChanged;

    /// <summary> 触发科技/文化进度条的播放动画 </summary>
    void TriggerProgressAnimation();

    void AddPointsPerTurn();
}