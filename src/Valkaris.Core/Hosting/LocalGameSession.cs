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
    private readonly List<IMatchAgent> _agents = new();

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

#if VALKARIS_DEV
    public bool IsGodMode => State.GodPlayerId == LocalPlayerId;

    /// <summary>
    /// Dev only, and deliberately not on IGameSession, so a multiplayer session can never offer it. Turning it on finishes
    /// the player's work under way, so an old queue can't undo an instant upgrade. Returns false while paused or after the match.
    /// </summary>
    public bool SetGodMode(bool active)
    {
        if (IsPaused || State.IsMatchOver)
            return false;

        State.GodPlayerId = active ? LocalPlayerId : null;
        if (active)
        {
            State.RushWork(LocalPlayerId);
            Tick(0);
        }
        return true;
    }
#endif

    public void Send(ICommand command)
    {
        if (IsPaused)
        {
            CommandRejected?.Invoke(CommandRejection.GamePaused);
            return;
        }

        var result = Handle(LocalPlayerId, command);
        if (result.Rejection is { } rejection)
            CommandRejected?.Invoke(rejection);
    }

    /// <summary>
    /// Issues a command for another player in this process, such as an AI. It goes through the same rules.
    /// Deliberately not on IGameSession, so the UI can never act as someone else.
    /// </summary>
    public CommandResult SendAs(int playerId, ICommand command) =>
        IsPaused ? CommandResult.Rejected(CommandRejection.GamePaused) : Handle(playerId, command);

    private CommandResult Handle(int playerId, ICommand command)
    {
        var result = _handler.Handle(playerId, command);
        foreach (var gameEvent in _handler.TakeEvents())
        {
            foreach (var agent in _agents)
                agent.OnEvent(gameEvent);
            EventRaised?.Invoke(gameEvent);
        }

        // Only the player's own command: an AI acting in the middle of a tick must not start another.
        if (result.Success && playerId == LocalPlayerId && GodRules.Applies(State, playerId))
            Tick(0);
        return result;
    }

    public void AddAgent(IMatchAgent agent) => _agents.Add(agent);

    /// <summary>
    /// Stops at every event and agent decision, so what agents do does not depend on the frame rate,
    /// even when an event wakes one early.
    /// </summary>
    public void Tick(double deltaSeconds)
    {
        if (IsPaused)
            return;

        var end = State.ElapsedTime + deltaSeconds;
        var events = new List<GameEvent>();

        while (!State.IsMatchOver)
        {
            var eventRan = _simulation.StepTowards(Math.Min(end, NextDecisionTime()), events);
            foreach (var gameEvent in events)
            {
                foreach (var agent in _agents)
                    agent.OnEvent(gameEvent);
                EventRaised?.Invoke(gameEvent);
            }
            events.Clear();

            if (State.IsMatchOver)
                return;

            if (DueAgent() is { } due)
            {
                var before = due.NextDecisionTime;
                due.Decide();
                if (due.NextDecisionTime <= before)
                    throw new InvalidOperationException("An agent must move its next decision forward.");
            }
            else if (!eventRan && State.ElapsedTime >= end)
            {
                return;
            }
        }
    }

    private double NextDecisionTime() =>
        _agents.Count == 0 ? double.PositiveInfinity : _agents.Min(a => a.NextDecisionTime);

    /// <summary>The first agent due now. Ties go to the one added first.</summary>
    private IMatchAgent? DueAgent()
    {
        IMatchAgent? due = null;
        foreach (var agent in _agents)
        {
            if (agent.NextDecisionTime <= State.ElapsedTime && (due is null || agent.NextDecisionTime < due.NextDecisionTime))
                due = agent;
        }
        return due;
    }
}
