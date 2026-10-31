using System.Text.RegularExpressions;
using Valkaris.Core.Model;
using Valkaris.Presentation;

namespace Valkaris.Core.Tests;

public class VillageArtTests
{
    private static string Village => Path.Combine(Catalogue.Root, "assets", "village");

    [Fact]
    public void EverySpriteInTheRecipesExists()
    {
        var recipes = File.ReadAllText(Path.Combine(Catalogue.Root, "tools", "art", "recipes.gd"));
        var list = Regex.Match(recipes, @"const ALL := \[(.*?)\]", RegexOptions.Singleline).Groups[1].Value;
        var names = Regex.Matches(list, "\"([a-z_0-9]+)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.True(File.Exists(Path.Combine(Village, name + ".png")), name));
    }

    [Theory]
    [InlineData("License-Fantasy-Town-Kit.txt")]
    [InlineData("License-Nature-Kit.txt")]
    public void TheArtCarriesItsLicence(string file)
    {
        Assert.Contains("Creative Commons Zero", File.ReadAllText(Path.Combine(Village, file)));
    }

    [Fact]
    public void TheRenderToolStaysOutOfTheGame()
    {
        Assert.True(File.Exists(Path.Combine(Catalogue.Root, "tools", "art", ".gdignore")));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(7, 2)]
    [InlineData(8, 3)]
    [InlineData(10, 3)]
    public void TenLevelsSplitIntoThreeStages(int level, int stage)
    {
        Assert.Equal(stage, VillageArt.Stage(level, 10));
    }

    [Fact]
    public void EveryBuildingShowsEveryStageAndNeverGoesBack()
    {
        foreach (var (building, definition) in TestData.Shipped.Buildings.Buildings)
        {
            var stages = Enumerable.Range(0, definition.MaxLevel + 1).Select(level => VillageArt.Stage(level, definition.MaxLevel)).ToList();

            Assert.Equal(new[] { 0, 1, 2, 3 }, stages.Distinct());
            Assert.Equal(stages.Order(), stages);
        }
    }

    [Fact]
    public void EveryBuildingHasAPlotAndNoneOverlapAtTheSmallestView()
    {
        const float width = 832, height = 633, tile = 150;
        Assert.All(Enum.GetValues<BuildingType>(), building => Assert.True(VillageArt.Plots.ContainsKey(building)));

        var plots = VillageArt.Plots.Values.ToList();
        for (var a = 0; a < plots.Count; a++)
        {
            for (var b = a + 1; b < plots.Count; b++)
                Assert.True(Math.Abs(plots[a].X - plots[b].X) * width >= tile || Math.Abs(plots[a].Y - plots[b].Y) * height >= tile, $"{a} and {b}");
        }
    }

    [Fact]
    public void EverySpriteTheGameAsksForExists()
    {
        foreach (var building in Enum.GetValues<BuildingType>())
        {
            for (var stage = 0; stage <= VillageArt.StageCount; stage++)
                Assert.True(File.Exists(Path.Combine(Catalogue.Root, VillageArt.Sprite(building, stage).Replace("res://", ""))), $"{building} {stage}");
        }
    }

    [Fact]
    public void TheSameVillageAlwaysGetsTheSameScenery()
    {
        Assert.Equal(VillageArt.Scenery(9, 40, 2), VillageArt.Scenery(9, 40, 2));
        Assert.NotEqual(VillageArt.Scenery(9, 40, 2), VillageArt.Scenery(10, 40, 2));
    }

    [Fact]
    public void GrowingOnlyAddsHousesAndExtras()
    {
        var before = new HashSet<Placed>();
        for (var levels = 0; levels <= 90; levels++)
        {
            var now = VillageArt.Scenery(9, levels, 1).ToHashSet();
            Assert.Superset(before, now);
            before = now;
        }

        Assert.Equal(0, VillageArt.HouseCount(0));
        Assert.Equal(VillageArt.HouseSpots.Count, VillageArt.HouseCount(90));
    }

    [Fact]
    public void FarmStagesAddCrops()
    {
        int Crops(int stage) => VillageArt.Scenery(9, 20, stage).Count(item => item.Sprite.Contains("crops_"));

        Assert.Equal(0, Crops(0));
        Assert.True(Crops(1) < Crops(2) && Crops(2) < Crops(3));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(77)]
    public void SceneryStaysOffPlotsAndPathsAndItsArtExists(int seed)
    {
        foreach (var item in VillageArt.Scenery(seed, 90, 3))
        {
            Assert.True(VillageArt.IsClear(item.X, item.Y), $"{item.Sprite} at {item.X}, {item.Y}");
            Assert.True(File.Exists(Path.Combine(Catalogue.Root, item.Sprite.Replace("res://", ""))), item.Sprite);
        }
    }

    [Fact]
    public void EveryFixedSpotIsClear()
    {
        Assert.All(VillageArt.HouseSpots.Concat(VillageArt.CropSpots), spot => Assert.True(VillageArt.IsClear(spot.X, spot.Y), $"{spot.X}, {spot.Y}"));
    }

    [Fact]
    public void AVillageKeepsItsLookAndDiffersFromOthers()
    {
        Assert.Equal(VillageArt.Seed(5, 1), VillageArt.Seed(5, 1));
        Assert.NotEqual(VillageArt.Seed(5, 1), VillageArt.Seed(5, 2));
    }
}
