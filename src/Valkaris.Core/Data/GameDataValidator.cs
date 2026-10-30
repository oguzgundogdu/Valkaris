using Valkaris.Core.Model;

namespace Valkaris.Core.Data;

/// <summary>Checks values that parse correctly but would break the game. Reports every problem at once.</summary>
internal static class GameDataValidator
{
    public static IReadOnlyList<string> Validate(GameConfig game, BuildingsConfig buildings, UnitsConfig units, CombatConfig combat, MapConfig map, VillagesConfig villages, ScoringConfig scoring, AiConfig ai)
    {
        var errors = new List<string>();
        ValidateBuildings(buildings, errors);
        ValidateGame(game, buildings, errors);
        ValidateUnits(units, buildings, errors);
        ValidateCombat(combat, errors);
        ValidateMap(map, game, buildings, errors);
        ValidateVillages(villages, buildings, errors);
        ValidateScoring(scoring, errors);
        ValidateAi(ai, game, buildings, map, errors);
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

        if (config.Buildings.TryGetValue(BuildingType.Marketplace, out var marketplace))
        {
            var carriers = config.CarriersByMarketplaceLevel;
            if (carriers.Count != marketplace.MaxLevel + 1)
                Error("carriersByMarketplaceLevel",
                    $"must have {marketplace.MaxLevel + 1} entries (Marketplace levels 0 to {marketplace.MaxLevel}), found {carriers.Count}.");
            if (carriers.Any(count => count < 0))
                Error("carriersByMarketplaceLevel", "must not contain negative counts.");
        }

        if (config.Buildings.TryGetValue(BuildingType.MainBuilding, out var main))
        {
            var caps = config.MaxOtherBuildingLevelByMainLevel;
            if (caps.Count != main.MaxLevel + 1)
                Error("maxOtherBuildingLevelByMainLevel",
                    $"must have {main.MaxLevel + 1} entries (Main Building levels 0 to {main.MaxLevel}), found {caps.Count}.");
            if (caps.Any(cap => cap < 0))
                Error("maxOtherBuildingLevelByMainLevel", "must not contain negative levels.");

            var limits = config.MaxVillagesByCapitalMainLevel;
            if (limits.Count != main.MaxLevel + 1)
                Error("maxVillagesByCapitalMainLevel",
                    $"must have {main.MaxLevel + 1} entries (Main Building levels 0 to {main.MaxLevel}), found {limits.Count}.");
            if (limits.Any(limit => limit < 1))
                Error("maxVillagesByCapitalMainLevel", "must be at least 1 at every level.");
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

        foreach (var (type, count) in game.StartingTroops)
        {
            if (count < 0)
                Error($"startingTroops.{type}", "must not be negative.");
        }

        var founded = game.FoundedVillage;
        if (founded.Resources.Wood < 0 || founded.Resources.Stone < 0 || founded.Resources.Food < 0)
            Error("foundedVillage.resources", "must not contain negative amounts.");

        foreach (var type in Enum.GetValues<BuildingType>())
        {
            if (!founded.Buildings.ContainsKey(type))
                Error($"foundedVillage.buildings.{type}", "is missing.");
        }

        foreach (var (type, level) in founded.Buildings)
        {
            if (level < 0 || (buildings.Buildings.TryGetValue(type, out var definition) && level > definition.MaxLevel))
                Error($"foundedVillage.buildings.{type}", "is outside the building's levels.");
        }

        if (founded.Troops.Values.Any(count => count < 0))
            Error("foundedVillage.troops", "must not contain negative counts.");

        if (game.MaxBuildQueueLength < 1)
            Error("maxBuildQueueLength", "must be at least 1.");
        if (game.MaxTrainingQueueLength < 1)
            Error("maxTrainingQueueLength", "must be at least 1.");

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

    private static void ValidateUnits(UnitsConfig config, BuildingsConfig buildings, List<string> errors)
    {
        void Error(string field, string problem) => errors.Add($"{GameData.UnitsFile}: {field} {problem}");

        if (config.TrainingSpeedBonusPerLevel < 0)
            Error("trainingSpeedBonusPerLevel", "must not be negative.");

        foreach (var type in Enum.GetValues<UnitType>())
        {
            if (!config.Units.ContainsKey(type))
                Error($"units.{type}", "is missing.");
        }

        foreach (var (type, unit) in config.Units)
        {
            var path = $"units.{type}";

            // Combat weighs defence by the attacker's composition, so attack must never total zero.
            if (unit.Attack <= 0)
                Error($"{path}.attack", "must be positive.");
            if (unit.DefenceVsInfantry < 0)
                Error($"{path}.defenceVsInfantry", "must not be negative.");
            if (unit.DefenceVsCavalry < 0)
                Error($"{path}.defenceVsCavalry", "must not be negative.");
            if (unit.Speed <= 0)
                Error($"{path}.speed", "must be positive.");
            if (unit.LootCapacity < 0)
                Error($"{path}.lootCapacity", "must not be negative.");
            if (unit.Cost.Wood < 0 || unit.Cost.Stone < 0 || unit.Cost.Food < 0)
                Error($"{path}.cost", "must not contain negative amounts.");
            else if (unit.Cost.Wood + unit.Cost.Stone + unit.Cost.Food <= 0)
                Error($"{path}.cost", "must be positive.");
            if (unit.TrainingSeconds <= 0)
                Error($"{path}.trainingSeconds", "must be positive.");

            var barracksMax = buildings.Buildings.TryGetValue(BuildingType.Barracks, out var barracks) ? barracks.MaxLevel : int.MaxValue;
            if (unit.RequiredBarracksLevel < 1 || unit.RequiredBarracksLevel > barracksMax)
                Error($"{path}.requiredBarracksLevel", $"must be between 1 and {barracksMax}.");
        }
    }

    private static void ValidateCombat(CombatConfig config, List<string> errors)
    {
        void Error(string field, string problem) => errors.Add($"{GameData.CombatFile}: {field} {problem}");

        if (config.WallBonusPerLevel < 0)
            Error("wallBonusPerLevel", "must not be negative.");
        if (config.CasualtyExponent <= 0)
            Error("casualtyExponent", "must be positive.");
        if (config.ProtectedResourceShare is < 0 or > 1)
            Error("protectedResourceShare", "must be between 0 and 1.");
        if (config.CatapultsPerLevelOfDamage < 1)
            Error("catapultsPerLevelOfDamage", "must be at least 1.");
    }

    private static void ValidateMap(MapConfig config, GameConfig game, BuildingsConfig buildings, List<string> errors)
    {
        void Error(string field, string problem) => errors.Add($"{GameData.MapFile}: {field} {problem}");

        foreach (var size in Enum.GetValues<MapSize>())
        {
            if (!config.BarbarianCounts.TryGetValue(size, out var count))
                Error($"barbarianCounts.{size}", "is missing.");
            else if (count < 0)
                Error($"barbarianCounts.{size}", "must not be negative.");
        }

        if (config.MinVillageSpacing < 1)
            Error("minVillageSpacing", "must be at least 1.");
        if (config.MinFoundingDistance < 1)
            Error("minFoundingDistance", "must be at least 1.");
        if (config.StartJitter < 0)
            Error("startJitter", "must not be negative.");
        if (config.StartClearRadius < 0)
            Error("startClearRadius", "must not be negative.");

        var terrain = config.Terrain;
        if (terrain.NoiseCellSize < 1)
            Error("terrain.noiseCellSize", "must be at least 1.");
        if (terrain.WaterBelow is < 0 or > 1)
            Error("terrain.waterBelow", "must be between 0 and 1.");
        if (terrain.HillsAbove is < 0 or > 1 || terrain.HillsAbove <= terrain.WaterBelow)
            Error("terrain.hillsAbove", "must be between 0 and 1 and above waterBelow.");
        if (terrain.ForestAbove is < 0 or > 1)
            Error("terrain.forestAbove", "must be between 0 and 1.");

        var barbarians = config.Barbarians;
        if (barbarians.ResourceRegenPerSecond <= 0)
            Error("barbarians.resourceRegenPerSecond", "must be positive.");
        if (barbarians.TroopRegenSeconds <= 0)
            Error("barbarians.troopRegenSeconds", "must be positive.");
        if (barbarians.Tiers.Count == 0)
            Error("barbarians.tiers", "must not be empty.");

        for (var i = 0; i < barbarians.Tiers.Count; i++)
        {
            var tier = barbarians.Tiers[i];
            var path = $"barbarians.tiers[{i}]";

            if (tier.Name.Length == 0)
                Error($"{path}.name", "must not be empty.");
            if (tier.Weight < 1)
                Error($"{path}.weight", "must be at least 1.");
            if (tier.Garrison.Values.Any(count => count < 0))
                Error($"{path}.garrison", "must not contain negative counts.");
            if (tier.Resources.Wood < 0 || tier.Resources.Stone < 0 || tier.Resources.Food < 0)
                Error($"{path}.resources", "must not contain negative amounts.");

            foreach (var (type, level) in tier.Buildings)
            {
                if (level < 0 || (buildings.Buildings.TryGetValue(type, out var definition) && level > definition.MaxLevel))
                    Error($"{path}.buildings.{type}", "is outside the building's levels.");
            }
        }

        if (config.FoundedVillageNames.Count == 0 || config.FoundedVillageNames.Any(name => name.Length == 0))
            Error("foundedVillageNames", "must be a non-empty list of names.");
        if (config.PlayerVillageNames.Count < game.MaxPlayers)
            Error("playerVillageNames", $"must have at least {game.MaxPlayers} names.");
        var mostBarbarians = config.BarbarianCounts.Count == 0 ? 0 : config.BarbarianCounts.Values.Max();
        if (config.BarbarianVillageNames.Count < mostBarbarians)
            Error("barbarianVillageNames", $"must have at least {mostBarbarians} names.");
    }

    private static void ValidateVillages(VillagesConfig config, BuildingsConfig buildings, List<string> errors)
    {
        void Error(string field, string problem) => errors.Add($"{GameData.VillagesFile}: {field} {problem}");

        var loyalty = config.Loyalty;
        if (loyalty.DropMin < 1)
            Error("loyalty.dropMin", "must be at least 1.");
        if (loyalty.DropMax < loyalty.DropMin)
            Error("loyalty.dropMax", "must not be below dropMin.");
        if (loyalty.AfterCapture is <= 0 or > Village.MaxLoyalty)
            Error("loyalty.afterCapture", $"must be above 0 and at most {Village.MaxLoyalty}.");
        if (loyalty.RegenPerSecond < 0)
            Error("loyalty.regenPerSecond", "must not be negative.");
        if (loyalty.RegenPerSecondPerMainLevel < 0)
            Error("loyalty.regenPerSecondPerMainLevel", "must not be negative.");

        if (config.Carriers.Capacity < 1)
            Error("carriers.capacity", "must be at least 1.");
        if (config.Carriers.Speed <= 0)
            Error("carriers.speed", "must be positive.");

        var mainMax = buildings.Buildings.TryGetValue(BuildingType.MainBuilding, out var main) ? main.MaxLevel : int.MaxValue;
        var capital = config.Capital;
        if (capital.LockedAboveMainLevel < 0 || capital.LockedAboveMainLevel > mainMax)
            Error("capital.lockedAboveMainLevel", $"must be between 0 and {mainMax}.");
        if (capital.ChoiceSeconds <= 0)
            Error("capital.choiceSeconds", "must be positive.");
        if (capital.PenaltyMainLevel < 0 || capital.PenaltyMainLevel > mainMax)
            Error("capital.penaltyMainLevel", $"must be between 0 and {mainMax}.");
    }

    private static void ValidateScoring(ScoringConfig config, List<string> errors)
    {
        void Error(string field, string problem) => errors.Add($"{GameData.ScoringFile}: {field} {problem}");

        if (config.VillageWeight < 0)
            Error("villageWeight", "must not be negative.");
        if (config.PopulationWeight < 0)
            Error("populationWeight", "must not be negative.");
        if (config.CaptureWeight < 0)
            Error("captureWeight", "must not be negative.");

        foreach (var type in Enum.GetValues<BuildingType>())
        {
            if (!config.PopulationPerLevel.TryGetValue(type, out var weight))
                Error($"populationPerLevel.{type}", "is missing.");
            else if (weight < 0)
                Error($"populationPerLevel.{type}", "must not be negative.");
        }
    }

    private static void ValidateAi(AiConfig config, GameConfig game, BuildingsConfig buildings, MapConfig map, List<string> errors)
    {
        void Error(string field, string problem) => errors.Add($"{GameData.AiFile}: {field} {problem}");

        if (config.Names.Count < game.MaxPlayers - 1 || config.Names.Any(name => name.Length == 0))
            Error("names", $"must have at least {game.MaxPlayers - 1} names.");
        if (config.WarehouseFullShare is <= 0 or > 1)
            Error("warehouseFullShare", "must be above 0 and at most 1.");
        if (config.TrainingBatchMax < 1)
            Error("trainingBatchMax", "must be at least 1.");
        if (config.CavalryPerSwordsman < 0)
            Error("cavalryPerSwordsman", "must not be negative.");
        if (config.ReinforceShare is <= 0 or > 1)
            Error("reinforceShare", "must be above 0 and at most 1.");
        if (config.LordMinAttack < 0)
            Error("lordMinAttack", "must not be negative.");
        if (config.CapitalMainLevelWeight < 0)
            Error("capitalMainLevelWeight", "must not be negative.");
        if (config.LordsWanted < 0)
            Error("lordsWanted", "must not be negative.");
        if (config.CatapultsWanted < 0)
            Error("catapultsWanted", "must not be negative.");
        if (config.FightersBeforeSaving < 0)
            Error("fightersBeforeSaving", "must not be negative.");
        if (config.RaidMinLoot < 0)
            Error("raidMinLoot", "must not be negative.");
        if (config.BuildOrder.Count == 0)
            Error("buildOrder", "must not be empty.");

        for (var i = 0; i < config.BuildOrder.Count; i++)
        {
            var step = config.BuildOrder[i];
            if (step.Level < 1 || (buildings.Buildings.TryGetValue(step.Building, out var definition) && step.Level > definition.MaxLevel))
                Error($"buildOrder[{i}].level", "is outside the building's levels.");
        }

        var beliefs = config.Beliefs;
        foreach (var tier in map.Barbarians.Tiers)
        {
            if (!beliefs.BarbarianPowerByTier.TryGetValue(tier.Name, out var power) || power <= 0)
                Error($"beliefs.barbarianPowerByTier.{tier.Name}", "must be positive.");
            if (!beliefs.BarbarianLootByTier.TryGetValue(tier.Name, out var loot) || loot < 0)
                Error($"beliefs.barbarianLootByTier.{tier.Name}", "must not be negative.");
        }

        if (beliefs.PlayerPowerAtStart < 0)
            Error("beliefs.playerPowerAtStart", "must not be negative.");
        if (beliefs.PlayerPowerPerMinute < 0)
            Error("beliefs.playerPowerPerMinute", "must not be negative.");
        if (beliefs.GrowthAfterSightingPerMinute < 0)
            Error("beliefs.growthAfterSightingPerMinute", "must not be negative.");
        if (beliefs.BarbarianLootRecoveryPerMinute < 0)
            Error("beliefs.barbarianLootRecoveryPerMinute", "must not be negative.");

        foreach (var level in Enum.GetValues<AiDifficulty>())
        {
            if (!config.Difficulties.TryGetValue(level, out var profile))
            {
                Error($"difficulties.{level}", "is missing.");
                continue;
            }

            var path = $"difficulties.{level}";
            if (profile.DecisionSeconds <= 0)
                Error($"{path}.decisionSeconds", "must be positive.");
            if (profile.RaidMargin < 1)
                Error($"{path}.raidMargin", "must be at least 1.");
            if (profile.AttackMargin < 1)
                Error($"{path}.attackMargin", "must be at least 1.");
            if (profile.LordMargin < 1)
                Error($"{path}.lordMargin", "must be at least 1.");
            if (profile.AggressionDelaySeconds < 0)
                Error($"{path}.aggressionDelaySeconds", "must not be negative.");
            if (profile.GarrisonShare is < 0 or > 1)
                Error($"{path}.garrisonShare", "must be between 0 and 1.");
            if (profile.MaxRaidsPerVillage < 0)
                Error($"{path}.maxRaidsPerVillage", "must not be negative.");
            if (profile.RaidCooldownSeconds < 0)
                Error($"{path}.raidCooldownSeconds", "must not be negative.");
            if (profile.WantedVillages < 1)
                Error($"{path}.wantedVillages", "must be at least 1.");
            if (profile.ExpansionDelaySeconds < 0)
                Error($"{path}.expansionDelaySeconds", "must not be negative.");
        }
    }
}
