using Valkaris.Core.Commands;
using Valkaris.Core.Model;

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
        _forces = new Forces(view);
    }

    public void Plan(Village village)
    {
        if (_view.Now * _view.GameSpeed < _view.Profile.AggressionDelaySeconds)
            return;
        if (_view.OwnMovements.Any(m => m.Type == MovementType.Scout && m.FromVillageId == village.Id))
            return;

        if (_war.NextTarget(village) is not { } target || !NeedsScouting(target))
            return;
        if (_view.OwnMovements.Any(m => m.Type == MovementType.Scout && m.ToVillageId == target.Id))
            return;

        var group = _forces.PickFastest(_forces.Spare(village), ScoutsFor(target));
        if (group is null)
            return;

        var command = new SendTroopsCommand(village.Id, target.Id, MovementType.Scout, group);
        if (_view.PreviewSend(command).CanSend)
            _view.Send(command);
    }

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
