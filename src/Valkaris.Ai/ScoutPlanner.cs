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
    private readonly Forces _forces;

    public ScoutPlanner(AiView view, AiMemory memory, WarPlanner war)
    {
        _view = view;
        _memory = memory;
        _war = war;
        _forces = new Forces(view, memory);
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

        // A small party only feeds their Scouts, so it waits for the whole force.
        var scouts = wanted;
        if (village.TroopCount(UnitType.Scout) - _forces.HomeScouts(village) < scouts)
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

    /// <summary>Enough to beat what it expects there by its margin, and twice what was caught last time.</summary>
    private int ScoutsFor(KnownVillage target)
    {
        var scouts = Math.Max(1, (int)Math.Ceiling(_memory.ExpectedScoutStrength(target) * _view.Profile.ScoutMargin));
        return _memory.CaughtAt(target) is { } caught ? Math.Max(scouts, caught.Scouts * 2) : scouts;
    }
}
