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

    /// <summary>Other players' movements in the local player's sight now.</summary>
    IReadOnlyList<SightedMovement> SightedMovements { get; }

    /// <summary>Battles between other players, oldest first.</summary>
    IReadOnlyList<PublicNotice> PublicNotices { get; }

    /// <summary>Battles the local player saw happen after a given time, oldest first.</summary>
    IReadOnlyList<Clash> Clashes(double after);

    void Send(ICommand command);
    void Tick(double deltaSeconds);

    event Action<GameEvent>? EventRaised;
    event Action<CommandRejection>? CommandRejected;
}
