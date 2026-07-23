using UnityEngine;
using Zenject;

public class UIManagerPresenter : IInitializable, System.IDisposable
{
    [Inject] private IUIManagerView _view;
    [Inject] private IGameStateMachine _gameStateMachine;
    [Inject] private IUnitRepository _unitRepository;

    public GameObject CurrentSelectedUnit { get; private set; }
    public bool IsPanelInfoSwitched { get; private set; }

    public void Initialize()
    {
        _unitRepository.OnPlayerUnitRemoved += OnUnitRemoved;
        _unitRepository.OnEnemyUnitRemoved += OnUnitRemoved;
    }

    private void OnUnitRemoved(GameObject unit)
    {
        if (CurrentSelectedUnit == unit)
        {
            DeselectUnit();
        }
    }

    public void SelectUnit(GameObject unit)
    {
        if (CurrentSelectedUnit == unit) return;

        CurrentSelectedUnit = unit;
        IsPanelInfoSwitched = false;

        var characterData = unit.GetComponent<UnitMovementController>()?.characterData;
        if (characterData != null)
        {
            _view.ShowUnitInfoPanel(characterData);
        }
    }

    public void DeselectUnit()
    {
        CurrentSelectedUnit = null;
        IsPanelInfoSwitched = true;
        _view.HideUnitInfoPanel();
    }

    public void OnTurnEnded()
    {
    }

    public void Dispose()
    {
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
                _view.RefreshUnitInfoPanel(data);
        }
    }
}
