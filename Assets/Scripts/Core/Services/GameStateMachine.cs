using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Zenject;

public class GameStateMachine : IGameStateMachine, IInitializable
{
    private readonly List<IPhase> _phases;
    private int _currentPhaseIndex;
    private int _currentTurn;

    [Inject] private ICardService _cardService;
    [Inject] private CardPresenter _cardPresenter;
    [Inject] private IUnitRepository _unitRepository;
    [Inject] private LazyInject<PlayerInputHandler> _playerInputHandler;

    [Inject]
    public GameStateMachine(
        PlayerPhase playerPhase,
        AIPhase aiPhase,
        SettlementPhase settlementPhase)
    {
        _phases = new List<IPhase> { playerPhase, aiPhase, settlementPhase };
        _currentPhaseIndex = 0;
        _currentTurn = 1;
    }

    public int CurrentTurn => _currentTurn;
    public IPhase CurrentPhase => _phases[_currentPhaseIndex];

    public event System.Action PhaseChanged;

    public void Initialize()
    {
        StartGame();
    }

    public void StartGame()
    {
        _currentPhaseIndex = 0;
        _currentTurn = 1;
        EnterCurrentPhase();
    }

    private void EnterCurrentPhase()
    {
        _phases[_currentPhaseIndex].Enter();
        PhaseChanged?.Invoke();
    }

    public void EndTurn()
    {
        if (_currentPhaseIndex != 0) return;

        var phase = _phases[_currentPhaseIndex];
        if (!phase.CanExit()) return;

        _playerInputHandler.Value.ForceDeselectUnit();
        phase.Exit();
        MoveToNextPhase();
    }

    private void MoveToNextPhase()
    {
        _currentPhaseIndex++;
        if (_currentPhaseIndex >= _phases.Count)
        {
            _currentTurn++;
            _currentPhaseIndex = 0;
            ResetForNewTurn();
        }
        EnterCurrentPhase();

        if (_phases[_currentPhaseIndex] is AIPhase aiPhase)
        {
            _ = ProcessAIPhase(aiPhase);
        }
    }

    private async Task ProcessAIPhase(AIPhase aiPhase)
    {
        await aiPhase.RunAITurn();
        int aiIndex = _phases.IndexOf(aiPhase);
        if (aiIndex >= 0 && _currentPhaseIndex == aiIndex)
        {
            _phases[_currentPhaseIndex].Exit();

            int settlementIndex = _phases.FindIndex(p => p is SettlementPhase);
            if (settlementIndex >= 0)
            {
                _currentPhaseIndex = settlementIndex;
                // SettlementPhase is synchronous, so this completes settlement before the next turn.
                _phases[_currentPhaseIndex].Enter();
                _phases[_currentPhaseIndex].Exit();
            }

            _currentTurn++;
            ResetForNewTurn();

            int playerIndex = _phases.FindIndex(p => p is PlayerPhase);
            if (playerIndex >= 0)
            {
                _currentPhaseIndex = playerIndex;
                EnterCurrentPhase();
            }
        }
    }

    private void ResetForNewTurn()
    {
        ResetUnitMovement();

        _cardService.ResetDealOpportunity();
        _cardPresenter.TryDealFromNextIfPossible();

        _cardService.ResetDrawOpportunity();
        _cardPresenter.OnTurnEnded();
    }

    private void ResetUnitMovement()
    {
        foreach (CharacterData c in _unitRepository.AllPlayerUnits.Values)
        {
            if (c == null) continue;
            var umc = c.unitMovementController ?? c.model?.GetComponent<UnitMovementController>();
            if (umc == null) continue;

            umc.RestoreUnitMovementStandbyParameters();
            umc.isMoved = true;
        }
    }


}
