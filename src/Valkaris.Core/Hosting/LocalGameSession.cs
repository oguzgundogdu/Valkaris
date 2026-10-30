using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Hosting;

/// <summary>Single player: the state, the rules and the simulation all run in this process.</summary>
public sealed class LocalGameSession : IGameSession
{
    private readonly CommandHandler _handler;
    private readonly Simulation _simulation;

    public LocalGameSession(GameState state, GameData data, int localPlayerId)
    {
        State = state;
        Data = data;
        LocalPlayerId = localPlayerId;
        _handler = new CommandHandler(state, data);
        _simulation = new Simulation(state, data);
    }

    public GameState State { get; }
    public GameData Data { get; }
    public int LocalPlayerId { get; }
    public bool CanPause => true;
    public bool IsPaused { get; private set; }

    public event Action<GameEvent>? EventRaised;
    public event Action<CommandRejection>? CommandRejected;

    public void SetPaused(bool paused) => IsPaused = paused;

    public void Send(ICommand command)
    {
        if (IsPaused)
        {
            CommandRejected?.Invoke(CommandRejection.GamePaused);
            return;
        }

        var result = _handler.Handle(LocalPlayerId, command);
        if (result.Rejection is { } rejection)
            CommandRejected?.Invoke(rejection);
    }

    /// <summary>
    /// Issues a command for another player in this process, such as an AI. It goes through the same rules.
    /// Deliberately not on IGameSession, so the UI can never act as someone else.
    /// </summary>
    public CommandResult SendAs(int playerId, ICommand command) =>
        IsPaused ? CommandResult.Rejected(CommandRejection.GamePaused) : _handler.Handle(playerId, command);

    public void Tick(double deltaSeconds)
    {
        if (IsPaused)
            return;

        foreach (var gameEvent in _simulation.Advance(deltaSeconds))
            EventRaised?.Invoke(gameEvent);
    }
}
