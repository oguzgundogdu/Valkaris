using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>How a contest between Scouts goes. Pure: the random number is passed in.</summary>
public static class ScoutRules
{
    public static double DefendingStrength(GameData data, Village village) =>
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

    private static int Rounded(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
