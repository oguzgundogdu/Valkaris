using Valkaris.Core.Model;

namespace Valkaris.Core.Data;

/// <summary>data/game.json</summary>
public sealed record GameConfig
{
    public required Resources StartingResources { get; init; }
    public required IReadOnlyDictionary<BuildingType, int> StartingBuildings { get; init; }
    public required IReadOnlyDictionary<UnitType, int> StartingTroops { get; init; }
    public required FoundedVillageConfig FoundedVillage { get; init; }
    public required int MaxBuildQueueLength { get; init; }
    public required int MaxTrainingQueueLength { get; init; }
    public required int MinPlayers { get; init; }
    public required int MaxPlayers { get; init; }
    public required IReadOnlyList<double> MatchDurationsMinutes { get; init; }
    public required IReadOnlyList<double> SpeedMultipliers { get; init; }
    public required IReadOnlyDictionary<MapSize, MapSizeDefinition> MapSizes { get; init; }
}

/// <summary>What a Settler's new village starts with.</summary>
public sealed record FoundedVillageConfig
{
    public required Resources Resources { get; init; }
    public required IReadOnlyDictionary<BuildingType, int> Buildings { get; init; }
    public required IReadOnlyDictionary<UnitType, int> Troops { get; init; }
}

public sealed record MapSizeDefinition
{
    public required int Width { get; init; }
    public required int Height { get; init; }
}

/// <summary>data/buildings.json</summary>
public sealed record BuildingsConfig
{
    /// <summary>Indexed by Main Building level, starting at level 0.</summary>
    public required IReadOnlyList<int> MaxOtherBuildingLevelByMainLevel { get; init; }

    /// <summary>The founding limit, indexed by the capital's Main Building level, starting at level 0.</summary>
    public required IReadOnlyList<int> MaxVillagesByCapitalMainLevel { get; init; }

    /// <summary>Indexed by Marketplace level, starting at level 0.</summary>
    public required IReadOnlyList<int> CarriersByMarketplaceLevel { get; init; }

    public required IReadOnlyDictionary<BuildingType, BuildingDefinition> Buildings { get; init; }
}

public sealed record BuildingDefinition
{
    public required int MaxLevel { get; init; }
    public required CostCurve Cost { get; init; }
    public required GrowthCurve BuildTime { get; init; }
    public ProductionDefinition? Production { get; init; }
    public CapacityDefinition? Capacity { get; init; }
}

/// <summary>Value at level L = base * growth^(L-1), for L >= 1.</summary>
public sealed record GrowthCurve
{
    public required double Base { get; init; }
    public required double Growth { get; init; }

    public double ValueAt(int level) => Base * Math.Pow(Growth, level - 1);
}

public sealed record CostCurve
{
    public required Resources Base { get; init; }
    public required double Growth { get; init; }
}

public sealed record ProductionDefinition
{
    public required ResourceType Resource { get; init; }
    public required double Base { get; init; }
    public required double Growth { get; init; }
}

public sealed record CapacityDefinition
{
    public required double AtLevelZero { get; init; }
    public required double Base { get; init; }
    public required double Growth { get; init; }

    public double ValueAt(int level) =>
        level <= 0 ? AtLevelZero : Math.Round(Base * Math.Pow(Growth, level - 1), MidpointRounding.AwayFromZero);
}

/// <summary>data/units.json</summary>
public sealed record UnitsConfig
{
    /// <summary>Each Barracks level above 1 trains this much faster.</summary>
    public required double TrainingSpeedBonusPerLevel { get; init; }

    public required IReadOnlyDictionary<UnitType, UnitDefinition> Units { get; init; }
}

public sealed record UnitDefinition
{
    public required UnitClass Class { get; init; }
    public required double Attack { get; init; }
    public required double DefenceVsInfantry { get; init; }
    public required double DefenceVsCavalry { get; init; }

    /// <summary>Tiles per second at game speed 1.</summary>
    public required double Speed { get; init; }

    public required double LootCapacity { get; init; }
    public required Resources Cost { get; init; }
    public required double TrainingSeconds { get; init; }
    public required int RequiredBarracksLevel { get; init; }
}

/// <summary>data/combat.json</summary>
public sealed record CombatConfig
{
    public required double WallBonusPerLevel { get; init; }
    public required double CasualtyExponent { get; init; }

    /// <summary>Share of warehouse capacity, per resource, that cannot be looted.</summary>
    public required double ProtectedResourceShare { get; init; }

    public required int CatapultsPerLevelOfDamage { get; init; }

    /// <summary>Scouts succeed if the garrison is no larger than this many troops for each scout.</summary>
    public required int TroopsPerScout { get; init; }
}

/// <summary>data/map.json</summary>
public sealed record MapConfig
{
    public required IReadOnlyDictionary<MapSize, int> BarbarianCounts { get; init; }
    public required int MinVillageSpacing { get; init; }

    /// <summary>A new village must be at least this many tiles from every existing village.</summary>
    public required double MinFoundingDistance { get; init; }

    /// <summary>How far, in tiles, a player's start may shift from its quadrant position.</summary>
    public required int StartJitter { get; init; }

    /// <summary>Tiles this close to a player's start are forced to land.</summary>
    public required int StartClearRadius { get; init; }

    public required TerrainConfig Terrain { get; init; }
    public required BarbarianConfig Barbarians { get; init; }
    public required IReadOnlyList<string> PlayerVillageNames { get; init; }
    public required IReadOnlyList<string> FoundedVillageNames { get; init; }
    public required IReadOnlyList<string> BarbarianVillageNames { get; init; }
}

/// <summary>Thresholds on noise values from 0 to 1.</summary>
public sealed record TerrainConfig
{
    public required int NoiseCellSize { get; init; }
    public required double WaterBelow { get; init; }
    public required double HillsAbove { get; init; }
    public required double ForestAbove { get; init; }
}

public sealed record BarbarianConfig
{
    /// <summary>Per resource, at game speed 1.</summary>
    public required double ResourceRegenPerSecond { get; init; }

    /// <summary>Seconds per troop, at game speed 1.</summary>
    public required double TroopRegenSeconds { get; init; }

    public required IReadOnlyList<BarbarianTierConfig> Tiers { get; init; }
}

public sealed record BarbarianTierConfig
{
    public required string Name { get; init; }
    public required int Weight { get; init; }
    public required IReadOnlyDictionary<UnitType, int> Garrison { get; init; }
    public required Resources Resources { get; init; }
    public required IReadOnlyDictionary<BuildingType, int> Buildings { get; init; }
}

/// <summary>data/villages.json</summary>
public sealed record VillagesConfig
{
    public required LoyaltyConfig Loyalty { get; init; }
    public required CapitalConfig Capital { get; init; }
    public required CarriersConfig Carriers { get; init; }
}

public sealed record CarriersConfig
{
    /// <summary>Resources one carrier can hold, across all types.</summary>
    public required int Capacity { get; init; }

    /// <summary>Tiles per second at game speed 1.</summary>
    public required double Speed { get; init; }
}

public sealed record LoyaltyConfig
{
    public required int DropMin { get; init; }
    public required int DropMax { get; init; }
    public required double AfterCapture { get; init; }

    /// <summary>At game speed 1, before the Main Building's share.</summary>
    public required double RegenPerSecond { get; init; }

    public required double RegenPerSecondPerMainLevel { get; init; }
}

public sealed record CapitalConfig
{
    /// <summary>A capital's loyalty cannot fall while its Main Building is above this level.</summary>
    public required int LockedAboveMainLevel { get; init; }

    /// <summary>Real seconds to pick a new capital. Game speed does not scale it.</summary>
    public required double ChoiceSeconds { get; init; }

    public required int PenaltyMainLevel { get; init; }
}

/// <summary>data/scoring.json</summary>
public sealed record ScoringConfig
{
    public required double VillageWeight { get; init; }
    public required double PopulationWeight { get; init; }
    public required double CaptureWeight { get; init; }
    public required IReadOnlyDictionary<BuildingType, int> PopulationPerLevel { get; init; }
}

/// <summary>data/ai.json</summary>
public sealed record AiConfig
{
    /// <summary>Names for AI opponents, in seat order.</summary>
    public required IReadOnlyList<string> Names { get; init; }

    /// <summary>Stock above this share of capacity makes the Warehouse the next build.</summary>
    public required double WarehouseFullShare { get; init; }

    /// <summary>The largest batch trained at once, so one unit type doesn't take everything.</summary>
    public required int TrainingBatchMax { get; init; }

    public required double CavalryPerSwordsman { get; init; }

    /// <summary>Share of a safe village's garrison sent to help another under attack.</summary>
    public required double ReinforceShare { get; init; }

    /// <summary>A raid isn't worth sending for less expected loot than this.</summary>
    public required double RaidMinLoot { get; init; }

    /// <summary>How many Lords the AI keeps in training or alive once it has capture targets.</summary>
    /// <summary>When choosing a new capital: tiles from the nearest enemy that one Main Building level, lost to the penalty, is worth.</summary>
    public required double CapitalMainLevelWeight { get; init; }

    /// <summary>The capital's Marketplace level, once there is a second village to supply.</summary>
    public required int MarketplaceLevel { get; init; }

    /// <summary>A village only gives away what it holds above this share of its capacity.</summary>
    public required double DonorKeepShare { get; init; }

    public required int LordsWanted { get; init; }

    /// <summary>A Lord never leaves with less attack than this, however weak the target seems. Casualties round against small groups.</summary>
    public required double LordMinAttack { get; init; }

    /// <summary>How many Catapults it wants once a report shows a protected capital.</summary>
    public required int CatapultsWanted { get; init; }

    /// <summary>With this many fighters, a profile that saves stops training them to afford a Lord or Settler.</summary>
    public required int FightersBeforeSaving { get; init; }

    public required IReadOnlyList<AiBuildStep> BuildOrder { get; init; }
    public required AiBeliefs Beliefs { get; init; }
    /// <summary>Game seconds it waits after scouts are caught before trying again, with twice as many.</summary>
    public required double ScoutRetrySeconds { get; init; }

    public required IReadOnlyDictionary<AiDifficulty, AiProfile> Difficulties { get; init; }
}

public sealed record AiBuildStep
{
    public required BuildingType Building { get; init; }
    public required int Level { get; init; }
}

/// <summary>What the AI assumes before its reports say otherwise. Separate from the real garrisons in map.json, so it can be wrong.</summary>
public sealed record AiBeliefs
{
    public required IReadOnlyDictionary<string, double> BarbarianPowerByTier { get; init; }
    public required IReadOnlyDictionary<string, double> BarbarianLootByTier { get; init; }
    public required double PlayerPowerAtStart { get; init; }
    public required double PlayerPowerPerMinute { get; init; }

    /// <summary>Defence one troop is assumed to bring, to turn power into a head count and back when scouting.</summary>
    public required double PowerPerTroop { get; init; }

    /// <summary>How fast a sighted barbarian garrison is assumed to come back, as a share per minute. Players grow by PlayerPowerPerMinute.</summary>
    public required double GrowthAfterSightingPerMinute { get; init; }

    public required double BarbarianLootRecoveryPerMinute { get; init; }
}

/// <summary>One difficulty level. The rules are the same for all: only these habits differ.</summary>
public sealed record AiProfile
{
    /// <summary>Seconds at game speed 1 between decisions.</summary>
    public required double DecisionSeconds { get; init; }

    /// <summary>The army it wants by now, in fighters per minute of game time. Behind this, troops come before buildings.</summary>
    public required double FightersPerMinute { get; init; }

    public required double RaidMargin { get; init; }
    public required double AttackMargin { get; init; }
    public required double LordMargin { get; init; }
    public required double AggressionDelaySeconds { get; init; }

    /// <summary>Share of each village's troops kept home as its garrison.</summary>
    public required double GarrisonShare { get; init; }

    public required int MaxRaidsPerVillage { get; init; }
    public required double RaidCooldownSeconds { get; init; }
    public required int WantedVillages { get; init; }
    public required double ExpansionDelaySeconds { get; init; }
    public required bool Reinforces { get; init; }
    public required bool SavesForGoals { get; init; }

    /// <summary>Stops training fighters above this many per minute of game time, even under attack. Absent means no limit.</summary>
    public double? MaxFightersPerMinute { get; init; }

    /// <summary>The highest Wall it builds, in the build order or under attack.</summary>
    public required int MaxWallLevel { get; init; }

    /// <summary>Game seconds that news of a target stays good enough to attack on. Older than this, it scouts first.</summary>
    public required double ScoutMaxAgeSeconds { get; init; }
}
