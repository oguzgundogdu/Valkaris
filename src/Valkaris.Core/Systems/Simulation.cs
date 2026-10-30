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
    /// Time stops at the end of the match. The step is split at each event, so a new rate or
    /// cap applies from that exact moment and the result does not depend on the step size.
    /// </summary>
    public IReadOnlyList<GameEvent> Advance(double deltaSeconds)
    {
        if (deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds, "Time cannot go backwards.");

        var end = Math.Min(_state.ElapsedTime + deltaSeconds, _state.Settings.MatchDurationSeconds);
        var events = new List<GameEvent>();

        while (NextEvent(end) is { } next)
        {
            MoveTimeTo(next.Time);
            events.Add(Apply(next));
        }

        MoveTimeTo(end);
        return events;
    }

    private enum EventKind
    {
        Build,
        Training,
        Arrival,
    }

    /// <summary>Events at the same moment run in this kind order, then by id, so results are deterministic.</summary>
    private readonly record struct PendingEvent(double Time, EventKind Kind, int Id)
    {
        public bool IsBefore(PendingEvent other) =>
            (Time, (int)Kind, Id).CompareTo((other.Time, (int)other.Kind, other.Id)) < 0;
    }

    /// <summary>The first event due by the given time.</summary>
    private PendingEvent? NextEvent(double byTime)
    {
        PendingEvent? next = null;

        void Consider(PendingEvent candidate)
        {
            if (candidate.Time <= byTime && (next is null || candidate.IsBefore(next.Value)))
                next = candidate;
        }

        foreach (var village in _state.Villages.Values)
        {
            if (village.BuildQueue.Count > 0)
                Consider(new PendingEvent(village.BuildQueue[0].FinishTime, EventKind.Build, village.Id));
            if (village.TrainingQueue.Count > 0)
                Consider(new PendingEvent(village.TrainingQueue[0].FinishTime, EventKind.Training, village.Id));
        }

        foreach (var movement in _state.Movements)
            Consider(new PendingEvent(movement.ArrivalTime, EventKind.Arrival, movement.Id));

        return next;
    }

    private GameEvent Apply(PendingEvent pending) => pending.Kind switch
    {
        EventKind.Build => CompleteBuild(_state.Villages[pending.Id]),
        EventKind.Training => CompleteTraining(_state.Villages[pending.Id]),
        EventKind.Arrival => CompleteMovement(_state.Movements.First(m => m.Id == pending.Id)),
        _ => throw new ArgumentOutOfRangeException(nameof(pending), pending.Kind, null),
    };

    private void MoveTimeTo(double time)
    {
        if (time <= _state.ElapsedTime)
            return;

        Produce(time - _state.ElapsedTime);
        _state.ElapsedTime = time;
    }

    private GameEvent CompleteMovement(Movement movement)
    {
        _state.RemoveMovement(movement);

        switch (movement.Type)
        {
            case MovementType.Return:
                return CompleteReturn(movement);
            case MovementType.Reinforce:
                AddTroops(_state.Villages[movement.ToVillageId], movement.Troops);
                return new ReinforcementsArrived(movement.ArrivalTime, movement.ToVillageId);
            default:
                // Battles are resolved in the next task; until then attackers simply turn back.
                SendHome(movement, movement.Troops, Resources.Zero);
                return new ReinforcementsArrived(movement.ArrivalTime, movement.ToVillageId);
        }
    }

    private TroopsReturned CompleteReturn(Movement movement)
    {
        var home = _state.Villages[movement.ToVillageId];
        AddTroops(home, movement.Troops);
        home.Resources = (home.Resources + movement.Loot).CappedAt(BuildingRules.VillageCapacity(_data, home));
        return new TroopsReturned(movement.ArrivalTime, home.Id, movement.Loot);
    }

    private static void AddTroops(Village village, IReadOnlyDictionary<UnitType, int> troops)
    {
        foreach (var (unit, count) in troops)
            village.AddTroops(unit, count);
    }

    /// <summary>Starts the trip home from where the movement arrived. The survivors set the pace.</summary>
    private void SendHome(Movement movement, IReadOnlyDictionary<UnitType, int> survivors, Resources loot)
    {
        if (survivors.Values.All(count => count == 0))
            return;

        var from = _state.Villages[movement.ToVillageId];
        var home = _state.Villages[movement.FromVillageId];
        var duration = UnitRules.TravelSeconds(_data, _state, from.Position, home.Position, survivors);

        _state.AddMovement(new Movement(
            _state.NextMovementId++,
            movement.OwnerId,
            movement.ToVillageId,
            movement.FromVillageId,
            MovementType.Return,
            survivors,
            loot,
            movement.ArrivalTime,
            movement.ArrivalTime + duration,
            null));
    }

    private static TroopsTrained CompleteTraining(Village village)
    {
        var item = village.TrainingQueue[0];
        village.DequeueTraining();
        village.AddTroops(item.Unit, item.Count);
        return new TroopsTrained(item.FinishTime, village.Id, item.Unit, item.Count);
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
