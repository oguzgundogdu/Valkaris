using System.Text.Json.Nodes;
using Valkaris.Core.Data;

namespace Valkaris.Core.Tests;

public class GameDataValidationTests
{
    private static void AssertInvalid(string file, Action<JsonNode> mutate, string expectedField)
    {
        var ex = Assert.Throws<GameDataException>(() => TestData.Mutated(file, mutate));

        Assert.Contains(file, ex.Message);
        Assert.Contains(expectedField, ex.Message);
    }

    private static void AssertBuildingsInvalid(Action<JsonNode> mutate, string expectedField) =>
        AssertInvalid(GameData.BuildingsFile, mutate, expectedField);

    private static void AssertGameInvalid(Action<JsonNode> mutate, string expectedField) =>
        AssertInvalid(GameData.GameFile, mutate, expectedField);

    [Fact]
    public void ShippedDataIsValid() => Assert.NotNull(TestData.Shipped);

    [Fact]
    public void MissingBuilding() =>
        AssertBuildingsInvalid(r => r["buildings"]!.AsObject().Remove("Wall"), "buildings.Wall");

    [Fact]
    public void MaxLevelBelowOne() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Wall"]!["maxLevel"] = 0, "buildings.Wall.maxLevel");

    [Fact]
    public void NegativeCost() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Wall"]!["cost"]!["base"]!["wood"] = -1, "buildings.Wall.cost.base");

    [Fact]
    public void ZeroCost() =>
        AssertBuildingsInvalid(
            r => r["buildings"]!["Wall"]!["cost"]!["base"] = new JsonObject { ["wood"] = 0, ["stone"] = 0, ["food"] = 0 },
            "buildings.Wall.cost.base");

    [Fact]
    public void CostGrowthNotIncreasing() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Wall"]!["cost"]!["growth"] = 1.0, "buildings.Wall.cost.growth");

    [Fact]
    public void NonPositiveBuildTime() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Wall"]!["buildTime"]!["base"] = 0, "buildings.Wall.buildTime.base");

    [Fact]
    public void BuildTimeGrowthNotIncreasing() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Wall"]!["buildTime"]!["growth"] = 0.9, "buildings.Wall.buildTime.growth");

    [Fact]
    public void NonPositiveProduction() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Farm"]!["production"]!["base"] = 0, "buildings.Farm.production.base");

    [Fact]
    public void ResourceWithoutProducer() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Farm"]!["production"]!["resource"] = "Wood", "exactly one producer of Food");

    [Fact]
    public void WarehouseWithoutCapacity() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Warehouse"]!.AsObject().Remove("capacity"), "buildings.Warehouse.capacity");

    [Fact]
    public void NonPositiveCapacityAtLevelZero() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Warehouse"]!["capacity"]!["atLevelZero"] = 0, "buildings.Warehouse.capacity.atLevelZero");

    [Fact]
    public void MainLevelTableWrongLength() =>
        AssertBuildingsInvalid(r => r["maxOtherBuildingLevelByMainLevel"]!.AsArray().RemoveAt(0), "maxOtherBuildingLevelByMainLevel");

    [Fact]
    public void NegativeStartingResources() =>
        AssertGameInvalid(r => r["startingResources"]!["wood"] = -1, "startingResources");

    [Fact]
    public void StartingResourcesAboveCapacity() =>
        AssertGameInvalid(r => r["startingResources"]!["stone"] = 1_000_000, "startingResources");

    [Fact]
    public void MissingStartingBuilding() =>
        AssertGameInvalid(r => r["startingBuildings"]!.AsObject().Remove("Wall"), "startingBuildings.Wall");

    [Fact]
    public void StartingBuildingAboveMaxLevel() =>
        AssertGameInvalid(r => r["startingBuildings"]!["Wall"] = 99, "startingBuildings.Wall");

    [Fact]
    public void QueueLengthBelowOne() =>
        AssertGameInvalid(r => r["maxBuildQueueLength"] = 0, "maxBuildQueueLength");

    [Fact]
    public void MaxPlayersBelowMinPlayers() =>
        AssertGameInvalid(r => r["maxPlayers"] = 1, "maxPlayers");

    [Fact]
    public void EmptyMatchDurations() =>
        AssertGameInvalid(r => r["matchDurationsMinutes"] = new JsonArray(), "matchDurationsMinutes");

    [Fact]
    public void NonPositiveSpeedMultiplier() =>
        AssertGameInvalid(r => r["speedMultipliers"]![0] = 0, "speedMultipliers");

    [Fact]
    public void MissingMapSize() =>
        AssertGameInvalid(r => r["mapSizes"]!.AsObject().Remove("Medium"), "mapSizes.Medium");

    [Fact]
    public void AllProblemsAreReportedTogether()
    {
        var ex = Assert.Throws<GameDataException>(() => TestData.Mutated(GameData.BuildingsFile, r =>
        {
            r["buildings"]!["Wall"]!["maxLevel"] = 0;
            r["buildings"]!["Farm"]!["buildTime"]!["base"] = 0;
        }));

        Assert.Contains("buildings.Wall.maxLevel", ex.Message);
        Assert.Contains("buildings.Farm.buildTime.base", ex.Message);
    }
}
