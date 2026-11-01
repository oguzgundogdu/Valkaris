using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
            .Concat(Keys<Valkaris.Core.Network.JoinRefusal>("join"))
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

    private static readonly Regex KeyPattern = new(@"^[a-z_]+(\.[a-z0-9_]+)+$");

    // Calls whose text arguments are never shown to the player.
    private static readonly HashSet<string> NonTextCalls = new()
    {
        "GetNode", "Load", "AddThemeColorOverride", "RemoveThemeColorOverride", "AddThemeConstantOverride", "AddThemeFontSizeOverride", "AddThemeStyleboxOverride",
        "TweenProperty", "GetThemeFont", "GetThemeFontSize", "T", "N", "Key", "ResizedIcon", "GetValue", "SetValue", "Open",
    };

    // Properties whose values are never shown to the player.
    private static readonly HashSet<string> NonTextProperties = new() { "Name", "ThemeTypeVariation", "IconPath", "SettingsPath" };

    // Names in a file that are identifiers, each with the reason.
    private static readonly (string File, string Text)[] Exceptions =
    {
        ("Main.cs", "You"), // The stored name of the local player. The UI always shows the translated word instead.
        ("MatchEndScreen.cs", "StrongLabel"), // A theme style held in a variable.
        ("UserSettings.cs", "player"), // Settings file sections and keys.
        ("UserSettings.cs", "name"),
        ("UserSettings.cs", "display"),
        ("UserSettings.cs", "fullscreen"),
        ("UserSettings.cs", "drawer_height"),
        ("UserSettings.cs", "nation"),
        ("UserSettings.cs", "audio"),
        ("UserSettings.cs", "master"),
        ("UserSettings.cs", "music"),
        ("UserSettings.cs", "effects"),
        ("UserSettings.cs", "muted"),
        ("WorldMapView.cs", "In"), // Node names of the zoom buttons.
        ("WorldMapView.cs", "Out"),
        ("Sound.cs", "Master"), // Audio bus names.
        ("Sound.cs", "Music"),
        ("Sound.cs", "Effects"),
        ("Language.cs", "language"), // Settings file section.
        ("Language.cs", "code"), // Settings file key.
        ("Language.cs", "en"),
        ("Language.cs", "tr"),
    };

    // Dev flags, and messages for whoever edits data/.
    private static readonly string[] SkippedFiles = { "DevOptions.cs", "GodotDataSource.cs" };

    private static string TypeOf(SyntaxNode node) => node.Ancestors().OfType<TypeDeclarationSyntax>().LastOrDefault()?.Identifier.Text ?? "";

    private static bool IsNonText(SyntaxNode node)
    {
        foreach (var parent in node.Ancestors())
        {
            if (parent is InvocationExpressionSyntax call)
            {
                var name = call.Expression switch
                {
                    MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
                    GenericNameSyntax generic => generic.Identifier.Text,
                    IdentifierNameSyntax identifier => identifier.Identifier.Text,
                    _ => "",
                };
                if (NonTextCalls.Contains(name))
                    return true;
            }

            var target = parent switch
            {
                AssignmentExpressionSyntax { Left: IdentifierNameSyntax left } => left.Identifier.Text,
                AssignmentExpressionSyntax { Left: MemberAccessExpressionSyntax left } => left.Name.Identifier.Text,
                _ => null,
            };
            if (target is not null)
                return NonTextProperties.Contains(target);
        }
        return false;
    }

    private static IEnumerable<string> ScanSource(string file, string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        foreach (var node in root.DescendantNodes())
        {
            // Format and L are the two places allowed to build text from values.
            if (TypeOf(node) is "Format" or "L")
                continue;

            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (node is InterpolatedStringExpressionSyntax && !IsNonText(node))
                yield return $"{file}:{line}: interpolated text: {node}";
            else if (node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "ToString" } } && !IsNonText(node))
                yield return $"{file}:{line}: ToString() as text: {node}";
            else if (node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                var text = literal.Token.ValueText;
                if (text.Any(char.IsLetter) && !text.StartsWith("res://", StringComparison.Ordinal) && !text.StartsWith("user://", StringComparison.Ordinal) && !KeyPattern.IsMatch(text)
                    && !IsNonText(node) && !Exceptions.Contains((file, text)))
                    yield return $"{file}:{line}: hard-coded text: {text}";
            }
        }
    }

    private static IEnumerable<string> PresentationFiles() =>
        Directory.GetFiles(Path.Combine(Catalogue.Root, "src", "Presentation"), "*.cs")
            .Where(file => !SkippedFiles.Contains(Path.GetFileName(file)));

    [Fact]
    public void NoHardCodedTextInTheGameCode()
    {
        var problems = PresentationFiles().SelectMany(file => ScanSource(Path.GetFileName(file), File.ReadAllText(file))).ToList();

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Theory]
    [InlineData("""class A { void F(Label l) { l.Text = "Hello there"; } }""")]
    [InlineData("""class A { void F(Label l, int n) { l.Text = $"{n}"; } }""")]
    [InlineData("""class A { void F(Label l, object t) { l.Text = t.ToString(); } }""")]
    [InlineData("""class A { void F(Button b) { b.TooltipText = "Tip"; } }""")]
    [InlineData("""class A { string F() => "Plain words"; }""")]
    public void TheScanCatchesNewHardCodedText(string source)
    {
        Assert.NotEmpty(ScanSource("New.cs", source));
    }

    [Theory]
    [InlineData("""class A { void F(Label l) { l.Text = L.T("some.key"); } }""")]
    [InlineData("""class A { void F() { var l = new Label { Name = "Thing" }; GetNode<Label>("%Thing"); } }""")]
    [InlineData("""class A { void F(int n) { var l = new Label { Name = $"Row{n}" }; } }""")]
    [InlineData("""class Format { string F(int n) => $"/ {n}"; }""")]
    public void TheScanAllowsWhatIsNotText(string source)
    {
        Assert.Empty(ScanSource("New.cs", source));
    }

    [Fact]
    public void ScenesHoldOnlyKeys()
    {
        var shown = new Regex("""^(text|tooltip_text|placeholder_text|title) = "(.*)"$""");
        var problems = Directory.GetFiles(Path.Combine(Catalogue.Root, "scenes"), "*.tscn", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadAllLines(file).Select(line => (file, match: shown.Match(line))))
            .Where(line => line.match.Success)
            .Select(line => (line.file, text: line.match.Groups[2].Value))
            .Where(line => line.text.Length > 0 && !KeyPattern.IsMatch(line.text) && line.text is not ("Valkaris" or "VALKARIS"))
            .Select(line => $"{Path.GetFileName(line.file)}: {line.text}")
            .ToList();

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static IEnumerable<string> UsedKeys()
    {
        var code = PresentationFiles().Select(File.ReadAllText)
            .SelectMany(source => CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes())
            .OfType<LiteralExpressionSyntax>()
            .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(literal => literal.Token.ValueText);
        var scenes = Directory.GetFiles(Path.Combine(Catalogue.Root, "scenes"), "*.tscn", SearchOption.AllDirectories)
            .SelectMany(File.ReadAllLines)
            .Select(line => Regex.Match(line, "\"([a-z_]+(\\.[a-z0-9_]+)+)\"$").Groups[1].Value);
        return code.Concat(scenes).Where(text => KeyPattern.IsMatch(text)).Distinct();
    }

    // Keys built from an enum or a data identifier, checked by EveryGameNameHasAKey.
    private static readonly string[] BuiltKeyPrefixes = { "building.", "unit.", "resource.", "mission.", "tier.", "difficulty.", "map_size.", "terrain.", "rejection.", "language.", "join." };

    [Fact]
    public void EveryKeyInTheCodeExists()
    {
        var catalogue = Catalogue.Load("en");
        Assert.Empty(UsedKeys().Where(key => !catalogue.Entries.ContainsKey(key)));
    }

    [Fact]
    public void EveryKeyInTheFilesIsUsed()
    {
        var used = UsedKeys().ToHashSet();
        Assert.Empty(Catalogue.Load("en").Entries.Keys
            .Where(key => !used.Contains(key) && !BuiltKeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal))));
    }

    private static HashSet<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{(\w+)\}").Select(m => m.Value).ToHashSet();

    [Fact]
    public void BothLanguagesHaveTheSameKeysAndPlaceholders()
    {
        var english = Catalogue.Load("en").Entries;
        var turkish = Catalogue.Load("tr").Entries;

        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), turkish.Keys.Order(StringComparer.Ordinal));
        foreach (var (key, forms) in english)
            Assert.True(Placeholders(forms[^1]).SetEquals(Placeholders(turkish[key].Single())), $"{key}: placeholders differ");
    }

    [Fact]
    public void TurkishHasOnePluralFormAndEnglishTwo()
    {
        Assert.Equal(2, Catalogue.Load("en").Entries["common.minutes"].Count);
        Assert.Single(Catalogue.Load("tr").Entries["common.minutes"]);
    }

    [Theory]
    [InlineData("mission.attack", "Saldırı")]
    [InlineData("mission.raid", "Yağma")]
    [InlineData("mission.reinforce", "Takviye")]
    [InlineData("mission.scout", "Keşif")]
    [InlineData("unit.scout.name", "Casus")]
    [InlineData("unit.spearman.name", "Mızrakçı")]
    [InlineData("unit.swordsman.name", "Kılıççı")]
    [InlineData("unit.cavalry.name", "Atlı")]
    [InlineData("unit.catapult.name", "Mancınık")]
    [InlineData("unit.settler.name", "Öncü")]
    [InlineData("unit.lord.name", "Vali")]
    [InlineData("building.main_building.name", "Merkez Bina")]
    [InlineData("building.warehouse.name", "Depo")]
    [InlineData("building.barracks.name", "Kışla")]
    [InlineData("building.wall.name", "Sur")]
    [InlineData("building.watchtower.name", "Gözetleme Kulesi")]
    [InlineData("building.woodcutter.name", "Oduncu")]
    [InlineData("building.quarry.name", "Taş Ocağı")]
    [InlineData("building.farm.name", "Çiftlik")]
    [InlineData("building.marketplace.name", "Pazar Yeri")]
    [InlineData("player.barbarians", "Barbarlar")]
    [InlineData("tier.camp", "Kamp")]
    [InlineData("tier.stronghold", "Kale")]
    [InlineData("overlay.surrender", "Teslim Ol")]
    [InlineData("resource.wood.name", "Odun")]
    [InlineData("resource.stone.name", "Taş")]
    [InlineData("resource.food.name", "Tahıl")]
    [InlineData("popup.capital", "Başkent")]
    public void TurkishUsesTheGlossary(string key, string term)
    {
        Assert.Equal(term, Catalogue.Load("tr").Entries[key].Single());
    }

    [Theory]
    [InlineData("overlay.leave", "Oyundan çık")]
    [InlineData("ui.bar.world", "Harita")]
    [InlineData("ui.bar.world_tip", "Harita (M)")]
    [InlineData("send.pick_target", "Haritadan hedef seç.")]
    [InlineData("overlay.surrender.title", "Teslim olmak istiyor musun?")]
    [InlineData("scout.result_narrow", "Keşif: kısmi bilgi")]
    [InlineData("scout.resources", "Kaynaklar: {list}")]
    [InlineData("queue.empty", "Boş")]
    [InlineData("movement.returning_loot", "Ganimetle dönüyor: {amount}")]
    [InlineData("toast.troops_arrived", "Hedefe ulaşıldı · {village}")]
    public void TurkishUsesTheApprovedWording(string key, string text)
    {
        Assert.Equal(text, Catalogue.Load("tr").Entries[key].Single());
    }

    [Fact]
    public void TurkishSaysOyunAndHaritaNotMacAndDunya()
    {
        var texts = Catalogue.Load("tr").Entries.Values.SelectMany(v => v).ToList();

        Assert.DoesNotContain(texts, text => text.Contains("Maç", StringComparison.Ordinal) || text.Contains("maç", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, text => text.Contains("Dünya", StringComparison.Ordinal) || text.Contains("dünya", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Sadakat", "scout.loyalty")]
    [InlineData("Tüccar", "effect.carriers")]
    public void TurkishGlossaryTermsAppearInSentences(string term, string key)
    {
        Assert.Contains(term, Catalogue.Load("tr").Entries[key].Single());
    }

    [Fact]
    public void TurkishDiffersFromEnglishExceptWhereIdentical()
    {
        var english = Catalogue.Load("en").Entries;
        var turkish = Catalogue.Load("tr").Entries;
        var same = english.Keys.Where(key => english[key][^1] == turkish[key].Single() && english[key][^1].Any(char.IsLetter)).Order(StringComparer.Ordinal).ToList();

        // Only text made up entirely of names and placeholders may match.
        Assert.Equal(new[]
        {
            "bar.incoming_short", "difficulty.normal", "end.difficulty", "language.en", "language.tr", "lobby.player_village", "movement.to", "notice.repeat", "queue.train_item", "report.unit_count", "scout.building_level", "start.map_size",
        }, same);
    }
}
