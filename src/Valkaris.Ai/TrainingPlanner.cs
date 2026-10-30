using Valkaris.Core.Commands;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Ai;

/// <summary>Keeps a share of the troops as a garrison and the rest as a strike force, in ratios from data.</summary>
public sealed class TrainingPlanner
{
    private readonly AiView _view;
    private readonly AiMemory _memory;

    public TrainingPlanner(AiView view, AiMemory memory)
    {
        _view = view;
        _memory = memory;
    }

    /// <summary>True while the army is smaller than the profile wants by now, across all villages.</summary>
    public bool IsBehind()
    {
        var wanted = _view.Profile.FightersPerMinute * _view.Now * _view.GameSpeed / 60;
        return _view.OwnVillages.Sum(v => Count(v, null)) < wanted;
    }

    private bool IsAtArmyLimit() =>
        _view.Profile.MaxFightersPerMinute is { } limit
        && _view.OwnVillages.Sum(v => Count(v, null)) >= limit * _view.Now * _view.GameSpeed / 60;

    /// <summary>Trains one batch. It leaves what the next build needs, unless the village is under attack.</summary>
    public void Plan(Village village, bool threatened, Resources saving)
    {
        var available = threatened ? village.Resources : Remaining(village.Resources, saving);

        if (!threatened && SpecialWanted(village) is { } special)
        {
            var option = _view.PreviewTrain(village.Id, special, 1);
            if (option.CanTrain)
            {
                _view.Send(new TrainCommand(village.Id, special, 1));
                return;
            }

            // Saving up: fighters would otherwise eat everything a Lord or Settler costs.
            if (option.Rejection == CommandRejection.InsufficientResources
                && _view.Profile.SavesForGoals
                && Count(village, null) >= _view.Config.FightersBeforeSaving)
                return;
        }

        if (IsAtArmyLimit())
            return;

        foreach (var unit in Wanted(village, threatened))
        {
            var count = Math.Min(Affordable(unit, available), _view.Config.TrainingBatchMax);
            if (count < 1)
                continue;

            var option = _view.PreviewTrain(village.Id, unit, count);
            if (option.Rejection == CommandRejection.QueueFull)
                return;
            if (option.CanTrain)
            {
                _view.Send(new TrainCommand(village.Id, unit, count));
                return;
            }
        }
    }

    /// <summary>True while this village is saving for a Lord or Settler, which should come before further buildings.</summary>
    public bool IsSavingForGoal(Village village) =>
        _view.Profile.SavesForGoals && SpecialWanted(village) is UnitType.Lord or UnitType.Settler;

    /// <summary>A unit trained one at a time for a purpose, rather than as part of the army's ratios.</summary>
    private UnitType? SpecialWanted(Village village)
    {
        var barracks = village.BuildingLevel(BuildingType.Barracks);
        var pastDelay = _view.Now * _view.GameSpeed >= _view.Profile.AggressionDelaySeconds;

        if (pastDelay
            && barracks >= _view.Units.Units[UnitType.Lord].RequiredBarracksLevel
            && CountEverywhere(UnitType.Lord) < _view.Config.LordsWanted)
            return UnitType.Lord;

        if (WantsSettler(barracks))
            return UnitType.Settler;

        if (_memory.SawProtectedCapital
            && barracks >= _view.Units.Units[UnitType.Catapult].RequiredBarracksLevel
            && CountEverywhere(UnitType.Catapult) < _view.Config.CatapultsWanted)
            return UnitType.Catapult;

        return null;
    }

    private bool WantsSettler(int barracks)
    {
        if (_view.Now * _view.GameSpeed < _view.Profile.ExpansionDelaySeconds
            || barracks < _view.Units.Units[UnitType.Settler].RequiredBarracksLevel
            || _view.CapitalVillageId is not { } capital)
            return false;

        var limit = _view.Buildings.MaxVillagesByCapitalMainLevel[_view.OwnVillages.First(v => v.Id == capital).BuildingLevel(BuildingType.MainBuilding)];
        var settling = CountEverywhere(UnitType.Settler);
        return _view.OwnVillages.Count + settling < Math.Min(limit, _view.Profile.WantedVillages);
    }

    private int CountEverywhere(UnitType unit) =>
        _view.OwnVillages.Sum(v => v.TroopCount(unit) + v.TrainingQueue.Where(q => q.Unit == unit).Sum(q => q.Count))
        + _view.OwnMovements.Sum(m => m.Troops.GetValueOrDefault(unit));

    /// <summary>Most needed first. An under-attack village wants defenders above all.</summary>
    private IEnumerable<UnitType> Wanted(Village village, bool threatened)
    {
        var total = Count(village, null);
        var spears = Count(village, UnitType.Spearman);
        if (threatened || spears < total * _view.Profile.GarrisonShare)
            yield return UnitType.Spearman;

        var swords = Count(village, UnitType.Swordsman);
        var cavalry = Count(village, UnitType.Cavalry);
        if (cavalry < swords * _view.Config.CavalryPerSwordsman)
            yield return UnitType.Cavalry;
        yield return UnitType.Swordsman;
        yield return UnitType.Spearman;
    }

    /// <summary>Troops home, on the way back and queued, so the ratios don't swing while an army is out. Null counts all fighting units.</summary>
    private int Count(Village village, UnitType? unit)
    {
        int Matching(IEnumerable<KeyValuePair<UnitType, int>> troops) =>
            troops.Where(t => unit is null ? IsFighter(t.Key) : t.Key == unit).Sum(t => t.Value);

        var queued = village.TrainingQueue.Where(q => unit is null ? IsFighter(q.Unit) : q.Unit == unit).Sum(q => q.Count);
        var away = _view.OwnMovements
            .Where(m => m.FromVillageId == village.Id || m.ToVillageId == village.Id)
            .Sum(m => Matching(m.Troops));
        return Matching(village.Troops) + queued + away;
    }

    private static bool IsFighter(UnitType unit) => unit is UnitType.Spearman or UnitType.Swordsman or UnitType.Cavalry;

    private int Affordable(UnitType unit, Resources available)
    {
        var cost = _view.Units.Units[unit].Cost;
        var count = int.MaxValue;
        foreach (var resource in Enum.GetValues<ResourceType>())
        {
            if (cost.Get(resource) > 0)
                count = Math.Min(count, Whole.Down(available.Get(resource) / cost.Get(resource)));
        }
        return count;
    }

    private static Resources Remaining(Resources stock, Resources saving) => new(
        Math.Max(0, stock.Wood - saving.Wood),
        Math.Max(0, stock.Stone - saving.Stone),
        Math.Max(0, stock.Food - saving.Food));
}
