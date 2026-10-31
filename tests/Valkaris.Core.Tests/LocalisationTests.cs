using System.Text;
using System.Text.RegularExpressions;
using Valkaris.Core.Commands;
using Valkaris.Core.Model;

namespace Valkaris.Core.Tests;

/// <summary>A PO file: one or more translated forms per key.</summary>
internal sealed class Catalogue
{
    private static readonly Regex Quoted = new("\"(.*)\"$");

    public Dictionary<string, List<string>> Entries { get; } = new();

    public static string Root
    {
        get
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName, "Valkaris.sln")))
                root = root.Parent!;
            return root.FullName;
        }
    }

    public static Catalogue Load(string language)
    {
        var catalogue = new Catalogue();
        string? key = null;
        foreach (var line in File.ReadAllLines(Path.Combine(Root, "locale", $"{language}.po")))
        {
            var text = Quoted.Match(line) is { Success: true } match ? Regex.Unescape(match.Groups[1].Value) : "";
            if (line.StartsWith("msgid_plural", StringComparison.Ordinal))
                continue;
            if (line.StartsWith("msgid ", StringComparison.Ordinal))
            {
                key = text;
                if (key.Length > 0)
                    catalogue.Entries[key] = new List<string>();
            }
            else if (line.StartsWith("msgstr", StringComparison.Ordinal) && key is { Length: > 0 })
            {
                catalogue.Entries[key].Add(text);
            }
        }
        return catalogue;
    }
}

public class LocalisationTests
{
    /// <summary>The same rule as L.Key in the game.</summary>
    private static string Key(string prefix, string identifier)
    {
        var key = new StringBuilder(prefix).Append('.');
        for (var i = 0; i < identifier.Length; i++)
        {
            if (i > 0 && char.IsUpper(identifier[i]))
                key.Append('_');
            key.Append(char.ToLowerInvariant(identifier[i]));
        }
        return key.ToString();
    }

    private static IEnumerable<string> Keys<T>(string prefix, string suffix = "") where T : struct, Enum =>
        Enum.GetNames<T>().Select(name => Key(prefix, name) + suffix);

    [Fact]
    public void EveryGameNameHasAKey()
    {
        var expected = Keys<BuildingType>("building", ".name")
            .Concat(Keys<BuildingType>("building", ".description"))
            .Concat(Keys<UnitType>("unit", ".name"))
            .Concat(Keys<ResourceType>("resource", ".name"))
            .Concat(Keys<ResourceType>("resource", ".amount"))
            .Concat(Keys<MovementType>("mission"))
            .Concat(Keys<AiDifficulty>("difficulty"))
            .Concat(Keys<MapSize>("map_size"))
            .Concat(Keys<Terrain>("terrain"))
            .Concat(Keys<CommandRejection>("rejection"))
            .Concat(TestData.Shipped.Map.Barbarians.Tiers.Select(tier => Key("tier", tier.Name)));

        var catalogue = Catalogue.Load("en");
        Assert.Empty(expected.Where(key => !catalogue.Entries.ContainsKey(key)));
    }

    [Fact]
    public void EnglishSaysGrainAndNeverFood()
    {
        var catalogue = Catalogue.Load("en");

        Assert.Equal("Grain", catalogue.Entries["resource.food.name"].Single());
        Assert.Empty(catalogue.Entries.Where(e => e.Value.Any(text => Regex.IsMatch(text, @"\bfood\b", RegexOptions.IgnoreCase))));
    }

    [Fact]
    public void VillageNamesAreNotTranslated()
    {
        Assert.DoesNotContain(Catalogue.Load("en").Entries.Keys, key => key.StartsWith("village.name", StringComparison.Ordinal));
        Assert.Contains("Stonebridge", TestData.Shipped.Map.FoundedVillageNames);
        Assert.DoesNotContain(Catalogue.Load("en").Entries.Values.SelectMany(v => v), text => text.Contains("Stonebridge", StringComparison.Ordinal));
    }
}
