using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Model;

namespace Valkaris.Core.Hosting;

/// <summary>
/// The only thing Presentation talks to. A networked session will implement this too, so:
/// read State fresh each frame (a snapshot may replace it), and expect rejections as events,
/// because a command sent over the network cannot answer immediately.
/// </summary>
public interface IGameSession
{
    GameState State { get; }
    GameData Data { get; }
    int LocalPlayerId { get; }

    /// <summary>True only for single player. Multiplayer sessions return false and throw from SetPaused.</summary>
    bool CanPause { get; }
    bool IsPaused { get; }
    void SetPaused(bool paused);

    void Send(ICommand command);
    void Tick(double deltaSeconds);

    event Action<GameEvent>? EventRaised;
    event Action<CommandRejection>? CommandRejected;
}
