using Valkaris.Core.Events;

namespace Valkaris.Core.Hosting;

/// <summary>Plays for one player, such as an AI. It acts through the same commands as anyone else.</summary>
public interface IMatchAgent
{
    /// <summary>The match time of the next decision. Must move past the current time after each Decide.</summary>
    double NextDecisionTime { get; }

    void Decide();

    /// <summary>Lets an agent wake early by moving NextDecisionTime, for example when its capital falls.</summary>
    void OnEvent(GameEvent gameEvent);
}
