using Valkaris.Core.Commands;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Ai;

/// <summary>Sends armies at players, and Lords at anything worth capturing, when the margins hold.</summary>
public sealed class WarPlanner
{
    private readonly AiView _view;
    private readonly AiMemory _memory;
    private readonly Forces _forces;

    public WarPlanner(AiView view, AiMemory memory)
    {
        _view = view;
        _memory = memory;
        _forces = new Forces(view);
    }

    public void Plan(Village village)
    {
        if (_view.Now * _view.GameSpeed < _view.Profile.AggressionDelaySeconds)
            return;

        var spare = _forces.Spare(village);
        if (spare.Count == 0)
            return;

        var heading = _view.OwnMovements.Where(m => m.Type == MovementType.Attack).Select(m => m.ToVillageId).ToHashSet();
        var targets = _view.KnownVillages
            .Where(v => v.OwnerId != _view.PlayerId && !heading.Contains(v.Id))
            .OrderByDescending(CaptureUnderWay)
            .ThenBy(v => _memory.EstimateDefence(v) * UnitRules.Distance(village.Position, v.Position))
            .ThenBy(v => v.Id);

        foreach (var target in targets)
        {
            if (Attack(village, target, spare))
                return;
        }
    }

    /// <summary>Finish what was started: a target whose loyalty is already falling comes first.</summary>
    private bool CaptureUnderWay(KnownVillage target) =>
        _memory.SightingOf(target.Id) is { Loyalty: not null } sighting && sighting.OwnerId == target.OwnerId;

    private bool Attack(Village village, KnownVillage target, Dictionary<UnitType, int> spare)
    {
        var estimate = _memory.EstimateDefence(target);
        var sighting = _memory.SightingOf(target.Id);
        var knownProtected = sighting is { CapitalProtected: true } && sighting.OwnerId == target.OwnerId;
        var catapults = knownProtected ? village.TroopCount(UnitType.Catapult) : 0;
        var hasLord = village.TroopCount(UnitType.Lord) > 0;

        // Lords only go with a margin that should see them survive, and not at a capital they can't yet affect.
        // With a capture under way, the others wait for it rather than opening another front.
        var focused = !_view.KnownVillages.Any(v => v.OwnerId != _view.PlayerId && CaptureUnderWay(v)) || CaptureUnderWay(target);
        if (hasLord && focused && (!knownProtected || catapults > 0))
        {
            var group = _forces.Pick(spare, Math.Max(estimate * _view.Profile.LordMargin, _view.Config.LordMinAttack));
            if (group is not null)
            {
                group[UnitType.Lord] = 1;
                return Send(village, target, group, catapults);
            }
        }

        // Without a Lord there is nothing to gain from a barbarian village that a raid wouldn't bring.
        if (target.IsBarbarian)
            return false;

        var army = _forces.Pick(spare, estimate * _view.Profile.AttackMargin);
        return army is not null && Send(village, target, army, catapults);
    }

    private bool Send(Village village, KnownVillage target, Dictionary<UnitType, int> group, int catapults)
    {
        BuildingType? aim = null;
        if (catapults > 0)
        {
            group[UnitType.Catapult] = catapults;
            aim = BuildingType.MainBuilding;
        }

        var command = new SendTroopsCommand(village.Id, target.Id, MovementType.Attack, group, aim);
        return _view.PreviewSend(command).CanSend && _view.Send(command);
    }
}
