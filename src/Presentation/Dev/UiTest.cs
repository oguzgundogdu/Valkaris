using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation.Dev;

/// <summary>
/// Drives the real UI and exits non-zero on failure. Dev only: excluded from exports.
/// Pass "-- --shots=DIR" in a windowed run to also save a screenshot of each step.
/// </summary>
public partial class UiTest : Control
{
    private readonly List<string> _failures = new();
    private string? _shotsDir;
    private Main _main = null!;

    public override async void _Ready()
    {
        _shotsDir = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith("--shots="))?["--shots=".Length..];

        try
        {
            await Run();
        }
        catch (Exception ex)
        {
            _failures.Add($"Exception: {ex}");
        }

        foreach (var failure in _failures)
            GD.PrintErr($"[uitest] FAIL {failure}");
        GD.Print(_failures.Count == 0 ? "[uitest] PASS" : $"[uitest] {_failures.Count} failure(s)");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private async Task Run()
    {
        _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        AddChild(_main);
        await Frames();

        var start = Find<StartScreen>();
        await Shot("start");

        start.GetNode<Button>("%Start").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();

        var game = Find<GameScreen>();
        var session = game.Session;
        var village = session.State.Villages[1];
        var panel = Find<BuildingPanel>();
        var upgrade = panel.GetNode<Button>("%Upgrade");

        game.Select(BuildingType.Woodcutter);
        await Frames();
        Expect(panel.GetNode<Label>("%Name").Text == "Woodcutter", "Selecting shows the building");
        Expect(panel.GetNode<Label>("%Level").Text == "Level 1", "Starts at level 1");
        Expect(upgrade.Text == "Upgrade to level 2", $"Button names the next level, got '{upgrade.Text}'");
        await Shot("game");

        var woodBefore = village.Resources.Wood;
        upgrade.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(village.BuildQueue.Count == 1, "Upgrade queues the build");
        Expect(village.Resources.Wood < woodBefore, "Upgrade pays the cost");
        await Shot("queued");

        session.Tick(BuildingRules.BuildTimeSeconds(session.Data, BuildingType.Woodcutter, 2));
        await Frames();
        Expect(panel.GetNode<Label>("%Level").Text == "Level 2", "Level changes when the build completes");

        var woodLabel = Find<ResourceBar>().GetNode<Label>("Box/Chips/Wood/Amount");
        var shownWood = woodLabel.Text;
        session.Tick(10);
        await Frames();
        Expect(woodLabel.Text != shownWood, "Resource amount ticks up");

        var rate = Find<ResourceBar>().GetNode<Label>("Box/Chips/Stone/Rate").Text;
        Expect(rate == "+1.0/s", $"Numbers ignore the system locale, got '{rate}'");
    }

    private T Find<T>() where T : Node =>
        FindAll<T>(_main).FirstOrDefault() ?? throw new InvalidOperationException($"{typeof(T).Name} not found.");

    private static IEnumerable<T> FindAll<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T match && !child.IsQueuedForDeletion())
                yield return match;
            foreach (var nested in FindAll<T>(child))
                yield return nested;
        }
    }

    private void Expect(bool condition, string description)
    {
        if (!condition)
            _failures.Add(description);
    }

    private async Task Frames(int count = 3)
    {
        for (var i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (_shotsDir is null || DisplayServer.GetName() == "headless")
            return;

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng($"{_shotsDir}/{name}.png");
    }
}
