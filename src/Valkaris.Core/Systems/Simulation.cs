using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>Advances time. Applies the state changes that no player command causes directly.</summary>
public sealed class Simulation
{
    private readonly GameState _state;
    private readonly GameData _data;

    public Simulation(GameState state, GameData data)
    {
        _state = state;
        _data = data;
    }

    /// <summary>
    /// Moves the match forward by real seconds and returns what happened, in time order.
    /// Time stops at the end of the match. The step is split at each completion, so a new rate or
    /// cap applies from that exact moment and the result does not depend on the step size.
    /// </summary>
    public IReadOnlyList<GameEvent> Advance(double deltaSeconds)
    {
        if (deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds, "Time cannot go backwards.");

        var end = Math.Min(_state.ElapsedTime + deltaSeconds, _state.Settings.MatchDurationSeconds);
        var events = new List<GameEvent>();

        while (NextCompletion(end) is { } village)
        {
            MoveTimeTo(village.BuildQueue[0].FinishTime);
            events.Add(CompleteBuild(village));
        }

        MoveTimeTo(end);
        return events;
    }

    /// <summary>The village whose next build finishes first, by or at the given time. Ties go to the lowest id.</summary>
    private Village? NextCompletion(double byTime)
    {
        Village? next = null;
        foreach (var village in _state.Villages.Values)
        {
            if (village.BuildQueue.Count == 0 || village.BuildQueue[0].FinishTime > byTime)
                continue;

            var finish = village.BuildQueue[0].FinishTime;
            if (next is null
                || finish < next.BuildQueue[0].FinishTime
                || (finish == next.BuildQueue[0].FinishTime && village.Id < next.Id))
                next = village;
        }

        return next;
    }

    private void MoveTimeTo(double time)
    {
        if (time <= _state.ElapsedTime)
            return;

        Produce(time - _state.ElapsedTime);
        _state.ElapsedTime = time;
    }

    private static BuildingCompleted CompleteBuild(Village village)
    {
        var item = village.BuildQueue[0];
        village.DequeueBuild();
        village.SetBuildingLevel(item.Building, item.TargetLevel);
        return new BuildingCompleted(item.FinishTime, village.Id, item.Building, item.TargetLevel);
    }

    private void Produce(double seconds)
    {
        var gameSeconds = seconds * _state.Settings.SpeedMultiplier;
        foreach (var village in _state.Villages.Values)
        {
            var produced = BuildingRules.VillageProduction(_data, village) * gameSeconds;
            village.Resources = (village.Resources + produced).CappedAt(BuildingRules.VillageCapacity(_data, village));
        }
    }
}
