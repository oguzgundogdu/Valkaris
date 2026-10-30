using Valkaris.Core.Events;
using Valkaris.Core.Hosting;

namespace Valkaris.Ai;

/// <summary>Plays one player by rules. Everything it knows and does goes through its AiView.</summary>
public sealed class AiPlayer : IMatchAgent
{
    private readonly AiView _view;
    private readonly double _interval;

    public AiPlayer(AiView view)
    {
        _view = view;
        _interval = view.Profile.DecisionSeconds / view.GameSpeed;

        // Staggered by player, so opponents don't all think at the same moment.
        NextDecisionTime = _interval * (1 + view.PlayerId % 7 / 7.0);
    }

    public double NextDecisionTime { get; private set; }

    public void Decide()
    {
        NextDecisionTime = _view.Now + _interval;
    }

    public void OnEvent(GameEvent gameEvent)
    {
        // Choosing a new capital can't wait for the next turn to come round.
        if (gameEvent is CapitalChoiceRequired required && required.PlayerId == _view.PlayerId)
            NextDecisionTime = Math.Min(NextDecisionTime, gameEvent.Time);
    }
}
