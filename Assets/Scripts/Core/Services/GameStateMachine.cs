using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Zenject;

public class GameStateMachine : IGameStateMachine, IInitializable
{
    private readonly List<IPhase> _phases;
    private int _currentPhaseIndex;
    private int _currentTurn;

    [Inject] private ICardService _cardService;      // 改为接口注入，更灵活
    [Inject] private CardPresenter _cardPresenter;
    [Inject] private IUnitRepository _unitRepository;
    [Inject] private ITechCultureService _techCultureService;

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
    }

    public void EndTurn()
    {
        if (_currentPhaseIndex != 0) return;

        var phase = _phases[_currentPhaseIndex];
        if (!phase.CanExit()) return;

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
        if (_currentPhaseIndex == 1)
        {
            _phases[_currentPhaseIndex].Exit();

            int settlementIndex = _phases.FindIndex(p => p is SettlementPhase);
            if (settlementIndex >= 0)
            {
                _currentPhaseIndex = settlementIndex;
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

        _cardService.ResetDealOpportunity();           // 重置“每回合一次”标志
        _cardPresenter.TryDealFromNextIfPossible();    // 回合开始时自动填补上一回合留下的空槽

        // 抽卡机会重置 + 尝试发一张新卡
        _cardService.ResetDrawOpportunity();
        _cardPresenter.OnTurnEnded();

        // 触发科技/文化进度条动画
        _techCultureService.TriggerProgressAnimation();
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