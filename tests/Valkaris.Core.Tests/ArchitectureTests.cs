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

    [Fact]
    public void DevScenariosAreLeftOutOfReleaseBuilds()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Valkaris.sln")))
            root = root.Parent!;
        var project = File.ReadAllText(Path.Combine(root.FullName, "src", "Valkaris.Core", "Valkaris.Core.csproj"));

        Assert.Contains("""<Compile Remove="Dev/**" Condition="'$(Configuration)' == 'ExportRelease'" />""", project);
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
