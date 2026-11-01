using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>How a contest between Scouts goes. Pure: the random number is passed in.</summary>
public static class ScoutRules
{
    public static double DefendingStrength(GameData data, GameState state, Village village) =>
        DefendingStrength(data, village) * NationRules.Of(data, state, village.OwnerId).ScoutStrength;

    internal static double DefendingStrength(GameData data, Village village) =>
        village.TroopCount(UnitType.Scout) * (1 + data.Combat.Scouting.WatchtowerBonusPerLevel * village.BuildingLevel(BuildingType.Watchtower));

    /// <summary>The chance from the curve in data: linear between its points, rising from zero at ratio 0, and flat after the last.</summary>
    public static double SuccessChance(GameData data, double ratio)
    {
        var (lastRatio, lastChance) = (0.0, 0.0);
        foreach (var point in data.Combat.Scouting.SuccessCurve)
        {
            if (ratio <= point.Ratio)
                return lastChance + (point.Chance - lastChance) * (ratio - lastRatio) / (point.Ratio - lastRatio);
            (lastRatio, lastChance) = (point.Ratio, point.Chance);
        }
        return lastChance;
    }

    /// <summary>On a success at least one attacker gets home, or it would look like a total loss.</summary>
    public static int AttackersLost(int attackers, double attackStrength, double defendStrength, bool succeeded) =>
        succeeded
            ? Math.Min(attackers - 1, Rounded(attackers * defendStrength / (attackStrength + defendStrength)))
            : attackers;

    public static int DefendersLost(int defenders, double attackStrength, double defendStrength) =>
        Rounded(defenders * attackStrength / (attackStrength + defendStrength));

    public static ScoutQuality Quality(GameData data, double ratio) =>
        ratio >= data.Combat.Scouting.OverwhelmingRatio ? ScoutQuality.Overwhelming
        : ratio >= data.Combat.Scouting.ClearRatio ? ScoutQuality.Clear
        : ScoutQuality.Narrow;

    public static int TroopRangeWidth(GameData data, int troops) =>
        Math.Max(data.Combat.Scouting.TroopRangeMinWidth, (int)Math.Ceiling(troops * data.Combat.Scouting.TroopRangeShare));

    /// <summary>
    /// A range of this width around the true value, which always contains it. Where the value sits is chosen by
    /// the offset, from 0 to width, so the middle gives nothing away. It stays between zero and the maximum, if there is one.
    /// </summary>
    public static ValueRange Range(int value, int width, int offset, int? max = null)
    {
        var low = Math.Max(0, value - offset);
        if (max is { } limit)
            low = Math.Max(0, Math.Min(low, limit - width));
        return new ValueRange(low, max is { } top ? Math.Min(top, low + width) : low + width);
    }

    private static int Rounded(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
