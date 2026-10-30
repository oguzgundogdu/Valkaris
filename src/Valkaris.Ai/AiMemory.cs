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
    double LootTaken);

/// <summary>
/// Everything the AI believes about other villages. It starts from beliefs in data, which stand for a player's
/// experience, and learns only from its own reports.
/// </summary>
public sealed class AiMemory
{
    private readonly AiView _view;
    private readonly Dictionary<int, Sighting> _sightings = new();
    private int _lastReportId;

    public AiMemory(AiView view) => _view = view;

    public Sighting? SightingOf(int villageId) => _sightings.GetValueOrDefault(villageId);

    /// <summary>True once any report on a village still held by that owner said its capital was protected.</summary>
    public bool SawProtectedCapital => _sightings.Values.Any(s => s.CapitalProtected);

    public void Update()
    {
        foreach (var report in _view.ReportsSince(_lastReportId).ToList())
        {
            _lastReportId = report.Id;
            if (report.AttackerId == _view.PlayerId)
                Remember(report);
        }
    }

    private void Remember(BattleReport report)
    {
        var remaining = report.DefenderPresent.ToDictionary(t => t.Key, t => t.Value - report.DefenderLost.GetValueOrDefault(t.Key));

        // A battle without a Lord says nothing about loyalty, so what was known still stands.
        var earlier = _sightings.GetValueOrDefault(report.DefenderVillageId);
        var sameOwner = earlier?.OwnerId == report.DefenderId;
        var loyalty = report.Loyalty?.To ?? (sameOwner ? earlier!.Loyalty : null);
        var isProtected = report.Loyalty is null && (report.CapitalProtected || (sameOwner && earlier!.CapitalProtected));

        _sightings[report.DefenderVillageId] = new Sighting(
            report.DefenderId,
            report.Time,
            remaining,
            DefenceOf(report.DefenderPresent, report.WallLevel),
            report.WallLevel,
            loyalty,
            isProtected,
            report.AttackerWon,
            report.Loot.Wood + report.Loot.Stone + report.Loot.Food);
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

            // A camp's garrison comes back to what it was. A player's grows with what they can train.
            return village.IsBarbarian
                ? Math.Min(remaining + sighting.PeakDefence * beliefs.GrowthAfterSightingPerMinute * minutes, sighting.PeakDefence)
                : remaining + beliefs.PlayerPowerPerMinute * minutes;
        }

        return village.BarbarianTier is { } tier
            ? beliefs.BarbarianPowerByTier[tier]
            : beliefs.PlayerPowerAtStart + beliefs.PlayerPowerPerMinute * elapsedMinutes;
    }

    /// <summary>Resources a barbarian village should hold. It refills after a raid, up to what the tier is believed to hold.</summary>
    public double ExpectedBarbarianLoot(KnownVillage village)
    {
        var beliefs = _view.Config.Beliefs;
        var full = beliefs.BarbarianLootByTier[village.BarbarianTier!];
        if (!_sightings.TryGetValue(village.Id, out var sighting) || !sighting.AttackWon)
            return full;

        var minutes = (_view.Now - sighting.Time) * _view.GameSpeed / 60;
        return Math.Min(full, beliefs.BarbarianLootRecoveryPerMinute * minutes);
    }
}
