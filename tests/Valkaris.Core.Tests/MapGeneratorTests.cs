using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class MapGeneratorTests
{
    private const int SeedCount = 200;

    private static readonly GameData Data = TestData.Shipped;

    private static GameState Match(ulong seed, MapSize size, int players = 4, GameData? data = null) =>
        GameFactory.CreateMatch(new GameSettings(TestMatch.OneHour, 1, size, seed), TestMatch.Slots(players), data ?? Data);

    private static string Json(GameState state) => SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(state));

    public static IEnumerable<object[]> Sizes() => Enum.GetValues<MapSize>().Select(s => new object[] { s });

    private static void ForEachSeed(MapSize size, Action<GameState> check)
    {
        for (ulong seed = 0; seed < SeedCount; seed++)
            check(Match(seed, size));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void SameSeedGivesTheSameMap(MapSize size) =>
        Assert.Equal(Json(Match(42, size)), Json(Match(42, size)));

    [Theory]
    [MemberData(nameof(Sizes))]
    public void DifferentSeedsGiveDifferentMaps(MapSize size) =>
        Assert.NotEqual(Json(Match(1, size)), Json(Match(2, size)));

    [Theory]
    [MemberData(nameof(Sizes))]
    public void BarbarianCountIsAlwaysMet(MapSize size) => ForEachSeed(size, state =>
        Assert.Equal(Data.Map.BarbarianCounts[size], state.Villages.Values.Count(v => v.IsBarbarian)));

    [Theory]
    [MemberData(nameof(Sizes))]
    public void NoVillageIsOnWater(MapSize size) => ForEachSeed(size, state =>
    {
        foreach (var village in state.Villages.Values)
            Assert.NotEqual(Terrain.Water, state.Map.TileAt(village.Position).Terrain);
    });

    [Theory]
    [MemberData(nameof(Sizes))]
    public void TilesAndVillagesPointAtEachOther(MapSize size) => ForEachSeed(size, state =>
    {
        foreach (var village in state.Villages.Values)
            Assert.Equal(village.Id, state.Map.TileAt(village.Position).VillageId);
        Assert.Equal(state.Villages.Count, state.Map.Tiles.Count(t => t.VillageId is not null));
    });

    [Theory]
    [MemberData(nameof(Sizes))]
    public void VillagesKeepTheirSpacing(MapSize size) => ForEachSeed(size, state =>
    {
        var villages = state.Villages.Values.ToList();
        for (var i = 0; i < villages.Count; i++)
        {
            for (var j = i + 1; j < villages.Count; j++)
            {
                Assert.True(
                    UnitRules.Distance(villages[i].Position, villages[j].Position) >= Data.Map.MinVillageSpacing,
                    $"{villages[i].Name} and {villages[j].Name}");
            }
        }
    });

    [Theory]
    [MemberData(nameof(Sizes))]
    public void PlayerDistancesStayFair(MapSize size)
    {
        // Each start shifts at most this far from its fixed quadrant position.
        var jitter = Data.Map.StartJitter * Math.Sqrt(2);

        ForEachSeed(size, state =>
        {
            var map = state.Map;
            var first = state.Villages[1].Position;
            var second = state.Villages[2].Position;
            var nominal = UnitRules.Distance(
                new TileCoord(map.Width / 4, map.Height / 4),
                new TileCoord(map.Width * 3 / 4, map.Height * 3 / 4));

            Assert.InRange(UnitRules.Distance(first, second), nominal - 2 * jitter - 1e-9, nominal + 2 * jitter + 1e-9);
        });
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void TilesAroundPlayerStartsAreLand(MapSize size) => ForEachSeed(size, state =>
    {
        var radius = Data.Map.StartClearRadius;
        foreach (var village in state.Villages.Values.Where(v => !v.IsBarbarian))
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var coord = new TileCoord(village.Position.X + dx, village.Position.Y + dy);
                    if (state.Map.Contains(coord))
                        Assert.NotEqual(Terrain.Water, state.Map.TileAt(coord).Terrain);
                }
            }
        }
    });

    [Theory]
    [MemberData(nameof(Sizes))]
    public void EveryTerrainAppearsAcrossSeeds(MapSize size)
    {
        var seen = new HashSet<Terrain>();
        ForEachSeed(size, state => seen.UnionWith(state.Map.Tiles.Select(t => t.Terrain)));

        Assert.Equal(Enum.GetValues<Terrain>().Length, seen.Count);
    }

    [Fact]
    public void LandIsTheMajorityOnAverage()
    {
        double water = 0;
        double total = 0;
        ForEachSeed(MapSize.Medium, state =>
        {
            water += state.Map.Tiles.Count(t => t.Terrain == Terrain.Water);
            total += state.Map.Tiles.Count;
        });

        Assert.InRange(water / total, 0.02, 0.4);
    }

    [Fact]
    public void BarbariansMatchTheirTier()
    {
        foreach (var village in Match(3, MapSize.Medium).Villages.Values.Where(v => v.IsBarbarian))
        {
            var tier = Data.Map.Barbarians.Tiers[village.BarbarianTier!.Value];
            Assert.Equal(tier.Resources, village.Resources);
            foreach (var (unit, count) in tier.Garrison)
                Assert.Equal(count, village.TroopCount(unit));
            foreach (var (building, level) in tier.Buildings)
                Assert.Equal(level, village.BuildingLevel(building));
        }
    }

    [Fact]
    public void BothTiersAppearAcrossSeeds()
    {
        var tiers = new HashSet<int>();
        ForEachSeed(MapSize.Small, state =>
            tiers.UnionWith(state.Villages.Values.Where(v => v.IsBarbarian).Select(v => v.BarbarianTier!.Value)));

        Assert.Equal(Data.Map.Barbarians.Tiers.Count, tiers.Count);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void VillageNamesAreUnique(MapSize size) => ForEachSeed(size, state =>
    {
        Assert.All(state.Villages.Values, v => Assert.False(string.IsNullOrEmpty(v.Name)));
        Assert.Equal(state.Villages.Count, state.Villages.Values.Select(v => v.Name).Distinct().Count());
    });

    [Fact]
    public void BarbarianCountComesFromData()
    {
        var data = TestData.Mutated(GameData.MapFile, root => root["barbarianCounts"]!["Small"] = 2);

        Assert.Equal(2, Match(1, MapSize.Small, data: data).Villages.Values.Count(v => v.IsBarbarian));
    }

    [Fact]
    public void CrowdedMapsRelaxSpacingToFitEveryone()
    {
        var data = TestData.Mutated(GameData.MapFile, root => root["minVillageSpacing"] = 12);

        var state = Match(1, MapSize.Small, data: data);

        Assert.Equal(Data.Map.BarbarianCounts[MapSize.Small], state.Villages.Values.Count(v => v.IsBarbarian));
    }

    [Fact]
    public void MapWithTooLittleLandIsRejected()
    {
        var data = TestData.Mutated(GameData.GameFile, root =>
        {
            root["mapSizes"]!["Small"]!["width"] = 3;
            root["mapSizes"]!["Small"]!["height"] = 3;
        });

        Assert.Throws<ArgumentException>(() => Match(1, MapSize.Small, data: data));
    }

    [Fact]
    public void GeneratedMapRoundTrips() => SnapshotAssert.RoundTrips(Match(9, MapSize.Medium));
}
