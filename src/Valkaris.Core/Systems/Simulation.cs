using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>Advances time. Applies the state changes that no player command causes directly.</summary>
public sealed class Simulation
{
    private readonly GameState _state;
    private readonly GameData _data;
    private readonly List<GameEvent> _followUps = new();

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
            if (Apply(next) is { } gameEvent)
                events.Add(gameEvent);
            events.AddRange(_followUps);
            _followUps.Clear();
        }

        MoveTimeTo(end);
        return events;
    }

    private enum EventKind
    {
        Build,
        Training,
        TroopRegen,
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
            if (village.NextTroopRegenTime is { } regenTime)
                Consider(new PendingEvent(regenTime, EventKind.TroopRegen, village.Id));
        }

        foreach (var movement in _state.Movements)
            Consider(new PendingEvent(movement.ArrivalTime, EventKind.Arrival, movement.Id));

        return next;
    }

    private GameEvent? Apply(PendingEvent pending) => pending.Kind switch
    {
        EventKind.TroopRegen => RegenerateTroop(_state.Villages[pending.Id]),
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
                AddTroops(_state.Villages[movement.ToVillageId!.Value], movement.Troops);
                return new ReinforcementsArrived(movement.ArrivalTime, movement.ToVillageId!.Value);
            case MovementType.Settle:
                return CompleteSettle(movement);
            default:
                return ResolveBattle(movement);
        }
    }

    private GameEvent CompleteSettle(Movement movement)
    {
        var tile = movement.TargetTile!.Value;
        var problem = FoundPreview.TileRejection(_state, _data, tile) ?? FoundPreview.LimitRejection(_state, _data, movement.OwnerId);
        if (problem is { } reason)
        {
            StartReturn(movement.OwnerId, movement.FromVillageId, tile, movement.FromVillageId, movement.Troops, Resources.Zero, movement.ArrivalTime);
            return new SettlingFailed(movement.ArrivalTime, movement.OwnerId, reason);
        }

        var founded = _data.Game.FoundedVillage;
        var village = new Village(_state.Villages.Keys.Max() + 1, movement.OwnerId, tile)
        {
            Name = NextFoundedName(),
            Resources = founded.Resources,
        };
        foreach (var (type, level) in founded.Buildings)
            village.SetBuildingLevel(type, level);
        foreach (var (unit, count) in founded.Troops)
            village.AddTroops(unit, count);

        _state.AddVillage(village);
        _state.Map.TileAt(tile).VillageId = village.Id;
        return new VillageFounded(movement.ArrivalTime, village.Id);
    }

    /// <summary>A random unused name. If the list runs out, names repeat with a number.</summary>
    private string NextFoundedName()
    {
        var names = _data.Map.FoundedVillageNames;
        var taken = _state.Villages.Values.Select(v => v.Name).ToHashSet();
        var free = names.Where(name => !taken.Contains(name)).ToList();
        if (free.Count > 0)
            return free[_state.NextRandom(free.Count)];

        var round = 2;
        while (true)
        {
            var numbered = names.Select(name => $"{name} {round}").Where(name => !taken.Contains(name)).ToList();
            if (numbered.Count > 0)
                return numbered[_state.NextRandom(numbered.Count)];
            round++;
        }
    }

    private BattleResolved ResolveBattle(Movement movement)
    {
        var target = _state.Villages[movement.ToVillageId!.Value];
        var defenders = target.Troops.Where(t => t.Value > 0).ToDictionary(t => t.Key, t => t.Value);
        var wallLevel = target.BuildingLevel(BuildingType.Wall);

        var result = CombatResolver.Resolve(_data, movement.Type, movement.Troops, defenders, wallLevel);

        foreach (var (unit, lost) in result.DefenderLost)
            target.RemoveTroops(unit, lost);

        var survivors = movement.Troops.ToDictionary(t => t.Key, t => t.Value - result.AttackerLost[t.Key]);

        var loot = Resources.Zero;
        CatapultDamage? damage = null;
        LoyaltyChange? loyalty = null;
        var capitalProtected = false;
        if (result.AttackerWon)
        {
            loot = LootRules.Calculate(_data, target, survivors);
            target.Resources -= loot;

            if (movement.Type == MovementType.Attack)
                damage = ApplyCatapults(target, survivors.GetValueOrDefault(UnitType.Catapult), movement.CatapultTarget);

            // After the catapults, so one wave can knock a capital's Main Building down to the threshold and use it.
            if (movement.Type == MovementType.Attack && survivors.GetValueOrDefault(UnitType.Lord) > 0)
            {
                capitalProtected = LoyaltyRules.IsLocked(_state, _data, target);
                if (!capitalProtected)
                    loyalty = LowerLoyalty(target);
            }
        }

        var report = new BattleReport(
            _state.NextReportId++,
            movement.ArrivalTime,
            movement.Type,
            movement.OwnerId,
            target.OwnerId,
            movement.FromVillageId,
            target.Id,
            movement.Troops,
            result.AttackerLost,
            defenders,
            result.DefenderLost,
            wallLevel,
            result.AttackPower,
            result.DefencePower,
            result.AttackerWon,
            loot,
            damage,
            loyalty,
            capitalProtected);
        _state.AddReport(report);

        if (target.IsBarbarian)
            ScheduleTroopRegen(target, movement.ArrivalTime);

        if (loyalty is { Captured: true })
            Capture(target, movement.OwnerId, movement.ArrivalTime);

        var returning = survivors.Where(t => t.Value > 0).ToDictionary(t => t.Key, t => t.Value);
        if (returning.Count > 0)
            StartReturn(movement.OwnerId, target.Id, null, movement.FromVillageId, returning, loot, movement.ArrivalTime);
        return new BattleResolved(movement.ArrivalTime, report.Id);
    }

    private LoyaltyChange LowerLoyalty(Village village)
    {
        var config = _data.Villages.Loyalty;
        var drop = config.DropMin + _state.NextRandom(config.DropMax - config.DropMin + 1);
        var from = village.Loyalty;
        village.Loyalty = Math.Max(0, from - drop);
        return new LoyaltyChange(from, village.Loyalty, village.Loyalty <= 0);
    }

    /// <summary>The garrison is already gone: only a won attack gets here. Stored resources and buildings stay.</summary>
    private void Capture(Village village, int newOwnerId, double now)
    {
        var previousOwnerId = village.OwnerId;
        village.OwnerId = newOwnerId;
        village.Loyalty = _data.Villages.Loyalty.AfterCapture;
        village.ClearQueues();
        village.BarbarianTier = null;
        village.NextTroopRegenTime = null;
        _state.Players[newOwnerId].RecordCapture(village.Id);

        _followUps.Add(new VillageCaptured(now, village.Id, previousOwnerId, newOwnerId));
    }

    private BarbarianTierConfig TierOf(Village village) => _data.Map.Barbarians.Tiers[village.BarbarianTier!.Value];

    private void RegenerateResources(Village village, double gameSeconds)
    {
        var gain = _data.Map.Barbarians.ResourceRegenPerSecond * gameSeconds;
        var cap = TierOf(village).Resources;
        village.Resources = new Resources(
            Math.Min(cap.Wood, village.Resources.Wood + gain),
            Math.Min(cap.Stone, village.Resources.Stone + gain),
            Math.Min(cap.Food, village.Resources.Food + gain));
    }

    private void ScheduleTroopRegen(Village village, double now)
    {
        if (village.NextTroopRegenTime is null && FirstUnitBelowCap(village) is not null)
            village.NextTroopRegenTime = now + _data.Map.Barbarians.TroopRegenSeconds / _state.Settings.SpeedMultiplier;
    }

    private UnitType? FirstUnitBelowCap(Village village)
    {
        foreach (var (unit, cap) in TierOf(village).Garrison.OrderBy(g => g.Key))
        {
            if (village.TroopCount(unit) < cap)
                return unit;
        }
        return null;
    }

    private GameEvent? RegenerateTroop(Village village)
    {
        var now = village.NextTroopRegenTime!.Value;
        village.NextTroopRegenTime = null;

        if (FirstUnitBelowCap(village) is { } unit)
            village.AddTroops(unit, 1);

        ScheduleTroopRegen(village, now);
        return null;
    }

    private CatapultDamage? ApplyCatapults(Village target, int catapults, BuildingType? chosen)
    {
        if (catapults <= 0)
            return null;

        var building = chosen ?? PickRandomBuilding(target);
        if (building is not { } hit || target.BuildingLevel(hit) == 0)
            return null;

        var levels = Math.Max(1, catapults / _data.Combat.CatapultsPerLevelOfDamage);
        var from = target.BuildingLevel(hit);
        var to = Math.Max(0, from - levels);

        target.SetBuildingLevel(hit, to);
        target.LowerQueuedBuildTargets(hit, from - to);
        return new CatapultDamage(hit, from, to);
    }

    private BuildingType? PickRandomBuilding(Village village)
    {
        var standing = Enum.GetValues<BuildingType>().Where(b => village.BuildingLevel(b) > 0).ToList();
        return standing.Count == 0 ? null : standing[_state.NextRandom(standing.Count)];
    }

    private TroopsReturned CompleteReturn(Movement movement)
    {
        var home = _state.Villages[movement.ToVillageId!.Value];
        AddTroops(home, movement.Troops);
        home.Resources = (home.Resources + movement.Loot).CappedAt(BuildingRules.VillageCapacity(_data, home));
        return new TroopsReturned(movement.ArrivalTime, home.Id, movement.Loot);
    }

    private static void AddTroops(Village village, IReadOnlyDictionary<UnitType, int> troops)
    {
        foreach (var (unit, count) in troops)
            village.AddTroops(unit, count);
    }

    /// <summary>Starts a trip home. The survivors set the pace.</summary>
    private void StartReturn(
        int ownerId,
        int fromVillageId,
        TileCoord? fromTile,
        int homeVillageId,
        IReadOnlyDictionary<UnitType, int> troops,
        Resources loot,
        double now)
    {
        var from = fromTile ?? _state.Villages[fromVillageId].Position;
        var duration = UnitRules.TravelSeconds(_data, _state, from, _state.Villages[homeVillageId].Position, troops);

        _state.AddMovement(new Movement(
            _state.NextMovementId++,
            ownerId,
            fromVillageId,
            homeVillageId,
            MovementType.Return,
            troops,
            loot,
            now,
            now + duration,
            null,
            null,
            fromTile));
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
            village.Loyalty = Math.Min(Village.MaxLoyalty, village.Loyalty + LoyaltyRules.RegenPerSecond(_data, village) * gameSeconds);

            if (village.IsBarbarian)
            {
                RegenerateResources(village, gameSeconds);
                continue;
            }

            var produced = BuildingRules.VillageProduction(_data, village) * gameSeconds;
            village.Resources = (village.Resources + produced).CappedAt(BuildingRules.VillageCapacity(_data, village));
        }
    }
}
