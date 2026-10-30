using Valkaris.Core.Model;

namespace Valkaris.Core.Data;

/// <summary>data/game.json</summary>
public sealed record GameConfig
{
    public required Resources StartingResources { get; init; }
    public required IReadOnlyDictionary<BuildingType, int> StartingBuildings { get; init; }
    public required int MaxBuildQueueLength { get; init; }
    public required int MinPlayers { get; init; }
    public required int MaxPlayers { get; init; }
    public required IReadOnlyList<double> MatchDurationsMinutes { get; init; }
    public required IReadOnlyList<double> SpeedMultipliers { get; init; }
    public required IReadOnlyDictionary<MapSize, MapSizeDefinition> MapSizes { get; init; }
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
