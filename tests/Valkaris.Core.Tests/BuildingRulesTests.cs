using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class BuildingRulesTests
{
    private static readonly GameData Data = TestData.Shipped;

    public static IEnumerable<object[]> AllBuildings() =>
        Enum.GetValues<BuildingType>().Select(t => new object[] { t });

    [Fact]
    public void CostAtLevelOneIsTheBaseCost() =>
        Assert.Equal(new Resources(40, 60, 10), BuildingRules.UpgradeCost(Data, BuildingType.Woodcutter, 1));

    [Fact]
    public void CostFollowsTheGrowthCurveAndIsRounded() =>
        // Base (40, 60, 10) * 1.5^2 = (90, 135, 22.5)
        Assert.Equal(new Resources(90, 135, 23), BuildingRules.UpgradeCost(Data, BuildingType.Woodcutter, 3));

    [Fact]
    public void BuildTimeFollowsTheGrowthCurveAndIsRounded()
    {
        Assert.Equal(30, BuildingRules.BuildTimeSeconds(Data, BuildingType.Woodcutter, 1));
        Assert.Equal(42, BuildingRules.BuildTimeSeconds(Data, BuildingType.Woodcutter, 2));
        Assert.Equal(59, BuildingRules.BuildTimeSeconds(Data, BuildingType.Woodcutter, 3));
    }

    [Theory]
    [MemberData(nameof(AllBuildings))]
    public void CostAndBuildTimeStrictlyIncreaseWithLevel(BuildingType type)
    {
        for (var level = 2; level <= Data.Buildings.Buildings[type].MaxLevel; level++)
        {
            var previous = BuildingRules.UpgradeCost(Data, type, level - 1);
            var current = BuildingRules.UpgradeCost(Data, type, level);
            Assert.True(current.Wood > previous.Wood, $"{type} wood at level {level}");
            Assert.True(current.Stone > previous.Stone, $"{type} stone at level {level}");
            Assert.True(current.Food > previous.Food, $"{type} food at level {level}");

            Assert.True(
                BuildingRules.BuildTimeSeconds(Data, type, level) > BuildingRules.BuildTimeSeconds(Data, type, level - 1),
                $"{type} build time at level {level}");
        }
    }

    [Theory]
    [MemberData(nameof(AllBuildings))]
    public void LevelsBelowOneCannotBeBuilt(BuildingType type)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildingRules.UpgradeCost(Data, type, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildingRules.BuildTimeSeconds(Data, type, 0));
    }

    [Theory]
    [InlineData(BuildingType.Woodcutter, ResourceType.Wood)]
    [InlineData(BuildingType.Quarry, ResourceType.Stone)]
    [InlineData(BuildingType.Farm, ResourceType.Food)]
    public void ProducersOutputTheirResource(BuildingType type, ResourceType resource)
    {
        Assert.Equal(Resources.Zero, BuildingRules.Production(Data, type, 0));
        Assert.Equal(Resources.Of(resource, 1.0), BuildingRules.Production(Data, type, 1));
        Assert.Equal(1.4, BuildingRules.Production(Data, type, 2).Get(resource), precision: 10);
    }

    [Fact]
    public void NonProducersOutputNothing() =>
        Assert.Equal(Resources.Zero, BuildingRules.Production(Data, BuildingType.Barracks, 5));

    [Fact]
    public void WarehouseCapacityUsesTheLevelZeroValueAndTheCurve()
    {
        Assert.Equal(500, BuildingRules.WarehouseCapacity(Data, 0));
        Assert.Equal(1000, BuildingRules.WarehouseCapacity(Data, 1));
        Assert.Equal(1400, BuildingRules.WarehouseCapacity(Data, 2));
    }

    [Fact]
    public void MainBuildingLimitReadsTheTable()
    {
        Assert.Equal(1, BuildingRules.MaxOtherBuildingLevel(Data, 0));
        Assert.Equal(3, BuildingRules.MaxOtherBuildingLevel(Data, 1));
        Assert.Equal(10, BuildingRules.MaxOtherBuildingLevel(Data, 10));
    }

    [Fact]
    public void ResultsComeFromData()
    {
        var altered = TestData.Mutated(GameData.BuildingsFile, root =>
        {
            var woodcutter = root["buildings"]!["Woodcutter"]!;
            woodcutter["cost"]!["base"]!["wood"] = 1000;
            woodcutter["buildTime"]!["base"] = 7;
            woodcutter["production"]!["base"] = 3.5;
            root["buildings"]!["Warehouse"]!["capacity"]!["base"] = 1234;
            root["maxOtherBuildingLevelByMainLevel"]![1] = 2;
        });

        Assert.Equal(1000, BuildingRules.UpgradeCost(altered, BuildingType.Woodcutter, 1).Wood);
        Assert.Equal(7, BuildingRules.BuildTimeSeconds(altered, BuildingType.Woodcutter, 1));
        Assert.Equal(3.5, BuildingRules.Production(altered, BuildingType.Woodcutter, 1).Wood);
        Assert.Equal(1234, BuildingRules.WarehouseCapacity(altered, 1));
        Assert.Equal(2, BuildingRules.MaxOtherBuildingLevel(altered, 1));
    }

    [Fact]
    public void EveryWarehouseUpgradeFitsThePreviousLevelsCapacity()
    {
        // Otherwise a player could never afford the next Warehouse level.
        var warehouse = Data.Buildings.Buildings[BuildingType.Warehouse];
        for (var level = 1; level <= warehouse.MaxLevel; level++)
        {
            var cost = BuildingRules.UpgradeCost(Data, BuildingType.Warehouse, level);
            var cap = BuildingRules.WarehouseCapacity(Data, level - 1);
            Assert.True(cost.Wood <= cap && cost.Stone <= cap && cost.Food <= cap, $"Warehouse level {level}");
        }
    }

    [Theory]
    [MemberData(nameof(AllBuildings))]
    public void MaxLevelUpgradesFitTheMaxLevelWarehouse(BuildingType type)
    {
        var definition = Data.Buildings.Buildings[type];
        var cost = BuildingRules.UpgradeCost(Data, type, definition.MaxLevel);
        var cap = BuildingRules.WarehouseCapacity(Data, Data.Buildings.Buildings[BuildingType.Warehouse].MaxLevel);

        Assert.True(cost.Wood <= cap && cost.Stone <= cap && cost.Food <= cap);
    }
}
