using Valkaris.Core.Model;

namespace Valkaris.Core.Tests;

public class ArchitectureTests
{
    [Fact]
    public void CoreDoesNotReferenceGodot()
    {
        var references = typeof(GameState).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, r => r.Name!.Contains("Godot", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("ConnectScreen.cs")]
    [InlineData("LobbyScreen.cs")]
    [InlineData("EnetNetwork.cs")]
    public void TheMultiplayerPathNeverReadsDevOptions(string file)
    {
        Assert.DoesNotContain("DevOptions", File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Presentation", file)));
    }

    [Fact]
    public void OnlyTheSinglePlayerStartAppliesDevOptions()
    {
        var main = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Presentation", "Main.cs"));
        var from = main.IndexOf("private void ShowConnect", StringComparison.Ordinal);
        var to = main.IndexOf("private void StartMatch", StringComparison.Ordinal);

        Assert.True(from > 0 && to > from);
        Assert.DoesNotContain("devOptions", main[from..to]);
        Assert.DoesNotContain("DevOptions.Parse", main[from..to]);
        Assert.DoesNotContain("GodMode", main[from..to]);
    }

    [Fact]
    public void DevScenariosAreLeftOutOfReleaseBuilds()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Valkaris.sln")))
            root = root.Parent!;
        var project = File.ReadAllText(Path.Combine(root.FullName, "src", "Valkaris.Core", "Valkaris.Core.csproj"));

        Assert.Contains("""<Compile Remove="Dev/**" Condition="'$(Configuration)' == 'ExportRelease'" />""", project);
    }

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Valkaris.sln")))
            root = root.Parent!;
        return root.FullName;
    }

    /// <summary>The part of a file outside #if VALKARIS_DEV blocks, which is what ExportRelease compiles.</summary>
    private static string ReleaseText(string path)
    {
        var kept = new List<string>();
        var inDev = false;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Trim() == "#if VALKARIS_DEV")
                inDev = true;
            else if (inDev && line.Trim() is "#else" or "#endif")
                inDev = false;
            else if (!inDev)
                kept.Add(line);
        }
        return string.Join("\n", kept);
    }

    [Fact]
    public void GodModeIsLeftOutOfReleaseBuilds()
    {
        var root = RepositoryRoot();
        var game = File.ReadAllText(Path.Combine(root, "Valkaris.csproj"));
        Assert.Contains("""<Compile Remove="src/Presentation/Dev/**" Condition="'$(Configuration)' == 'ExportRelease'" />""", game);
        Assert.True(File.Exists(Path.Combine(root, "src", "Presentation", "Dev", "GodModeControl.cs")));

        var sources = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Dev{Path.DirectorySeparatorChar}"));
        foreach (var source in sources)
        {
            var text = ReleaseText(source);
            Assert.DoesNotContain("GodPlayerId", text);
            Assert.DoesNotContain("SetGodMode", text);
            Assert.DoesNotContain("GodModeControl", text);
        }
    }

    [Fact]
    public void OnlyTheLocalSessionCanTurnOnGodMode()
    {
        var names = typeof(Valkaris.Core.Hosting.IGameSession).GetMembers().Select(m => m.Name);

        Assert.DoesNotContain(names, n => n.Contains("God", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(GameState).GetMembers(), m => m.Name.Contains("God", StringComparison.Ordinal));
    }

    [Fact]
    public void TheAiAndTheSimToolNeverTouchGodMode()
    {
        var root = RepositoryRoot();
        var sources = new[] { Path.Combine(root, "src", "Valkaris.Ai"), Path.Combine(root, "tools", "Valkaris.Sim") }
            .SelectMany(folder => Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        Assert.All(sources, source => Assert.DoesNotContain("God", File.ReadAllText(source)));
    }

    [Fact]
    public void ModelClassesHaveNoPublicSetters()
    {
        var modelTypes = typeof(GameState).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(GameState).Namespace && t.IsClass && t.IsPublic)
            .Where(t => t.GetProperty("EqualityContract", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) is null);

        var publicSetters = modelTypes
            .SelectMany(t => t.GetProperties())
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}");

        Assert.Empty(publicSetters);
    }
}
