using UnityEngine;
using Zenject;

public class UIManagerPresenter : IInitializable, System.IDisposable
{
    [Inject] private IUIManagerView _view;
    [Inject] private ITechCultureService _techCultureService;
    [Inject] private IGameStateMachine _gameStateMachine;   // �����ڼ����غϱ仯
    [Inject] private IUnitRepository _unitRepository;

    // ��ǰѡ�еĵ�λ������������ʣ�
    public GameObject CurrentSelectedUnit { get; private set; }
    // ��־λ�����ڿ�����Ϣ����Ƿ���Ҫˢ��
    public bool IsPanelInfoSwitched { get; private set; }

    public void Initialize()
    {
        // ��ʼ�� UI ��ֵ
        UpdateTechCultureUI();

        // ���ĿƼ��Ļ�ֵ�仯�¼�
        if (_techCultureService != null)
        {
            _techCultureService.OnTechPointsChanged += UpdateTechPoints;
            _techCultureService.OnCulturePointsChanged += UpdateCulturePoints;
        }

        _unitRepository.OnPlayerUnitRemoved += OnUnitRemoved;
        _unitRepository.OnEnemyUnitRemoved += OnUnitRemoved;

        // ���Զ��ĻغϽ׶α仯�������Ҫ��ʾ��ǰ�׶Σ�
        // ���� _gameStateMachine.OnPhaseChanged += OnPhaseChanged;
    }

    private void OnUnitRemoved(GameObject unit)
    {
        if (CurrentSelectedUnit == unit)
        {
            DeselectUnit();
        }
    }

    // ���ⲿ���� PlayerInputHandler�����ã���ѡ�е�λʱ
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

    // ȡ��ѡ�е�λ
    public void DeselectUnit()
    {
        CurrentSelectedUnit = null;
        IsPanelInfoSwitched = true;
        _view.HideUnitInfoPanel();
    }

    // ���������Ƽ��Ļ� UI
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

    // ��ѡ���غϽ���ʱ�� UI ����
    public void OnTurnEnded()
    {
        // ���粥�Ŷ�����
    }

    public void Dispose()
    {
        if (_techCultureService != null)
        {
            _techCultureService.OnTechPointsChanged -= UpdateTechPoints;
            _techCultureService.OnCulturePointsChanged -= UpdateCulturePoints;
        }

        if (_unitRepository != null)
        {
            _unitRepository.OnPlayerUnitRemoved -= OnUnitRemoved;
            _unitRepository.OnEnemyUnitRemoved -= OnUnitRemoved;
        }
    }

    public void RefreshCurrentUnitInfo()
    {
        if (CurrentSelectedUnit != null)
        {
            var data = CurrentSelectedUnit.GetComponent<UnitMovementController>()?.characterData;
            if (data != null)
                _view.RefreshUnitInfoPanel(data);   // ��ˢ����ֵ����
        }
    }
}
