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

        return AdvanceTo(_state.ElapsedTime + deltaSeconds);
    }

    /// <summary>Moves the match to an exact time. A time already passed changes nothing.</summary>
    public IReadOnlyList<GameEvent> AdvanceTo(double time)
    {
        var events = new List<GameEvent>();
        while (StepTowards(time, events))
        {
        }
        return events;
    }

    /// <summary>
    /// Runs the next event due by the given time, or if there are none, moves to that time.
    /// Returns true if an event ran, since more may be due. This lets a caller act between any two events.
    /// </summary>
    public bool StepTowards(double time, List<GameEvent> events)
    {
        if (_state.IsMatchOver)
            return false;

        var end = Math.Max(time, _state.ElapsedTime);
        if (_state.Settings.MatchDurationSeconds is { } duration)
            end = Math.Min(end, duration);

        if (NextEvent(end) is { } next)
        {
            MoveTimeTo(next.Time);
            if (Apply(next) is { } gameEvent)
                events.Add(gameEvent);
            events.AddRange(_followUps);
            _followUps.Clear();
            return true;
        }

        MoveTimeTo(end);
        if (_state.ElapsedTime >= _state.Settings.MatchDurationSeconds)
        {
            MatchRules.EndMatch(_state, _data, MatchEndReason.TimeUp, end, _followUps);
            events.AddRange(_followUps);
            _followUps.Clear();
        }
        return false;
    }

    private enum EventKind
    {
        Build,
        Training,
        TroopRegen,
        Arrival,
        CapitalChoice,
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

        foreach (var player in _state.Players.Values)
        {
            if (player.CapitalChoiceDeadline is { } deadline)
                Consider(new PendingEvent(deadline, EventKind.CapitalChoice, player.Id));
        }

        return next;
    }

    private GameEvent? Apply(PendingEvent pending) => pending.Kind switch
    {
        EventKind.TroopRegen => RegenerateTroop(_state.Villages[pending.Id]),
        EventKind.Build => CompleteBuild(_state.Villages[pending.Id]),
        EventKind.Training => CompleteTraining(_state.Villages[pending.Id]),
        EventKind.Arrival => CompleteMovement(_state.Movements.First(m => m.Id == pending.Id)),
        EventKind.CapitalChoice => PickCapitalAutomatically(_state.Players[pending.Id], pending.Time),
        _ => throw new ArgumentOutOfRangeException(nameof(pending), pending.Kind, null),
    };

    private void MoveTimeTo(double time)
    {
        if (time <= _state.ElapsedTime)
            return;

        Produce(time - _state.ElapsedTime);
        _state.ElapsedTime = time;
    }

    private GameEvent? CompleteMovement(Movement movement)
    {
        _state.RemoveMovement(movement);

        switch (movement.Type)
        {
            case MovementType.Return:
                return CompleteReturn(movement);
            case MovementType.Reinforce:
                return CompleteReinforce(movement);
            case MovementType.Settle:
                return CompleteSettle(movement);
            case MovementType.Transfer:
                return CompleteTransfer(movement);
            case MovementType.CarrierReturn:
                return CompleteCarrierReturn(movement);
            default:
                // The attackers' side may have taken the village while they were on the way.
                if (_state.Villages[movement.ToVillageId!.Value].OwnerId == movement.OwnerId)
                    return CompleteReinforce(movement);
                return movement.Type == MovementType.Scout ? ResolveScout(movement) : ResolveBattle(movement);
        }
    }

    /// <summary>Reinforcements turn back if the village changed hands while they were on the way.</summary>
    private GameEvent? CompleteReinforce(Movement movement)
    {
        var target = _state.Villages[movement.ToVillageId!.Value];
        if (target.OwnerId != movement.OwnerId)
        {
            StartReturn(movement.OwnerId, target.Id, null, movement.FromVillageId, movement.Troops, Resources.Zero, movement.ArrivalTime);
            return null;
        }

        AddTroops(target, movement.Troops);
        return new ReinforcementsArrived(movement.ArrivalTime, target.Id);
    }

    /// <summary>A lost target sends the carriers back with the load. Otherwise they deliver and walk home empty.</summary>
    private ResourcesDelivered? CompleteTransfer(Movement movement)
    {
        var target = _state.Villages[movement.ToVillageId!.Value];
        if (target.OwnerId != movement.OwnerId)
        {
            StartCarrierReturn(movement, target, movement.Loot);
            return null;
        }

        var delivered = Deposit(target, movement.Loot);
        StartCarrierReturn(movement, target, Resources.Zero);
        return new ResourcesDelivered(movement.ArrivalTime, target.Id, delivered, movement.Loot - delivered);
    }

    private CarriersReturned? CompleteCarrierReturn(Movement movement)
    {
        var home = _state.Villages[movement.ToVillageId!.Value];
        if (home.OwnerId != movement.OwnerId)
        {
            StartCarrierReturn(movement, home, movement.Loot);
            return null;
        }

        var delivered = Deposit(home, movement.Loot);
        return new CarriersReturned(movement.ArrivalTime, home.Id, delivered, movement.Loot - delivered);
    }

    /// <summary>Adds what the warehouse can hold and returns that amount. The rest is lost.</summary>
    private Resources Deposit(Village village, Resources load)
    {
        var capacity = BuildingRules.VillageCapacity(_data, village);
        var before = village.Resources;

        // Worked out from the room left: subtracting afterwards would report rounding errors as lost resources.
        double Fits(double amount, double stock) => Math.Clamp(capacity - stock, 0, amount);
        var delivered = new Resources(Fits(load.Wood, before.Wood), Fits(load.Stone, before.Stone), Fits(load.Food, before.Food));

        village.Resources = (before + delivered).CappedAt(capacity);
        return delivered;
    }

    /// <summary>
    /// Carriers belong to the Marketplace of the village they left. With that village lost, a load still goes to the
    /// owner's nearest village, and empty carriers simply stop existing.
    /// </summary>
    private void StartCarrierReturn(Movement movement, Village from, Resources load)
    {
        var homeId = movement.Type == MovementType.Transfer ? movement.FromVillageId : movement.ToVillageId!.Value;
        if (load == Resources.Zero && _state.Villages[homeId].OwnerId != movement.OwnerId)
            return;

        var home = HomeFor(movement.OwnerId, homeId);
        _state.AddMovement(new Movement(
            _state.NextMovementId++,
            movement.OwnerId,
            from.Id,
            home.Id,
            MovementType.CarrierReturn,
            movement.Troops,
            load,
            movement.ArrivalTime,
            movement.ArrivalTime + GodRules.TravelSeconds(_state, movement.OwnerId, CarrierRules.TravelSeconds(_data, _state, from.Position, home.Position)),
            null,
            Carriers: movement.Carriers));
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

    /// <summary>Scouts against the Scouts at home, and never a battle. An undefended village is always scouted, and nobody is lost.</summary>
    private ScoutResolved ResolveScout(Movement movement)
    {
        var target = _state.Villages[movement.ToVillageId!.Value];
        var scouts = movement.Troops.Values.Sum();
        var defenders = target.TroopCount(UnitType.Scout);
        var defendStrength = ScoutRules.DefendingStrength(_data, target);

        var succeeded = defenders == 0 || _state.NextRandomShare() < ScoutRules.SuccessChance(_data, scouts / defendStrength);
        var scoutsLost = defenders == 0 ? 0 : ScoutRules.AttackersLost(scouts, scouts, defendStrength, succeeded);
        var defendersLost = defenders == 0 ? 0 : ScoutRules.DefendersLost(defenders, scouts, defendStrength);
        target.RemoveTroops(UnitType.Scout, defendersLost);

        ScoutQuality? quality = !succeeded ? null
            : defenders == 0 ? ScoutQuality.Overwhelming
            : ScoutRules.Quality(_data, scouts / defendStrength);
        var exact = quality is ScoutQuality.Clear or ScoutQuality.Overwhelming;
        var overwhelming = quality == ScoutQuality.Overwhelming;

        var report = new ScoutReport(
            _state.NextReportId++,
            movement.ArrivalTime,
            movement.OwnerId,
            target.OwnerId,
            movement.FromVillageId,
            target.Id,
            movement.Troops,
            succeeded,
            exact ? target.Troops.Where(t => t.Value > 0).ToDictionary(t => t.Key, t => t.Value) : null,
            exact ? target.BuildingLevel(BuildingType.Wall) : null,
            exact ? target.Resources : null,
            scoutsLost,
            quality,
            quality == ScoutQuality.Narrow ? TroopRangeOf(target) : null,
            quality == ScoutQuality.Narrow ? WallRangeOf(target) : null,
            overwhelming ? target.Buildings.ToDictionary(b => b.Key, b => b.Value) : null,
            overwhelming ? target.Loyalty : null);
        _state.AddScoutReport(report);

        if (target.IsBarbarian && defendersLost > 0)
            ScheduleTroopRegen(target, movement.ArrivalTime);
        else if (!target.IsBarbarian && (!succeeded || target.BuildingLevel(BuildingType.Watchtower) > 0))
            NotifyDefender(report, target, scouts, defendersLost);

        if (scouts > scoutsLost)
            StartReturn(movement.OwnerId, target.Id, null, movement.FromVillageId, new Dictionary<UnitType, int> { [UnitType.Scout] = scouts - scoutsLost }, Resources.Zero, movement.ArrivalTime);
        return new ScoutResolved(movement.ArrivalTime, report.Id);
    }

    /// <summary>Fighting troops only. Scouts are a separate question, and the contest already told the sender roughly how many.</summary>
    private ValueRange TroopRangeOf(Village target)
    {
        var troops = target.Troops.Where(t => t.Key != UnitType.Scout).Sum(t => t.Value);
        var width = ScoutRules.TroopRangeWidth(_data, troops);
        return ScoutRules.Range(troops, width, _state.NextRandom(width + 1));
    }

    private ValueRange WallRangeOf(Village target)
    {
        var width = _data.Combat.Scouting.WallRangeWidth;
        return ScoutRules.Range(target.BuildingLevel(BuildingType.Wall), width, _state.NextRandom(width + 1), _data.Buildings.Buildings[BuildingType.Wall].MaxLevel);
    }

    private void NotifyDefender(ScoutReport report, Village target, int scouts, int defendersLost)
    {
        var notice = new ScoutNotice(_state.NextReportId++, report.Time, report.SenderId, target.OwnerId, target.Id, !report.Succeeded, report.Succeeded ? null : scouts, defendersLost);
        _state.AddScoutNotice(notice);
        _followUps.Add(new ScoutNoticed(report.Time, notice.Id));
    }

    private BattleResolved ResolveBattle(Movement movement)
    {
        var target = _state.Villages[movement.ToVillageId!.Value];
        // Scouts stay out of battles, and so out of battle reports.
        var defenders = target.Troops.Where(t => t.Value > 0 && t.Key != UnitType.Scout).ToDictionary(t => t.Key, t => t.Value);
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
        _state.AddBattleArrival(new BattleArrival(
            movement.ArrivalTime, movement.OwnerId, target.OwnerId, _state.Villages[movement.FromVillageId].Name, target.Id, target.Name, result.AttackerWon));

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

    /// <summary>The garrison is already gone: only a won attack gets here, and Scouts die rather than change sides. Stored resources and buildings stay.</summary>
    private void Capture(Village village, int newOwnerId, double now)
    {
        var previousOwnerId = village.OwnerId;
        village.RemoveTroops(UnitType.Scout, village.TroopCount(UnitType.Scout));
        village.OwnerId = newOwnerId;
        village.Loyalty = _data.Villages.Loyalty.AfterCapture;
        village.ClearQueues();
        village.BarbarianTier = null;
        village.NextTroopRegenTime = null;
        _state.Players[newOwnerId].RecordCapture(village.Id);

        _followUps.Add(new VillageCaptured(now, village.Id, previousOwnerId, newOwnerId));
        if (previousOwnerId != Village.BarbarianOwnerId)
            HandleLostVillage(_state.Players[previousOwnerId], village.Id, now);
    }

    private void HandleLostVillage(Player player, int villageId, double now)
    {
        if (_state.Villages.Values.All(v => v.OwnerId != player.Id))
        {
            MatchRules.TakeOut(_state, _data, player, now, new PlayerEliminated(now, player.Id), _followUps);
            return;
        }

        if (player.CapitalVillageId != villageId)
            return;

        player.CapitalVillageId = null;
        player.CapitalChoiceDeadline = now + _data.Villages.Capital.ChoiceSeconds;
        _followUps.Add(new CapitalChoiceRequired(now, player.Id, player.CapitalChoiceDeadline.Value));
    }

    private CapitalChanged PickCapitalAutomatically(Player player, double now)
    {
        var village = CapitalRules.AutomaticChoice(_state, player.Id);
        CapitalRules.Assign(_data, player, village);
        return new CapitalChanged(now, player.Id, village.Id);
    }

    private BarbarianTierConfig TierOf(Village village) => _data.Map.Barbarians.Tiers[village.BarbarianTier!.Value];

    private void RegenerateResources(Village village, double gameSeconds)
    {
        if (village.IsAbandoned)
            return;

        var gain = _data.Map.Barbarians.ResourceRegenPerSecond * gameSeconds;
        var cap = TierOf(village).Resources;
        village.Resources = new Resources(
            Math.Min(cap.Wood, village.Resources.Wood + gain),
            Math.Min(cap.Stone, village.Resources.Stone + gain),
            Math.Min(cap.Food, village.Resources.Food + gain));
    }

    private void ScheduleTroopRegen(Village village, double now)
    {
        if (!village.IsAbandoned && village.NextTroopRegenTime is null && FirstUnitBelowCap(village) is not null)
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

    private TroopsReturned? CompleteReturn(Movement movement)
    {
        var home = _state.Villages[movement.ToVillageId!.Value];
        if (home.OwnerId != movement.OwnerId)
        {
            StartReturn(movement.OwnerId, home.Id, null, home.Id, movement.Troops, movement.Loot, movement.ArrivalTime);
            return null;
        }

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
        var home = HomeFor(ownerId, homeVillageId);
        var duration = GodRules.TravelSeconds(_state, ownerId, UnitRules.TravelSeconds(_data, _state, from, home.Position, troops));

        _state.AddMovement(new Movement(
            _state.NextMovementId++,
            ownerId,
            fromVillageId,
            home.Id,
            MovementType.Return,
            troops,
            loot,
            now,
            now + duration,
            null,
            null,
            fromTile));
    }

    /// <summary>The village itself if the player still owns it, otherwise their nearest one. Lowest id on a tie.</summary>
    private Village HomeFor(int ownerId, int villageId)
    {
        var wanted = _state.Villages[villageId];
        return wanted.OwnerId == ownerId
            ? wanted
            : _state.Villages.Values
                .Where(v => v.OwnerId == ownerId)
                .OrderBy(v => UnitRules.Distance(v.Position, wanted.Position))
                .ThenBy(v => v.Id)
                .First();
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
