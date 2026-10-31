using Valkaris.Core.Model;

namespace Valkaris.Ai;

/// <summary>What the AI last learned about a village, from a report it was part of.</summary>
public sealed record Sighting(
    int OwnerId,
    double Time,
    IReadOnlyDictionary<UnitType, int> Remaining,
    double PeakDefence,
    int WallLevel,
    double? Loyalty,
    bool CapitalProtected,
    bool AttackWon,
    double LootTaken)
{
    /// <summary>When it was last fought over. Scouting updates the sighting but is not a raid.</summary>
    public double? LastBattleTime { get; init; }

    /// <summary>The resources scouts counted, at the sighting's time. A battle makes this out of date.</summary>
    public Resources? Stock { get; init; }
}

/// <summary>Scouts died here, so its counter-scouting is strong. The defender's owner is kept, since a new owner changes everything.</summary>
public sealed record CaughtScouts(int OwnerId, double Time, int Scouts);

/// <summary>
/// Everything the AI believes about other villages. It starts from beliefs in data, which stand for a player's
/// experience, and learns only from its own reports.
/// </summary>
public sealed class AiMemory
{
    private readonly AiView _view;
    private readonly Dictionary<int, Sighting> _sightings = new();
    private readonly Dictionary<int, CaughtScouts> _caught = new();
    private readonly Dictionary<int, double> _strongestByPlayer = new();
    private int _lastReportId;

    public AiMemory(AiView view) => _view = view;

    /// <summary>The Scouts its next mission needs, kept up to date by the scout planner for the training planner.</summary>
    public int ScoutsWanted { get; set; } = 1;

    public Sighting? SightingOf(int villageId) => _sightings.GetValueOrDefault(villageId);

    /// <summary>The strongest player force in its own reports: an army that came for it, or a garrison it attacked.</summary>
    public double StrongestEnemySeen { get; private set; }

    /// <summary>The strongest force of one player in its own reports. Zero if it has none.</summary>
    public double StrongestSeenOf(int playerId) => _strongestByPlayer.GetValueOrDefault(playerId);

    private void NoteEnemyStrength(BattleReport report)
    {
        var (enemy, power) = report.DefenderId == _view.PlayerId
            ? (report.AttackerId, report.AttackPower)
            : (report.DefenderId, report.DefencePower);
        if (enemy == Village.BarbarianOwnerId)
            return;

        StrongestEnemySeen = Math.Max(StrongestEnemySeen, power);
        _strongestByPlayer[enemy] = Math.Max(_strongestByPlayer.GetValueOrDefault(enemy), power);
    }

    /// <summary>The latest failure to scout this village under its current owner, unless something newer replaced it.</summary>
    public CaughtScouts? CaughtAt(KnownVillage village) =>
        _caught.TryGetValue(village.Id, out var caught) && caught.OwnerId == village.OwnerId ? caught : null;

    /// <summary>True once any report on a village still held by that owner said its capital was protected.</summary>
    public bool SawProtectedCapital => _sightings.Values.Any(s => s.CapitalProtected);

    /// <summary>Report ids run in time order across both kinds, so the newest news is what stays.</summary>
    public void Update()
    {
        var reports = _view.ReportsSince(_lastReportId)
            .Select(r => (r.Id, Learn: (Action)(() => Remember(r))))
            .Concat(_view.ScoutReportsSince(_lastReportId).Select(r => (r.Id, Learn: (Action)(() => Remember(r)))))
            .OrderBy(r => r.Id)
            .ToList();

        foreach (var report in reports)
        {
            _lastReportId = report.Id;
            report.Learn();
        }
    }

    private void Remember(ScoutReport report)
    {
        if (!report.Succeeded)
        {
            _caught[report.TargetVillageId] = new CaughtScouts(report.DefenderId, report.Time, report.Sent.Values.Sum());
            return;
        }

        // Scouts see no loyalty or capital protection, and fight nothing, so what was known of those still stands.
        var earlier = _sightings.GetValueOrDefault(report.TargetVillageId);
        var known = earlier?.OwnerId == report.DefenderId ? earlier : null;
        var defence = DefenceOf(report.Garrison!, report.WallLevel!.Value);

        _caught.Remove(report.TargetVillageId);
        _sightings[report.TargetVillageId] = new Sighting(
            report.DefenderId,
            report.Time,
            report.Garrison!,
            Math.Max(defence, known?.PeakDefence ?? 0),
            report.WallLevel.Value,
            known?.Loyalty,
            known?.CapitalProtected ?? false,
            known?.AttackWon ?? false,
            known?.LootTaken ?? 0)
        {
            LastBattleTime = known?.LastBattleTime,
            Stock = report.Stock,
        };
    }

    private void Remember(BattleReport report)
    {
        NoteEnemyStrength(report);
        if (report.AttackerId != _view.PlayerId)
            return;

        var remaining = report.DefenderPresent.ToDictionary(t => t.Key, t => t.Value - report.DefenderLost.GetValueOrDefault(t.Key));

        // A battle without a Lord says nothing about loyalty, so what was known still stands.
        var earlier = _sightings.GetValueOrDefault(report.DefenderVillageId);
        var sameOwner = earlier?.OwnerId == report.DefenderId;
        var loyalty = report.Loyalty?.To ?? (sameOwner ? earlier!.Loyalty : null);
        var isProtected = report.Loyalty is null && (report.CapitalProtected || (sameOwner && earlier!.CapitalProtected));

        // A battle says nothing about Scouts, so a failure there still counts.
        _sightings[report.DefenderVillageId] = new Sighting(
            report.DefenderId,
            report.Time,
            remaining,
            DefenceOf(report.DefenderPresent, report.WallLevel),
            report.WallLevel,
            loyalty,
            isProtected,
            report.AttackerWon,
            report.Loot.Wood + report.Loot.Stone + report.Loot.Food)
        {
            LastBattleTime = report.Time,
        };
    }

    /// <summary>Defence power, taking the middle of defence against infantry and cavalry since the attacker's mix isn't known yet.</summary>
    public double DefenceOf(IReadOnlyDictionary<UnitType, int> troops, int wallLevel)
    {
        var total = troops.Sum(t => t.Value * (_view.Units.Units[t.Key].DefenceVsInfantry + _view.Units.Units[t.Key].DefenceVsCavalry) / 2);
        return total * (1 + _view.Combat.WallBonusPerLevel * wallLevel);
    }

    /// <summary>The defence the AI expects there now. Unwatched villages are assumed to keep growing.</summary>
    public double EstimateDefence(KnownVillage village)
    {
        var beliefs = _view.Config.Beliefs;
        var elapsedMinutes = _view.Now * _view.GameSpeed / 60;

        // A sighting from before the village changed hands says nothing about who holds it now.
        if (_sightings.TryGetValue(village.Id, out var sighting) && sighting.OwnerId == village.OwnerId)
        {
            var minutes = (_view.Now - sighting.Time) * _view.GameSpeed / 60;
            var remaining = DefenceOf(sighting.Remaining, sighting.WallLevel);

            // Scouts count only who was home, and a player's army may be out.
            if (!village.IsBarbarian && sighting.Stock is not null)
                remaining *= beliefs.ScoutedPlayerAllowance;


            // A camp's garrison comes back to what it was. A player's grows with what they can train.
            if (village.IsAbandoned)
                return remaining;

            return village.IsBarbarian
                ? Math.Min(remaining + sighting.PeakDefence * beliefs.GrowthAfterSightingPerMinute * minutes, sighting.PeakDefence)
                : remaining + beliefs.PlayerPowerPerMinute * minutes;
        }

        if (village.IsAbandoned)
            return beliefs.AbandonedPower;

        return village.BarbarianTier is { } tier
            ? beliefs.BarbarianPowerByTier[tier]
            : beliefs.PlayerPowerAtStart + beliefs.PlayerPowerPerMinute * elapsedMinutes;
    }

    /// <summary>Resources a barbarian village should hold. It refills after a raid, up to what the tier is believed to hold.</summary>
    public double ExpectedBarbarianLoot(KnownVillage village)
    {
        var beliefs = _view.Config.Beliefs;
        var full = village.IsAbandoned ? beliefs.AbandonedLoot : beliefs.BarbarianLootByTier[village.BarbarianTier!];
        var recovery = village.IsAbandoned ? 0 : beliefs.BarbarianLootRecoveryPerMinute;
        if (!_sightings.TryGetValue(village.Id, out var sighting))
            return full;

        var minutes = (_view.Now - sighting.Time) * _view.GameSpeed / 60;
        if (sighting.Stock is { } stock)
        {
            var seen = stock.Wood + stock.Stone + stock.Food;
            return village.IsAbandoned ? seen : Math.Min(full, seen + recovery * minutes);
        }

        return sighting.AttackWon ? Math.Min(full, recovery * minutes) : full;
    }
}
