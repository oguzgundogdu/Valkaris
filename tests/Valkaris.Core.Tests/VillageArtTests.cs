using System.Text.RegularExpressions;

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
}
