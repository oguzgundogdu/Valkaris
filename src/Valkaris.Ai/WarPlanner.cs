using Valkaris.Core.Commands;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Ai;

/// <summary>Sends armies at its rival, and Lords at anything worth capturing, when the margins hold.</summary>
public sealed class WarPlanner
{
    private readonly AiView _view;
    private readonly AiMemory _memory;
    private readonly RivalPlanner _rival;
    private readonly Forces _forces;
    private int _strikesOnRival;
    private int _strikesElsewhere;

    public WarPlanner(AiView view, AiMemory memory, RivalPlanner rival)
    {
        _view = view;
        _memory = memory;
        _rival = rival;
        _forces = new Forces(view);
    }

    public void Plan(Village village)
    {
        if (_view.Now * _view.GameSpeed < _view.Profile.AggressionDelaySeconds)
            return;

        var spare = _forces.Spare(village);
        if (spare.Count == 0)
            return;

        var scouting = _view.OwnMovements.Where(m => m.Type == MovementType.Scout).Select(m => m.ToVillageId).ToHashSet();
        foreach (var target in Targets(village))
        {
            // The news is on its way, so the army waits for it rather than moving on to a worse target.
            if (scouting.Contains(target.Id))
                return;

            if (Attack(village, target, spare))
            {
                if (!target.IsBarbarian)
                {
                    if (target.OwnerId == _rival.Current)
                        _strikesOnRival++;
                    else
                        _strikesElsewhere++;
                }
                return;
            }
        }
    }

    /// <summary>The target this village's army would go for first, if it can take it on.</summary>
    public KnownVillage? NextTarget(Village village)
    {
        var hasLord = village.TroopCount(UnitType.Lord) > 0;
        return Targets(village).FirstOrDefault(v => !v.IsBarbarian || hasLord);
    }

    private double StrikeAttack(KnownVillage target) =>
        Math.Max(_memory.EstimateDefence(target) * _view.Profile.AttackMargin, _view.Config.Beliefs.PlayerPowerAtStart);

    /// <summary>Its rival first, unless it has had its share of attacks. Outlying villages come before capitals.</summary>
    private IEnumerable<KnownVillage> Targets(Village village)
    {
        var heading = _view.OwnMovements.Where(m => m.Type == MovementType.Attack).Select(m => m.ToVillageId).ToHashSet();
        var total = _strikesOnRival + _strikesElsewhere + 1;
        var mayGoElsewhere = (double)(_strikesElsewhere + 1) / total <= 1 - _view.Profile.RivalFocusShare;
        return _view.KnownVillages
            .Where(v => v.OwnerId != _view.PlayerId && !heading.Contains(v.Id))
            .Where(v => v.IsBarbarian || v.OwnerId == _rival.Current || mayGoElsewhere)
            .OrderByDescending(CaptureUnderWay)
            .ThenByDescending(v => v.OwnerId == _rival.Current)
            .ThenBy(v => Cost(village, v))
            .ThenBy(v => v.Id);
    }

    private double Cost(Village village, KnownVillage target)
    {
        var cost = _memory.EstimateDefence(target) * UnitRules.Distance(village.Position, target.Position);
        var hasOthers = _view.KnownVillages.Any(v => v.OwnerId == target.OwnerId && v.Id != target.Id);
        return target.IsCapital && hasOthers ? cost * _view.Config.CapitalTargetPenalty : cost;
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

        // Scouts can report an empty village, and a token force is not worth the walk.
        var army = _forces.Pick(spare, StrikeAttack(target));
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
