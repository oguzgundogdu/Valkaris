using Valkaris.Core.Events;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

namespace Valkaris.Ai;

/// <summary>Plays one player by rules. Everything it knows and does goes through its AiView.</summary>
public sealed class AiPlayer : IMatchAgent
{
    private readonly AiView _view;
    private readonly double _interval;
    private readonly AiMemory _memory;
    private readonly BuildPlanner _builds;
    private readonly TrainingPlanner _training;
    private readonly DefencePlanner _defence;
    private readonly RaidPlanner _raids;
    private readonly WarPlanner _war;
    private readonly ExpansionPlanner _expansion;
    private readonly TransferPlanner _transfers;

    public AiPlayer(AiView view)
    {
        _view = view;
        _memory = new AiMemory(view);
        _builds = new BuildPlanner(view);
        _training = new TrainingPlanner(view, _memory);
        _defence = new DefencePlanner(view);
        _raids = new RaidPlanner(view, _memory);
        _war = new WarPlanner(view, _memory);
        _expansion = new ExpansionPlanner(view);
        _transfers = new TransferPlanner(view, _builds);
        _interval = view.Profile.DecisionSeconds / view.GameSpeed;

        // A random first decision from the match seed: a fixed order would always put one seat slightly ahead.
        NextDecisionTime = _interval * (1 + new AiRandom(view.Seed, view.PlayerId).NextDouble());
    }

    public double NextDecisionTime { get; private set; }

    public void Decide()
    {
        NextDecisionTime = _view.Now + _interval;
        if (_view.IsEliminated)
        {
            NextDecisionTime = double.PositiveInfinity;
            return;
        }

        _memory.Update();
        _expansion.ChooseCapital();
        _defence.Plan();
        _transfers.Plan();
        foreach (var village in _view.OwnVillages)
        {
            var threatened = _defence.IsThreatened(village);
            if (!threatened && _training.IsSavingForGoal(village))
            {
                _training.Plan(village, threatened, Resources.Zero);
            }
            else if (threatened || _training.IsBehind())
            {
                _training.Plan(village, threatened, Resources.Zero);
                _builds.Plan(village, threatened);
            }
            else
            {
                _training.Plan(village, threatened, _builds.Plan(village, threatened));
            }

            _expansion.Found(village);
            if (!threatened)
            {
                _war.Plan(village);
                _raids.Plan(village);
            }
        }
    }

    public void OnEvent(GameEvent gameEvent)
    {
        // Choosing a new capital can't wait for the next turn to come round.
        if (gameEvent is CapitalChoiceRequired required && required.PlayerId == _view.PlayerId)
            NextDecisionTime = Math.Min(NextDecisionTime, gameEvent.Time);
    }
}
