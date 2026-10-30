using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Valkaris.Core.Commands;
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

        SelectTile(BuildingType.Woodcutter);
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

        await TestQueue(game, panel);
    }

    private async Task TestQueue(GameScreen game, BuildingPanel panel)
    {
        var session = game.Session;
        var village = session.State.Villages[1];
        var queue = Find<QueuePanel>();
        var upgrade = panel.GetNode<Button>("%Upgrade");

        session.Tick(120);
        await Frames();
        Expect(queue.GetNode<Label>("Box/Rows/Row0/Top/Title").Text == "Empty slot", "Queue starts empty");

        SelectTile(BuildingType.Quarry);
        await Frames();
        upgrade.EmitSignal(BaseButton.SignalName.Pressed);
        SelectTile(BuildingType.Farm);
        await Frames();
        upgrade.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();

        Expect(queue.GetNode<Label>("%Count").Text == "2 / 2", "Queue count updates");
        Expect(queue.GetNode<Label>("Box/Rows/Row0/Top/Title").Text == "Quarry → level 2", "First row names the build");
        Expect(queue.GetNode<Label>("Box/Rows/Row1/Top/Time").Text.StartsWith("starts in"), "Second row is waiting");
        Expect(upgrade.Disabled, "A full queue disables the button");
        Expect(panel.GetNode<Label>("%Reason").Text == "The build queue is full.", "The button says why it is disabled");

        session.Tick(21);
        await Frames();
        var bar = queue.GetNode<ProgressBar>("Box/Rows/Row0/Bar");
        Expect(bar.Value is > 0.4 and < 0.6, $"Progress bar follows time, got {bar.Value}");
        await Shot("queue-full");

        session.Tick(21);
        await Frames();
        Expect(queue.GetNode<Label>("%Count").Text == "1 / 2", "Completed item leaves the queue");
        Expect(queue.GetNode<Label>("Box/Rows/Row0/Top/Title").Text == "Farm → level 2", "Waiting item moves up");
        Expect(!upgrade.Disabled, "A free slot enables the button again");

        session.Tick(60);
        await Frames();

        await TestTiles();
    }

    private async Task TestTiles()
    {
        var panel = Find<BuildingPanel>();
        foreach (var building in Enum.GetValues<BuildingType>())
        {
            SelectTile(building);
            await Frames();
            Expect(panel.GetNode<Label>("%Name").Text == UiContent.Buildings[building].Name, $"{building} tile selects it");
        }

        var wall = Find<VillageView>().GetNode<BuildingTile>(nameof(BuildingType.Wall));
        Expect(wall.GetNode<Label>("%Level").Text == "Not built", "Unbuilt building says so");
        Expect(wall.GetNode<TextureRect>("%Icon").Modulate.A < 1, "Unbuilt building is dimmed");

        SelectTile(BuildingType.Wall);
        await Frames();
        panel.GetNode<Button>("%Upgrade").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(wall.GetNode<ProgressBar>("%Progress").Visible, "Tile under construction shows progress");
        await Shot("village");

        await TestFeedback();
        await TestPause();
        TestDevOptions();
        await TestMatchEnd();
    }

    private async Task TestFeedback()
    {
        var game = Find<GameScreen>();
        var session = game.Session;
        var toast = game.GetNode<Toast>("%Toast");

        session.Tick(120);
        await Frames();
        Expect(toast.Visible, "Completing a build shows a toast");
        Expect(toast.GetNode<Label>("Label").Text == "Wall reached level 1", "Toast names the building and level");

        session.Send(new BuildCommand(2, BuildingType.Wall));
        await Frames();
        Expect(toast.GetNode<Label>("Label").Text == UiContent.RejectionText(CommandRejection.NotOwner), "A rejection shows a toast");

        session.Tick(600);
        await Frames();
        var amount = Find<ResourceBar>().GetNode<Label>("Box/Chips/Stone/Amount");
        Expect(amount.GetThemeColor("font_color") == UiContent.Warning, "A resource at its cap turns amber");
        await Shot("at-cap");
    }

    private async Task TestPause()
    {
        var game = Find<GameScreen>();
        var session = game.Session;
        var overlay = game.GetNode<Overlay>("%PauseOverlay");
        var pause = Find<ResourceBar>().GetNode<Button>("%Pause");

        Expect(pause.Visible, "The local session shows the pause button");
        pause.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(session.IsPaused && overlay.Visible, "The button pauses and shows the overlay");
        await Shot("paused");

        var time = session.State.ElapsedTime;
        await Frames(5);
        Expect(session.State.ElapsedTime == time, "Time stands still while paused");

        overlay.GetNode<Button>("%Action").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(!session.IsPaused && !overlay.Visible, "Resume hides the overlay");
        Expect(session.State.ElapsedTime > time, "Time runs again after resuming");

        var pressP = new InputEventKey { Pressed = true, Keycode = Key.P };
        game._UnhandledKeyInput(pressP);
        Expect(session.IsPaused, "P pauses");
        game._UnhandledKeyInput(pressP);
        Expect(!session.IsPaused, "P resumes");
    }

    private void TestDevOptions()
    {
        var arguments = new[] { "--dev-speed=20", "--dev-duration=60" };
        var settings = new GameSettings(1200, 1, MapSize.Small);

        var debug = DevOptions.Parse(arguments, isDebugBuild: true).Apply(settings);
        Expect(debug.SpeedMultiplier == 20 && debug.MatchDurationSeconds == 60, "Dev arguments apply in debug builds");

        var release = DevOptions.Parse(arguments, isDebugBuild: false).Apply(settings);
        Expect(release == settings, "Dev arguments are ignored in release builds");

        var invalid = DevOptions.Parse(new[] { "--dev-speed=abc", "--dev-duration=-5" }, isDebugBuild: true).Apply(settings);
        Expect(invalid == settings, "Invalid dev arguments are ignored");
    }

    private async Task TestMatchEnd()
    {
        var game = Find<GameScreen>();
        var session = game.Session;

        session.Tick(session.State.Settings.MatchDurationSeconds);
        await Frames();
        var overlay = game.GetNode<Overlay>("%MatchOverOverlay");
        Expect(overlay.Visible, "Match end shows the overlay");
        await Shot("match-over");

        game._UnhandledKeyInput(new InputEventKey { Pressed = true, Keycode = Key.P });
        Expect(!session.IsPaused, "A finished match cannot be paused");

        overlay.GetNode<Button>("%Action").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Find<StartScreen>().GetNode<Button>("%Start").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        var fresh = Find<GameScreen>().Session;
        Expect(!ReferenceEquals(fresh, session), "Starting again creates a new session");
        Expect(fresh.State.ElapsedTime < 1 && !fresh.State.IsMatchOver, "The new match starts fresh");
    }

    private void SelectTile(BuildingType building) =>
        Find<VillageView>().GetNode<BuildingTile>(building.ToString()).EmitSignal(BaseButton.SignalName.Pressed);

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
