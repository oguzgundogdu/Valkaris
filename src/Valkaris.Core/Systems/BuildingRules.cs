using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>
/// Building formulas, all driven by data. The speed multiplier is applied by callers,
/// so these return values at speed 1.
/// </summary>
public static class BuildingRules
{
    /// <summary>Cost of upgrading a building to the given level.</summary>
    public static Resources UpgradeCost(GameData data, BuildingType type, int level)
    {
        RequireBuildableLevel(level);
        var curve = data.Buildings.Buildings[type].Cost;
        var factor = Math.Pow(curve.Growth, level - 1);
        return new Resources(
            Rounded(curve.Base.Wood * factor),
            Rounded(curve.Base.Stone * factor),
            Rounded(curve.Base.Food * factor));
    }

    /// <summary>Seconds needed to upgrade a building to the given level.</summary>
    public static double BuildTimeSeconds(GameData data, BuildingType type, int level, NationConfig nation) =>
        BuildTimeSeconds(data, type, level) * nation.BuildTime;

    internal static double BuildTimeSeconds(GameData data, BuildingType type, int level)
    {
        RequireBuildableLevel(level);
        return Rounded(data.Buildings.Buildings[type].BuildTime.ValueAt(level));
    }

    /// <summary>Per-second output of a building at the given level. Zero at level 0 or for non-producers.</summary>
    public static Resources Production(GameData data, BuildingType type, int level)
    {
        if (level <= 0 || data.Buildings.Buildings[type].Production is not { } production)
            return Resources.Zero;
        return Resources.Of(production.Resource, production.Base * Math.Pow(production.Growth, level - 1));
    }

    /// <summary>Per-second output of all buildings in a village.</summary>
    public static Resources VillageProduction(GameData data, Village village)
    {
        var total = Resources.Zero;
        foreach (var (type, level) in village.Buildings)
            total += Production(data, type, level);
        return total;
    }

    /// <summary>Per-resource cap for a Warehouse at the given level.</summary>
    public static double WarehouseCapacity(GameData data, int warehouseLevel) =>
        data.Buildings.Buildings[BuildingType.Warehouse].Capacity!.ValueAt(warehouseLevel);

    public static double WarehouseCapacity(GameData data, int warehouseLevel, NationConfig nation) =>
        WarehouseCapacity(data, warehouseLevel) * nation.WarehouseCapacity;

    /// <summary>Follows the village's current owner.</summary>
    public static double VillageCapacity(GameData data, GameState state, Village village) =>
        WarehouseCapacity(data, village.BuildingLevel(BuildingType.Warehouse), NationRules.Of(data, state, village.OwnerId));

    internal static double VillageCapacity(GameData data, Village village) =>
        WarehouseCapacity(data, village.BuildingLevel(BuildingType.Warehouse));

    /// <summary>Highest level other buildings may reach under a Main Building of the given level.</summary>
    public static int MaxOtherBuildingLevel(GameData data, int mainBuildingLevel) =>
        data.Buildings.MaxOtherBuildingLevelByMainLevel[mainBuildingLevel];

    private static double Rounded(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    private static void RequireBuildableLevel(int level)
    {
        if (level < 1)
            throw new ArgumentOutOfRangeException(nameof(level), level, "Levels below 1 cannot be built.");
    }
}
