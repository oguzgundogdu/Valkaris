using Valkaris.Core.Commands;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Ai;

/// <summary>Looks before it attacks: sends scouts at the next target when what it knows there has gone stale.</summary>
public sealed class ScoutPlanner
{
    private readonly AiView _view;
    private readonly AiMemory _memory;
    private readonly WarPlanner _war;

    public ScoutPlanner(AiView view, AiMemory memory, WarPlanner war)
    {
        _view = view;
        _memory = memory;
        _war = war;
    }

    public void Plan(Village village)
    {
        if (_view.Now * _view.GameSpeed < _view.Profile.AggressionDelaySeconds)
            return;
        if (_view.OwnMovements.Any(m => m.Type == MovementType.Scout && m.FromVillageId == village.Id))
            return;

        var target = NearestUnseenAbandoned(village);
        if (target is null && _war.NextTarget(village) is { } next && NeedsScouting(next))
            target = next;
        if (target is null)
            return;

        if (_view.OwnMovements.Any(m => m.Type == MovementType.Scout && m.ToVillageId == target.Id))
            return;

        var wanted = Math.Min(ScoutsFor(target), _view.Profile.MaxScoutForce);
        _memory.ScoutsWanted = wanted;

        // A first look goes with what there is, since the army waits for it. After a failure, only the full force will do.
        var scouts = Math.Min(wanted, village.TroopCount(UnitType.Scout));
        if (scouts < 1 || (scouts < wanted && _memory.CaughtAt(target) is not null))
            return;

        var command = new SendTroopsCommand(village.Id, target.Id, MovementType.Scout, new Dictionary<UnitType, int> { [UnitType.Scout] = scouts });
        if (_view.PreviewSend(command).CanSend)
            _view.Send(command);
    }

    /// <summary>A surrendered player's village may hold anything, and raids won't go until someone has looked.</summary>
    private KnownVillage? NearestUnseenAbandoned(Village village) =>
        _view.KnownVillages
            .Where(v => v.IsAbandoned && _memory.SightingOf(v.Id) is null)
            .Where(v => _memory.CaughtAt(v) is not { } caught || (_view.Now - caught.Time) * _view.GameSpeed >= _view.Config.ScoutRetrySeconds)
            .OrderBy(v => UnitRules.Distance(village.Position, v.Position))
            .ThenBy(v => v.Id)
            .FirstOrDefault();

    private bool NeedsScouting(KnownVillage target)
    {
        if (_memory.CaughtAt(target) is { } caught)
            return (_view.Now - caught.Time) * _view.GameSpeed >= _view.Config.ScoutRetrySeconds;

        return _memory.SightingOf(target.Id) is not { } sighting
            || sighting.OwnerId != target.OwnerId
            || (_view.Now - sighting.Time) * _view.GameSpeed > _view.Profile.ScoutMaxAgeSeconds;
    }

    /// <summary>Enough for the garrison it expects, and twice what was caught last time.</summary>
    private int ScoutsFor(KnownVillage target)
    {
        var troops = _memory.EstimateDefence(target) / _view.Config.Beliefs.PowerPerTroop;
        var scouts = Math.Max(1, (int)Math.Ceiling(troops / _view.Combat.TroopsPerScout));
        return _memory.CaughtAt(target) is { } caught ? Math.Max(scouts, caught.Scouts * 2) : scouts;
    }
}
