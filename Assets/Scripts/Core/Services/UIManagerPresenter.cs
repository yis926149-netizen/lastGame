using UnityEngine;
using Zenject;

public class UIManagerPresenter : IInitializable
{
    [Inject] private IUIManagerView _view;
    [Inject] private ITechCultureService _techCultureService;
    [Inject] private IGameStateMachine _gameStateMachine;   // 可用于监听回合变化

    // 当前选中的单位（供其他类访问）
    public GameObject CurrentSelectedUnit { get; private set; }
    // 标志位，用于控制信息面板是否需要刷新
    public bool IsPanelInfoSwitched { get; private set; }

    public void Initialize()
    {
        // 初始化 UI 数值
        UpdateTechCultureUI();

        // 订阅科技文化值变化事件
        if (_techCultureService != null)
        {
            _techCultureService.OnTechPointsChanged += UpdateTechPoints;
            _techCultureService.OnCulturePointsChanged += UpdateCulturePoints;
        }

        // 可以订阅回合阶段变化（如果需要显示当前阶段）
        // 例如 _gameStateMachine.OnPhaseChanged += OnPhaseChanged;
    }

    // 由外部（如 PlayerInputHandler）调用，当选中单位时
    public void SelectUnit(GameObject unit)
    {
        if (CurrentSelectedUnit == unit) return;

        CurrentSelectedUnit = unit;
        IsPanelInfoSwitched = false;  
        //IsPanelInfoSwitched = true;

        var characterData = unit.GetComponent<UnitMovementController>()?.characterData;
        if (characterData != null)
        {
            _view.ShowUnitInfoPanel(characterData);
        }
    }

    // 取消选中单位
    public void DeselectUnit()
    {
        CurrentSelectedUnit = null;
        IsPanelInfoSwitched = true;
        _view.HideUnitInfoPanel();
    }

    // 更新整个科技文化 UI
    private void UpdateTechCultureUI()
    {
        if (_techCultureService != null)
        {
            _view.SetTechPoints((int)_techCultureService.TechPoints);
            _view.SetCulturePoints((int)_techCultureService.CulturePoints);
        }
    }

    private void UpdateTechPoints()
    {
        _view.SetTechPoints((int)_techCultureService.TechPoints);
    }

    private void UpdateCulturePoints()
    {
        _view.SetCulturePoints((int)_techCultureService.CulturePoints);
    }

    // 可选：回合结束时的 UI 反馈
    public void OnTurnEnded()
    {
        // 例如播放动画等
    }

    // 若需要释放事件订阅，可实现 IDisposable（此处暂不实现）

    public void RefreshCurrentUnitInfo()
    {
        if (CurrentSelectedUnit != null)
        {
            var data = CurrentSelectedUnit.GetComponent<UnitMovementController>()?.characterData;
            if (data != null)
                _view.RefreshUnitInfoPanel(data);   // 仅刷新数值部分
        }
    }
}