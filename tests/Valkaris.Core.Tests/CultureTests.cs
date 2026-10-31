using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;

namespace Valkaris.Core.Tests;

/// <summary>Lets the whole suite run under another culture: VALKARIS_TEST_CULTURE=tr-TR dotnet test.</summary>
internal static class TestCulture
{
    public static string? Requested => Environment.GetEnvironmentVariable("VALKARIS_TEST_CULTURE");

    [ModuleInitializer]
    internal static void Apply()
    {
        if (Requested is not { Length: > 0 } name)
            return;

        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}

public class CultureTests
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static T Under<T>(CultureInfo culture, Func<T> action)
    {
        var earlier = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = earlier;
        }
    }

    private static string PlayedMatch()
    {
        var match = new AiMatch(new[] { AiDifficulty.Normal, AiDifficulty.Hard }, seed: 42);
        match.Run(15 * 60);
        return SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(match.State));
    }

    [Fact]
    public void TheRequestedCultureIsInForce()
    {
        if (TestCulture.Requested is { Length: > 0 } name)
            Assert.Equal(name, CultureInfo.CurrentCulture.Name);
    }

    [Fact]
    public void TurkishRulesReallyApply()
    {
        Assert.Equal("İ", Under(Turkish, () => "i".ToUpper()));
        Assert.Equal("1,5", Under(Turkish, () => 1.5.ToString()));
    }

    [Fact]
    public void TheDataLoadsTheSameUnderTurkish()
    {
        var turkish = Under(Turkish, () => GameData.Load(new ShippedDataSource()));
        var invariant = Under(CultureInfo.InvariantCulture, () => GameData.Load(new ShippedDataSource()));

        Assert.Equal(invariant.Game.SpeedMultipliers, turkish.Game.SpeedMultipliers);
        Assert.Equal(invariant.Combat.WallBonusPerLevel, turkish.Combat.WallBonusPerLevel);
    }

    [Fact]
    public void AMatchPlaysTheSameUnderTurkish()
    {
        Assert.Equal(Under(CultureInfo.InvariantCulture, PlayedMatch), Under(Turkish, PlayedMatch));
    }

    [Fact]
    public void ASnapshotRoundTripsUnderTurkish()
    {
        Under(Turkish, () => SnapshotAssert.RoundTrips(TestMatch.Create(players: 3)));
    }

    private static readonly (Regex Pattern, Regex? Unless, string Why)[] Rules =
    {
        (new(@"\.(ToUpper|ToLower)\(\)"), null, "ToUpper/ToLower follow the culture; use the Invariant versions"),
        (new(@"\b(int|long|ulong|float|double|decimal)\.(Try)?Parse\("), new("CultureInfo"), "a parse must state its culture"),
        (new(@"\.(StartsWith|EndsWith)\(""|\b(?:string\.Compare|[A-Za-z]+\.(?:IndexOf|LastIndexOf))\("""), new("StringComparison"), "a text comparison must state its kind"),
        (new(@"\.ToString\(""[^""]*""\)"), null, "a formatted number must state its culture"),
    };

    private static readonly Regex FormatHole = new(@"\$""[^""]*\{[^{}""]+:[0#.]+\}");

    private static IEnumerable<string> ScannedFiles(string root) =>
        new[] { "src/Valkaris.Core", "src/Valkaris.Ai", "src/Presentation", "tools/Valkaris.Sim" }
            .SelectMany(folder => Directory.GetFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !file.Contains($"Presentation{Path.DirectorySeparatorChar}Dev{Path.DirectorySeparatorChar}"));

    [Fact]
    public void CodeNeverDependsOnTheCulture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Valkaris.sln")))
            root = root.Parent!;

        var problems = new List<string>();
        foreach (var file in ScannedFiles(root.FullName))
        {
            // The sim tool's reports fix their culture at startup, and its tests never compare the numbers as text.
            var inSim = file.Contains("Valkaris.Sim", StringComparison.Ordinal);
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var (pattern, unless, why) in Rules)
                {
                    if (pattern.IsMatch(lines[i]) && unless?.IsMatch(lines[i]) != true)
                        problems.Add($"{Path.GetFileName(file)}:{i + 1}: {why}");
                }
                if (!inSim && FormatHole.IsMatch(lines[i]))
                    problems.Add($"{Path.GetFileName(file)}:{i + 1}: a formatted number must go through Format");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
