using Valkaris.Core.Model;

namespace Valkaris.Core.Data;

/// <summary>Checks values that parse correctly but would break the game. Reports every problem at once.</summary>
internal static class GameDataValidator
{
    public static IReadOnlyList<string> Validate(GameConfig game, BuildingsConfig buildings)
    {
        var errors = new List<string>();
        ValidateBuildings(buildings, errors);
        ValidateGame(game, buildings, errors);
        return errors;
    }

    private static void ValidateBuildings(BuildingsConfig config, List<string> errors)
    {
        void Error(string field, string problem) => errors.Add($"{GameData.BuildingsFile}: {field} {problem}");

        foreach (var type in Enum.GetValues<BuildingType>())
        {
            if (!config.Buildings.ContainsKey(type))
                Error($"buildings.{type}", "is missing.");
        }

        foreach (var (type, building) in config.Buildings)
        {
            var path = $"buildings.{type}";

            if (building.MaxLevel < 1)
                Error($"{path}.maxLevel", "must be at least 1.");

            var cost = building.Cost.Base;
            if (cost.Wood < 0 || cost.Stone < 0 || cost.Food < 0)
                Error($"{path}.cost.base", "must not contain negative amounts.");
            else if (cost.Wood + cost.Stone + cost.Food <= 0)
                Error($"{path}.cost.base", "must be positive.");
            RequireGrowth(building.Cost.Growth, $"{path}.cost.growth", Error);

            if (building.BuildTime.Base <= 0)
                Error($"{path}.buildTime.base", "must be positive.");
            RequireGrowth(building.BuildTime.Growth, $"{path}.buildTime.growth", Error);

            if (building.Production is { } production)
            {
                if (production.Base <= 0)
                    Error($"{path}.production.base", "must be positive.");
                RequireGrowth(production.Growth, $"{path}.production.growth", Error);
            }

            if (building.Capacity is { } capacity)
            {
                if (capacity.AtLevelZero <= 0)
                    Error($"{path}.capacity.atLevelZero", "must be positive.");
                if (capacity.Base <= 0)
                    Error($"{path}.capacity.base", "must be positive.");
                RequireGrowth(capacity.Growth, $"{path}.capacity.growth", Error);
            }
        }

        foreach (var resource in Enum.GetValues<ResourceType>())
        {
            var producers = config.Buildings.Values.Count(b => b.Production?.Resource == resource);
            if (producers != 1)
                Error("buildings", $"must have exactly one producer of {resource}, found {producers}.");
        }

        if (config.Buildings.TryGetValue(BuildingType.Warehouse, out var warehouse) && warehouse.Capacity is null)
            Error($"buildings.{BuildingType.Warehouse}.capacity", "is missing.");

        if (config.Buildings.TryGetValue(BuildingType.MainBuilding, out var main))
        {
            var caps = config.MaxOtherBuildingLevelByMainLevel;
            if (caps.Count != main.MaxLevel + 1)
                Error("maxOtherBuildingLevelByMainLevel",
                    $"must have {main.MaxLevel + 1} entries (Main Building levels 0 to {main.MaxLevel}), found {caps.Count}.");
            if (caps.Any(cap => cap < 0))
                Error("maxOtherBuildingLevelByMainLevel", "must not contain negative levels.");
        }
    }

    private static void RequireGrowth(double growth, string field, Action<string, string> error)
    {
        // GDD section 6: each level costs and takes more than the last.
        if (growth <= 1)
            error(field, "must be greater than 1.");
    }

    private static void ValidateGame(GameConfig game, BuildingsConfig buildings, List<string> errors)
    {
        void Error(string field, string problem) => errors.Add($"{GameData.GameFile}: {field} {problem}");

        var start = game.StartingResources;
        if (start.Wood < 0 || start.Stone < 0 || start.Food < 0)
            Error("startingResources", "must not contain negative amounts.");

        foreach (var type in Enum.GetValues<BuildingType>())
        {
            if (!game.StartingBuildings.ContainsKey(type))
                Error($"startingBuildings.{type}", "is missing.");
        }

        foreach (var (type, level) in game.StartingBuildings)
        {
            if (level < 0)
                Error($"startingBuildings.{type}", "must not be negative.");
            else if (buildings.Buildings.TryGetValue(type, out var definition) && level > definition.MaxLevel)
                Error($"startingBuildings.{type}", $"is above the max level of {definition.MaxLevel}.");
        }

        if (game.StartingBuildings.TryGetValue(BuildingType.Warehouse, out var warehouseLevel)
            && buildings.Buildings.TryGetValue(BuildingType.Warehouse, out var warehouse)
            && warehouse.Capacity is { } capacity)
        {
            var cap = capacity.ValueAt(warehouseLevel);
            if (start.Wood > cap || start.Stone > cap || start.Food > cap)
                Error("startingResources", $"must fit the starting warehouse capacity of {cap}.");
        }

        if (game.MaxBuildQueueLength < 1)
            Error("maxBuildQueueLength", "must be at least 1.");

        if (game.MinPlayers < 1)
            Error("minPlayers", "must be at least 1.");
        if (game.MaxPlayers < game.MinPlayers)
            Error("maxPlayers", "must not be below minPlayers.");

        if (game.MatchDurationsMinutes.Count == 0 || game.MatchDurationsMinutes.Any(d => d <= 0))
            Error("matchDurationsMinutes", "must be a non-empty list of positive values.");
        if (game.SpeedMultipliers.Count == 0 || game.SpeedMultipliers.Any(s => s <= 0))
            Error("speedMultipliers", "must be a non-empty list of positive values.");

        foreach (var size in Enum.GetValues<MapSize>())
        {
            if (!game.MapSizes.TryGetValue(size, out var definition))
                Error($"mapSizes.{size}", "is missing.");
            else if (definition.Width < 1 || definition.Height < 1)
                Error($"mapSizes.{size}", "must have a positive width and height.");
        }
    }
}
