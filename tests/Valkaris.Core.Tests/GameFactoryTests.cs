using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class GameFactoryTests
{
    private static readonly GameData Data = TestData.Shipped;

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EachPlayerGetsOneVillageAsTheirCapital(int players)
    {
        var state = TestMatch.Create(players);

        Assert.Equal(players, state.Players.Count);
        Assert.Equal(players, state.Villages.Count);
        foreach (var player in state.Players.Values)
        {
            var capital = state.Villages[player.CapitalVillageId];
            Assert.Equal(player.Id, capital.OwnerId);
            Assert.False(player.IsEliminated);
        }
    }

    [Fact]
    public void VillagesStartWithDataDefinedValues()
    {
        var village = TestMatch.Create().Villages[1];

        Assert.Equal(Data.Game.StartingResources, village.Resources);
        foreach (var (type, level) in Data.Game.StartingBuildings)
            Assert.Equal(level, village.BuildingLevel(type));
        Assert.Empty(village.BuildQueue);
    }

    [Fact]
    public void StartingValuesComeFromData()
    {
        var altered = TestData.Mutated(GameData.GameFile, root =>
        {
            root["startingResources"]!["wood"] = 42;
            root["startingBuildings"]!["Barracks"] = 2;
        });

        var village = TestMatch.Create(data: altered).Villages[1];

        Assert.Equal(42, village.Resources.Wood);
        Assert.Equal(2, village.BuildingLevel(BuildingType.Barracks));
    }

    [Fact]
    public void VillagesAreOnDistinctTilesThatPointBackToThem()
    {
        var state = TestMatch.Create(players: 4);

        Assert.Equal(4, state.Villages.Values.Select(v => v.Position).Distinct().Count());
        foreach (var village in state.Villages.Values)
            Assert.Equal(village.Id, state.Map.TileAt(village.Position).VillageId);
    }

    [Fact]
    public void MapSizeComesFromData()
    {
        var state = TestMatch.Create();

        Assert.Equal(Data.Game.MapSizes[MapSize.Small].Width, state.Map.Width);
        Assert.Equal(Data.Game.MapSizes[MapSize.Small].Height, state.Map.Height);
        Assert.Equal(state.Map.Width * state.Map.Height, state.Map.Tiles.Count);
    }

    [Fact]
    public void PlayerSlotDetailsAreKept()
    {
        var slots = new[]
        {
            new PlayerSlot("Ada", Nation.Karuk, IsAi: false),
            new PlayerSlot("Bot", Nation.Solari, IsAi: true),
        };

        var state = GameFactory.CreateMatch(new GameSettings(TestMatch.OneHour, 1, MapSize.Small), slots, Data);

        Assert.Equal("Ada", state.Players[1].Name);
        Assert.Equal(Nation.Karuk, state.Players[1].Nation);
        Assert.False(state.Players[1].IsAi);
        Assert.Equal(Nation.Solari, state.Players[2].Nation);
        Assert.True(state.Players[2].IsAi);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void PlayerCountOutsideDataLimitsIsRejected(int players) =>
        Assert.Throws<ArgumentException>(() => TestMatch.Create(players));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(TestMatch.OneHour, 0)]
    [InlineData(TestMatch.OneHour, -1)]
    public void NonPositiveDurationOrSpeedIsRejected(double duration, double speed) =>
        Assert.Throws<ArgumentException>(() => TestMatch.Create(duration: duration, speed: speed));

    [Fact]
    public void MapTooSmallForEveryPlayerIsRejected()
    {
        var tiny = TestData.Mutated(GameData.GameFile, root =>
        {
            root["mapSizes"]!["Small"]!["width"] = 1;
            root["mapSizes"]!["Small"]!["height"] = 1;
        });

        Assert.Throws<ArgumentException>(() => TestMatch.Create(data: tiny));
    }
}
