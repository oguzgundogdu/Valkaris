using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>Unit formulas, driven by data. Values are at speed 1 unless a method takes the state.</summary>
public static class UnitRules
{
    /// <summary>Seconds to train one unit at the given Barracks level. Level 1 is the base time.</summary>
    public static double SecondsPerUnit(GameData data, UnitType unit, int barracksLevel, NationConfig nation) =>
        SecondsPerUnit(data, unit, barracksLevel) * nation.TrainingTime;

    internal static double SecondsPerUnit(GameData data, UnitType unit, int barracksLevel)
    {
        var speedUp = 1 + data.Units.TrainingSpeedBonusPerLevel * Math.Max(0, barracksLevel - 1);
        return data.Units.Units[unit].TrainingSeconds / speedUp;
    }

    /// <summary>What one unit costs this nation.</summary>
    public static Resources Cost(GameData data, UnitType unit, NationConfig nation) =>
        data.Units.Units[unit].Cost * nation.UnitCost;

    /// <summary>The most units the given resources can pay for.</summary>
    public static int MaxAffordable(GameData data, UnitType unit, Resources available, NationConfig nation)
    {
        var cost = Cost(data, unit, nation);
        var max = int.MaxValue;
        foreach (var resource in Enum.GetValues<ResourceType>())
        {
            if (cost.Get(resource) > 0)
                max = Math.Min(max, Whole.Down(available.Get(resource) / cost.Get(resource)));
        }
        return max;
    }

    public static double Distance(TileCoord a, TileCoord b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static double TravelSeconds(
        GameData data, GameState state, TileCoord from, TileCoord to, IReadOnlyDictionary<UnitType, int> troops, NationConfig nation) =>
        TravelSeconds(data, state, from, to, troops) / nation.UnitSpeed;

    /// <summary>The group moves at its slowest unit's pace. Scaled by game speed.</summary>
    internal static double TravelSeconds(
        GameData data, GameState state, TileCoord from, TileCoord to, IReadOnlyDictionary<UnitType, int> troops)
    {
        var slowest = troops.Where(t => t.Value > 0).Min(t => data.Units.Units[t.Key].Speed);
        return Distance(from, to) / slowest / state.Settings.SpeedMultiplier;
    }
}
