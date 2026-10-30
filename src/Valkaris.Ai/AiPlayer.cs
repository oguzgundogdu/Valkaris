using Valkaris.Core.Events;
using Valkaris.Core.Hosting;

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

    public AiPlayer(AiView view)
    {
        _view = view;
        _memory = new AiMemory(view);
        _builds = new BuildPlanner(view);
        _training = new TrainingPlanner(view);
        _defence = new DefencePlanner(view);
        _interval = view.Profile.DecisionSeconds / view.GameSpeed;

        // Staggered by player, so opponents don't all think at the same moment.
        NextDecisionTime = _interval * (1 + view.PlayerId % 7 / 7.0);
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
        _defence.Plan();
        foreach (var village in _view.OwnVillages)
        {
            var threatened = _defence.IsThreatened(village);
            var saving = _builds.Plan(village, threatened);
            _training.Plan(village, threatened, saving);
        }
    }

    public void OnEvent(GameEvent gameEvent)
    {
        // Choosing a new capital can't wait for the next turn to come round.
        if (gameEvent is CapitalChoiceRequired required && required.PlayerId == _view.PlayerId)
            NextDecisionTime = Math.Min(NextDecisionTime, gameEvent.Time);
    }
}
