using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;
using Xunit;

namespace Valkaris.Core.Tests;

/// <summary>The shipped sizes and layouts across many seeds.</summary>
public class RectangularMapTests
{
    private const int Seeds = 200;

    private static readonly GameData Data = TestData.Shipped;

    public static TheoryData<MapSize, int> Matches()
    {
        var data = new TheoryData<MapSize, int>();
        foreach (var size in Enum.GetValues<MapSize>())
        {
            for (var players = Data.Game.MinPlayers; players <= Data.Game.MaxPlayers; players++)
                data.Add(size, players);
        }
        return data;
    }

    private static GameState Create(MapSize size, int players, ulong seed) =>
        GameFactory.CreateMatch(new GameSettings(3600, 1, size, seed), TestMatch.Slots(players), Data);

    private static IEnumerable<Village> Starts(GameState state) => state.Players.Keys.Select(id => state.Villages[id]);

    [Fact]
    public void MapsAreWiderThanTheyAreTall() =>
        Assert.All(Data.Game.MapSizes.Values, size => Assert.InRange((double)size.Width / size.Height, 1.6, 1.9));

    [Theory]
    [MemberData(nameof(Matches))]
    public void StartsAreOnLandInsideTheMap(MapSize size, int players)
    {
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            var state = Create(size, players, seed);
            Assert.All(Starts(state), village =>
            {
                Assert.True(state.Map.Contains(village.Position));
                Assert.NotEqual(Terrain.Water, state.Map.TileAt(village.Position).Terrain);
            });
        }
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void EverySeatIsAboutAsFarFromItsRivals(MapSize size, int players)
    {
        // With jitter on both ends, a seat's mean distance to its rivals moves by a little either way.
        const double allowed = 3.5;
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            var starts = Starts(Create(size, players, seed)).ToList();
            var means = starts.Select(a => starts.Where(b => b != a).Average(b => UnitRules.Distance(a.Position, b.Position))).ToList();
            Assert.InRange(means.Max() - means.Min(), 0, allowed);
        }
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void RivalsStartApart(MapSize size, int players)
    {
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            var starts = Starts(Create(size, players, seed)).ToList();
            var closest = starts.SelectMany(a => starts.Where(b => b != a).Select(b => UnitRules.Distance(a.Position, b.Position))).Min();
            Assert.True(closest >= 5, $"Seed {seed}: rivals start {closest} tiles apart.");
        }
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void BarbariansKeepTheirCountAndAreOnLand(MapSize size, int players)
    {
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            var state = Create(size, players, seed);
            var barbarians = state.Villages.Values.Where(v => v.IsBarbarian).ToList();
            Assert.Equal(Data.Map.BarbarianCounts[size], barbarians.Count);
            Assert.All(barbarians, village => Assert.NotEqual(Terrain.Water, state.Map.TileAt(village.Position).Terrain));
        }
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void BarbariansKeepTheirSpacing(MapSize size, int players)
    {
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            var villages = Create(size, players, seed).Villages.Values.ToList();
            var closest = villages.SelectMany(a => villages.Where(b => b != a).Select(b => UnitRules.Distance(a.Position, b.Position))).Min();
            Assert.True(closest >= Data.Map.MinVillageSpacing, $"Seed {seed}: villages {closest} tiles apart.");
        }
    }

    [Fact]
    public void ALayoutMustFitThePlayers()
    {
        var problem = Assert.Throws<GameDataException>(() => GameData.Load(new MutatedDataSource(GameData.MapFile, root => root["startLayouts"]!["3"]!.AsArray().RemoveAt(0))));
        Assert.Contains("startLayouts.3", problem.Message);
    }
}
