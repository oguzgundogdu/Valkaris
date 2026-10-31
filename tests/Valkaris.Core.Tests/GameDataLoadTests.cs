using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Tests;

public class GameDataLoadTests
{
    [Fact]
    public void LoadsGameConfig()
    {
        var game = TestData.Shipped.Game;

        Assert.Equal(new Resources(300, 300, 200), game.StartingResources);
        Assert.Equal(1, game.StartingBuildings[BuildingType.MainBuilding]);
        Assert.Equal(0, game.StartingBuildings[BuildingType.Barracks]);
        Assert.Equal(2, game.MaxBuildQueueLength);
        Assert.Equal(new double[] { 30, 45, 60 }, game.MatchDurationsMinutes);
        Assert.Equal(16, game.MapSizes[MapSize.Small].Width);
    }

    [Fact]
    public void LoadsBuildingDefinitions()
    {
        var buildings = TestData.Shipped.Buildings.Buildings;

        Assert.Equal(Enum.GetValues<BuildingType>().Length, buildings.Count);
        Assert.Equal(10, buildings[BuildingType.Woodcutter].MaxLevel);
        Assert.Equal(40, buildings[BuildingType.Woodcutter].Cost.Base.Wood);
        Assert.Equal(ResourceType.Wood, buildings[BuildingType.Woodcutter].Production!.Resource);
        Assert.Equal(1000, buildings[BuildingType.Warehouse].Capacity!.Base);
        Assert.Null(buildings[BuildingType.Barracks].Production);
    }

    [Fact]
    public void UnknownFieldIsRejectedNamingTheFile()
    {
        var ex = Assert.Throws<GameDataException>(() =>
            TestData.Mutated(GameData.GameFile, root => root["maxBuildQueueLenght"] = 2));

        Assert.Contains(GameData.GameFile, ex.Message);
        Assert.Contains("maxBuildQueueLenght", ex.Message);
    }

    [Fact]
    public void MissingRequiredFieldIsRejectedNamingTheFile()
    {
        var ex = Assert.Throws<GameDataException>(() =>
            TestData.Mutated(GameData.GameFile, root => root.AsObject().Remove("maxPlayers")));

        Assert.Contains(GameData.GameFile, ex.Message);
        Assert.Contains("maxPlayers", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownResourceIsRejected()
    {
        var ex = Assert.Throws<GameDataException>(() =>
            TestData.Mutated(GameData.BuildingsFile, root =>
                root["buildings"]!["Farm"]!["production"]!["resource"] = "Gold"));

        Assert.Contains(GameData.BuildingsFile, ex.Message);
    }

    [Fact]
    public void UnknownBuildingIsRejected()
    {
        var ex = Assert.Throws<GameDataException>(() =>
            TestData.Mutated(GameData.GameFile, root => root["startingBuildings"]!["Castle"] = 1));

        Assert.Contains(GameData.GameFile, ex.Message);
    }

    [Fact]
    public void LoadsUnitsAndCombat()
    {
        var spearman = TestData.Shipped.Units.Units[UnitType.Spearman];

        Assert.Equal(UnitClass.Infantry, spearman.Class);
        Assert.Equal(23, spearman.DefenceVsCavalry);
        Assert.Equal(new Resources(30, 20, 60), spearman.Cost);
        Assert.Equal(0.2, TestData.Shipped.Combat.ProtectedResourceShare);
        Assert.Equal(10, TestData.Shipped.Game.StartingTroops[UnitType.Spearman]);
    }

    [Fact]
    public void FoodIsTheLargestCostOfEveryUnit()
    {
        foreach (var (type, unit) in TestData.Shipped.Units.Units)
        {
            Assert.True(unit.Cost.Food > unit.Cost.Wood, $"{type} food vs wood");
            Assert.True(unit.Cost.Food > unit.Cost.Stone, $"{type} food vs stone");
        }
    }
}
