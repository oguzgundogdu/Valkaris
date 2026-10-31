using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation.Dev;

/// <summary>
/// Drives the real UI and exits non-zero on failure. Dev only: excluded from exports.
/// Pass "-- --shots=DIR" in a windowed run to also save a screenshot of each step.
/// </summary>
public partial class UiTest : Control
{
    private const double TestDuration = 3600;

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

        // Quitting with live scenes can abort inside Godot's shutdown, failing a passed run.
        foreach (var child in GetChildren())
            child.QueueFree();
        await Frames();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    /// <summary>A run under tr-TR proves nothing unless that culture is really in force.</summary>
    private void TestCulture()
    {
        if (DevOptions.Parse(OS.GetCmdlineUserArgs(), OS.IsDebugBuild()).Culture is not { } culture)
            return;

        Expect(System.Globalization.CultureInfo.CurrentCulture.Name == culture, $"The culture is {culture}, got '{System.Globalization.CultureInfo.CurrentCulture.Name}'");
        Expect(culture != "tr-TR" || "i".ToUpper() == "\u0130", "tr-TR rules are in force");
    }

    /// <summary>The text is this key's translation, with anything in its placeholders.</summary>
    private static bool Shows(string text, string key)
    {
        var pattern = System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Escape(L.T(key)), @"\\\{\w+}", ".*");
        return System.Text.RegularExpressions.Regex.IsMatch(text, $"^{pattern}$", System.Text.RegularExpressions.RegexOptions.Singleline);
    }

    private static string RaidOnBarbarians() =>
        L.T("report.headline_attacker", ("result", ""), ("mission", UiContent.MissionName(MovementType.Raid)), ("target", L.T("player.barbarians"))).TrimStart(' ', ':');

    private async Task TestLanguage()
    {
        var language = Language.Current;

        Expect(Language.Pick(null, "tr") == "tr", "A first launch follows a Turkish system");
        Expect(Language.Pick(null, "de") == "en" && Language.Pick(null, "en") == "en", "A first launch on any other system is English");
        Expect(Language.Pick("en", "tr") == "en" && Language.Pick("tr", "en") == "tr", "A saved language beats the system");
        Expect(Language.Pick("xx", "en") == "en", "An unknown saved language is ignored");

        var duration = Find<StartScreen>().GetNode<OptionButton>("%Duration");
        duration.Select(1);
        Find<StartScreen>().GetNode<OptionButton>("%Language").Select(Array.IndexOf(Language.Codes, "tr"));
        Find<StartScreen>().GetNode<OptionButton>("%Language").EmitSignal(OptionButton.SignalName.ItemSelected, Array.IndexOf(Language.Codes, "tr"));
        await Frames();

        var start = Find<StartScreen>();
        Expect(Language.Current == "tr" && TranslationServer.GetLocale().StartsWith("tr", StringComparison.Ordinal), "Choosing Turkish applies it");
        Expect(Language.Load() == "tr", "The choice is remembered");
        Expect(start.GetNode<Label>("Card/Box/LanguageRow/Label").Tr("ui.start.language") == "Dil", "The start screen shows Turkish");
        Expect(start.GetNode<OptionButton>("%Duration").Selected == 1, "Other choices survive the language change");
        Expect(L.N("common.minutes", 1) == "1 dakika" && L.N("common.minutes", 2) == "2 dakika", $"Turkish has one plural form, got '{L.N("common.minutes", 2)}'");

        start.GetNode<OptionButton>("%Language").EmitSignal(OptionButton.SignalName.ItemSelected, Array.IndexOf(Language.Codes, "en"));
        await Frames();
        Expect(Language.Load() == "en" && L.T("ui.start.language") == "Language", "Choosing English applies it");
        Expect(L.N("common.minutes", 1) == "1 minute" && L.N("common.minutes", 2) == "2 minutes", $"English has two plural forms, got '{L.N("common.minutes", 1)}'");
        Expect(L.T("language.tr") == "T\u00fcrk\u00e7e", "A language shows its own name");

        Find<StartScreen>().GetNode<OptionButton>("%Duration").Select(0);
        Find<StartScreen>().GetNode<OptionButton>("%Language").EmitSignal(OptionButton.SignalName.ItemSelected, Array.IndexOf(Language.Codes, language));
        await Frames();
    }

    private async Task Run()
    {
        // Never touches the language saved for real play.
        Language.SettingsPath = "user://uitest_settings.cfg";
        DirAccess.RemoveAbsolute(Language.SettingsPath);

        _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        _main.IdleOpponents = true;
        AddChild(_main);
        await Frames();
        TestCulture();
        await TestLanguage();

        var start = Find<StartScreen>();
        await Shot("start");

        await TestUnlimitedFromStart();
        start = Find<StartScreen>();

        // The longest timed match, so the steps below can use plenty of game time.
        Expect(start.GetNode<OptionButton>("%MapSize").ItemCount == Enum.GetValues<MapSize>().Length, "Every map size is offered");
        await TestOpponentChoices();
        start = Find<StartScreen>();
        var durations = start.GetNode<OptionButton>("%Duration");
        durations.Select(LongestTimed(durations));
        start.GetNode<Button>("%Start").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();

        var game = Find<GameScreen>();
        var session = game.Session;
        var village = session.State.Villages[1];
        Expect(session.State.Settings.MapSize == MapSize.Small, "The default map size is small");

        var dev = DevOptions.Parse(OS.GetCmdlineUserArgs(), OS.IsDebugBuild());
        Expect(session.State.Settings.MatchDurationSeconds == (dev.DurationSeconds ?? session.Data.Game.MatchDurationsMinutes[^1] * 60), "Match duration follows the start screen or dev argument");
        Expect(session.State.Settings.SpeedMultiplier == (dev.Speed ?? session.Data.Game.SpeedMultipliers[0]), "Game speed follows the start screen or dev argument");
        var panel = Find<BuildingPanel>();
        var upgrade = panel.GetNode<Button>("%Upgrade");

        SelectTile(BuildingType.Woodcutter);
        await Frames();
        Expect(panel.GetNode<Label>("%Name").Text == UiContent.Buildings[BuildingType.Woodcutter].Name, "Selecting shows the building");
        Expect(panel.GetNode<Label>("%Level").Text == L.T("common.level", ("level", "1")), "Starts at level 1");
        Expect(upgrade.Text == L.T("upgrade.to_level", ("level", "2")), $"Button names the next level, got '{upgrade.Text}'");
        await Shot("game");

        var woodBefore = village.Resources.Wood;
        upgrade.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(village.BuildQueue.Count == 1, "Upgrade queues the build");
        Expect(village.Resources.Wood < woodBefore, "Upgrade pays the cost");
        await Shot("queued");

        session.Tick(BuildingRules.BuildTimeSeconds(session.Data, BuildingType.Woodcutter, 2));
        await Frames();
        Expect(panel.GetNode<Label>("%Level").Text == L.T("common.level", ("level", "2")), "Level changes when the build completes");

        var woodLabel = Find<ResourceBar>().GetNode<Label>("Box/Chips/Wood/Amount");
        var shownWood = woodLabel.Text;
        session.Tick(10);
        await Frames();
        Expect(woodLabel.Text != shownWood, "Resource amount ticks up");

        var rate = Find<ResourceBar>().GetNode<Label>("Box/Chips/Stone/Rate").Text;
        Expect(rate == "+3.0/s", $"Numbers ignore the system locale, got '{rate}'");

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
        Expect(queue.GetNode<Label>("Box/Rows/Row0/Top/Title").Text == L.T("queue.empty"), "Queue starts empty");

        SelectTile(BuildingType.Quarry);
        await Frames();
        upgrade.EmitSignal(BaseButton.SignalName.Pressed);
        SelectTile(BuildingType.Farm);
        await Frames();
        upgrade.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();

        Expect(queue.GetNode<Label>("%Count").Text == "2 / 2", "Queue count updates");
        Expect(queue.GetNode<Label>("Box/Rows/Row0/Top/Title").Text == L.T("queue.build_item", ("building", UiContent.Buildings[BuildingType.Quarry].Name), ("level", "2")), "First row names the build");
        Expect(Shows(queue.GetNode<Label>("Box/Rows/Row1/Top/Time").Text, "queue.starts_in"), "Second row is waiting");
        Expect(upgrade.Disabled, "A full queue disables the button");
        Expect(panel.GetNode<Label>("%Reason").Text == UiContent.RejectionText(CommandRejection.QueueFull), "The button says why it is disabled");

        var firstBuild = session.State.Villages[1].BuildQueue[0];
        session.Tick((firstBuild.FinishTime - firstBuild.StartTime) / 2);
        await Frames();
        var bar = queue.GetNode<ProgressBar>("Box/Rows/Row0/Bar");
        Expect(bar.Value is > 0.4 and < 0.6, $"Progress bar follows time, got {bar.Value}");
        await Shot("queue-full");

        session.Tick((firstBuild.FinishTime - firstBuild.StartTime) / 2 + 0.01);
        await Frames();
        Expect(queue.GetNode<Label>("%Count").Text == "1 / 2", "Completed item leaves the queue");
        Expect(queue.GetNode<Label>("Box/Rows/Row0/Top/Title").Text == L.T("queue.build_item", ("building", UiContent.Buildings[BuildingType.Farm].Name), ("level", "2")), "Waiting item moves up");
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
        Expect(wall.GetNode<Label>("%Level").Text == L.T("common.not_built"), "Unbuilt building says so");
        Expect(wall.GetNode<TextureRect>("%Icon").Modulate.A < 1, "Unbuilt building is dimmed");

        var tiles = Find<VillageView>().GetChildren().OfType<BuildingTile>().ToList();
        Expect(tiles.Count == Enum.GetValues<BuildingType>().Length, "Every building has a tile");
        Expect(!tiles.Any(t => tiles.Any(o => o != t && t.GetRect().Intersects(o.GetRect()))), "No building tiles overlap");
        SelectTile(BuildingType.Watchtower);
        await Frames();
        Expect(panel.GetNode<Label>("%Name").Text == UiContent.Buildings[BuildingType.Watchtower].Name, "The Watchtower can be selected");
        Expect(panel.GetNode<Button>("%Upgrade").Text == L.T("upgrade.build"), $"The Watchtower can be built, got '{panel.GetNode<Button>("%Upgrade").Text}'");

        SelectTile(BuildingType.Wall);
        await Frames();
        panel.GetNode<Button>("%Upgrade").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(wall.GetNode<ProgressBar>("%Progress").Visible, "Tile under construction shows progress");
        await Shot("village");

        await TestTroops();
        await TestSend();
        await TestReports();
        await TestFeedback();
        await TestPause();
        TestDevOptions();
        await TestViewSwitching();
        await TestCamera();
        await TestSelection();
        await TestSendFromMap();
        await TestLeaveMatch();
        await TestMatchEnd();
    }

    private async Task TestViewSwitching()
    {
        var game = Find<GameScreen>();
        var bar = game.GetNode<ResourceBar>("%ResourceBar");
        var villageView = game.GetNode<VillageView>("%VillageView");
        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var pressM = new InputEventKey { Pressed = true, Keycode = Key.M };
        var send = game.GetNode<SendPanel>("%Send");
        var amount = send.GetNode<VBoxContainer>("%Units").GetNode(nameof(UnitType.Spearman)).GetNode<SpinBox>("Amount");

        Expect(bar.GetNode<Button>("%ViewVillage").ButtonPressed, "The toggle starts on Village");

        game.ShowTab(GameScreen.SideTab.Send);
        await Frames();
        amount.Value = 1;

        bar.GetNode<Button>("%ViewWorld").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(world.Visible && !villageView.Visible, "The toggle opens the map");
        Expect(bar.GetNode<Button>("%ViewWorld").ButtonPressed && !bar.GetNode<Button>("%ViewVillage").ButtonPressed, "The toggle shows the current view");
        Expect(world.GetNode("World/Markers").GetChildCount() == game.Session.State.Villages.Count, "Every village has a marker");
        Expect((int)amount.Value == 1, "The Send form survives switching to the map");
        await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
        Expect(world.Modulate.A == 1, "The fade finishes");
        await Shot("map-small");

        game._UnhandledKeyInput(pressM);
        await Frames();
        Expect(villageView.Visible && !world.Visible, "M returns to the village");
        Expect((int)amount.Value == 1, "The Send form survives switching back");

        game._UnhandledKeyInput(pressM);
        await Frames();
        Expect(world.Visible, "M opens the map");
        game._UnhandledKeyInput(pressM);
        await Frames();
        amount.Value = 0;

        // Typing in a number field must not switch views.
        var line = amount.GetLineEdit();
        line.GrabFocus();
        await Frames();
        Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = Key.M, PhysicalKeycode = Key.M, Unicode = 'm' });
        Input.FlushBufferedEvents();
        await Frames();
        Expect(game.CurrentView == GameScreen.MainView.Village, "M does nothing while typing in a number field");
        line.ReleaseFocus();
    }

    private async Task TestCamera()
    {
        var game = Find<GameScreen>();
        game.ShowView(GameScreen.MainView.World);
        await Frames();
        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var mapSize = new Vector2(game.Session.State.Map.Width, game.Session.State.Map.Height) * WorldMapView.TileSize;
        var fit = world.Zoom;

        Expect(mapSize.X * fit <= world.Size.X + 0.5f && mapSize.Y * fit <= world.Size.Y + 0.5f, "The map opens fitted to the view");

        void Wheel(MouseButton button, Vector2 at) =>
            world._GuiInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at });

        // Once the map is larger than the view, zooming keeps the point under the cursor fixed.
        var cursor = world.Size / 2 + new Vector2(30, -20);
        Wheel(MouseButton.WheelUp, cursor);
        Wheel(MouseButton.WheelUp, cursor);
        Wheel(MouseButton.WheelUp, cursor);
        Expect(world.Zoom > fit, "The wheel zooms in");
        var under = world.ViewToWorld(cursor);
        Wheel(MouseButton.WheelUp, cursor);
        Expect(world.WorldToView(under).DistanceTo(cursor) < 1, "Zoom keeps the point under the cursor");

        for (var i = 0; i < 40; i++)
            Wheel(MouseButton.WheelUp, cursor);
        Expect(world.Zoom <= 1.6f + 0.001f, "Zoom in stops at the limit");

        // Dragging moves the map, but never off screen.
        var before = world.CameraPosition;
        world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(100, 100) });
        world._GuiInput(new InputEventMouseMotion { Position = new Vector2(160, 130), Relative = new Vector2(60, 30) });
        Expect(world.CameraPosition != before, "Dragging pans the map");
        world._GuiInput(new InputEventMouseMotion { Position = new Vector2(9000, 9000), Relative = new Vector2(9000, 9000) });
        Expect(world.CameraPosition.X <= 0.01f && world.CameraPosition.Y <= 0.01f, "The map's corner can't leave the view");
        world._GuiInput(new InputEventMouseMotion { Position = new Vector2(-9000, -9000), Relative = new Vector2(-18000, -18000) });
        var far = world.WorldToView(mapSize);
        Expect(far.X >= world.Size.X - 0.01f && far.Y >= world.Size.Y - 0.01f, "The far corner can't leave the view");
        world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(-9000, -9000) });
        await Shot("map-zoomed");

        // Switching views keeps the camera.
        var position = world.CameraPosition;
        var zoom = world.Zoom;
        game.ShowView(GameScreen.MainView.Village);
        await Frames();
        game.ShowView(GameScreen.MainView.World);
        await Frames();
        Expect(world.CameraPosition == position && world.Zoom == zoom, "The camera survives switching views");

        for (var i = 0; i < 40; i++)
            Wheel(MouseButton.WheelDown, cursor);
        Expect(Mathf.IsEqualApprox(world.Zoom, fit), "Zoom out stops at the fit");
        Expect(mapSize.X * world.Zoom <= world.Size.X + 0.5f, "Fully zoomed out shows the whole map");

        world.FitToView();
        game.ShowView(GameScreen.MainView.Village);
        await Frames();
    }

    private async Task TestSelection()
    {
        var game = Find<GameScreen>();
        var session = game.Session;
        game.ShowView(GameScreen.MainView.World);
        await Frames();
        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var popup = world.GetNode<VillagePopup>("Popup");
        var own = session.State.Villages[1];
        var camp = session.State.Villages.Values.First(v => v.IsBarbarian);

        async Task Click(Vector2 at)
        {
            world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
            world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
            await Frames();
        }

        Vector2 At(Village village) => world.WorldToView(WorldMapView.VillageWorldPosition(village));

        await Click(At(camp));
        Expect(world.SelectedVillageId == camp.Id && popup.Visible, "Clicking a marker selects it");
        Expect(popup.GetNode<Label>("%Name").Text == camp.Name, "The popup names the village");
        var tier = UiContent.TierName(session, camp.BarbarianTier!.Value);
        Expect(popup.GetNode<Label>("%Owner").Text == Format.Dotted(L.T("player.barbarians"), tier), $"The popup shows the owner and tier, got '{popup.GetNode<Label>("%Owner").Text}'");
        Expect(Shows(popup.GetNode<Label>("%Distance").Text, "common.tiles_away"), "The popup shows the distance");
        Expect(popup.GetNode<Button>("%Send").Visible && !popup.GetNode<Button>("%Enter").Visible, "An enemy village offers Send");
        Expect(new Rect2(Vector2.Zero, world.Size).Encloses(popup.GetRect()), "The popup stays inside the view");
        await Shot("map-popup");

        // No garrison numbers anywhere in the popup.
        var texts = new[] { "%Name", "%Owner", "%Distance" }.Select(n => popup.GetNode<Label>(n).Text);
        Expect(!texts.Any(t => t.Contains(UiContent.Units[UnitType.Spearman].Name) || t.Contains("×")), "The popup doesn't reveal the garrison");

        await Click(At(own));
        Expect(world.SelectedVillageId == own.Id, "Clicking another marker moves the selection");
        Expect(popup.GetNode<Button>("%Enter").Visible && !popup.GetNode<Button>("%Send").Visible, "Your own village offers Enter");
        Expect(!popup.GetNode<Label>("%Distance").Visible, "No distance to the village you're in");

        var empty = Enumerable.Range(0, session.State.Map.Tiles.Count)
            .Select(i => new TileCoord(i % session.State.Map.Width, i / session.State.Map.Width))
            .First(c => session.State.Villages.Values.All(v => UnitRules.Distance(v.Position, c) >= 2));
        await Click(world.WorldToView((new Vector2(empty.X, empty.Y) + new Vector2(0.5f, 0.5f)) * WorldMapView.TileSize));
        Expect(world.SelectedVillageId is null && !popup.Visible, "Clicking empty map clears the selection");

        // A drag that starts on a marker is a pan, not a click.
        var start = At(camp);
        world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = start });
        world._GuiInput(new InputEventMouseMotion { Position = start + new Vector2(40, 0), Relative = new Vector2(40, 0) });
        world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = start + new Vector2(40, 0) });
        await Frames();
        Expect(world.SelectedVillageId is null, "Dragging doesn't select");

        // Hit testing still works zoomed in.
        var centre = world.Size / 2;
        for (var i = 0; i < 6; i++)
            world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = At(camp) });
        await Frames();
        await Click(At(camp));
        Expect(world.SelectedVillageId == camp.Id, "Markers can be clicked when zoomed in");
        await Click(At(camp) + new Vector2(VillageMarker.HitRadius + 40, 0));
        Expect(world.SelectedVillageId != camp.Id, "A click beside a marker misses it");
        world.FitToView();
        await Frames();

        // Into the village: from the popup, and by double-click.
        await Click(At(own));
        popup.GetNode<Button>("%Enter").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(game.CurrentView == GameScreen.MainView.Village, "Enter village opens the village view");

        game.ShowView(GameScreen.MainView.World);
        await Frames();
        world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true, Position = At(own) });
        world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = At(own) });
        await Frames();
        Expect(game.CurrentView == GameScreen.MainView.Village, "Double-clicking your village opens it");

        game.ShowView(GameScreen.MainView.World);
        await Frames();
        world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true, Position = At(camp) });
        world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = At(camp) });
        await Frames();
        Expect(game.CurrentView == GameScreen.MainView.World, "Double-clicking an enemy village doesn't enter it");
        world.ClearSelection();
        game.ShowView(GameScreen.MainView.Village);
        await Frames();
    }

    private async Task TestSendFromMap()
    {
        var game = Find<GameScreen>();
        var session = game.Session;
        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var send = game.GetNode<SendPanel>("%Send");
        var camp = session.State.Villages.Values.Where(v => v.IsBarbarian).Last();
        var village = session.State.Villages[1];

        game.ShowView(GameScreen.MainView.Village);
        game.ShowTab(GameScreen.SideTab.Send);
        await Frames();
        send.GetNode<Button>("%PickTarget").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(game.CurrentView == GameScreen.MainView.World, "The form's button opens the map");
        Expect(!send.GetNode<Button>("%PickTarget").Visible, "The button hides once the map is open");

        var at = world.WorldToView(WorldMapView.VillageWorldPosition(camp));
        world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
        await Frames();
        game.ShowTab(GameScreen.SideTab.Village);
        await Frames();
        world.GetNode<VillagePopup>("Popup").GetNode<Button>("%Send").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(send.IsVisibleInTree(), "The popup opens the Send tab");
        Expect(send.GetNode<Label>("%TargetLine").Text.StartsWith(Format.Dotted(camp.Name, L.T("player.barbarians"))), $"The popup sets the target, got '{send.GetNode<Label>("%TargetLine").Text}'");

        // The filled form survives a trip to the village view.
        var amount = send.GetNode<VBoxContainer>("%Units").GetNode(nameof(UnitType.Spearman)).GetNode<SpinBox>("Amount");
        amount.Value = 1;
        game.ShowView(GameScreen.MainView.Village);
        await Frames();
        game.ShowView(GameScreen.MainView.World);
        await Frames();
        Expect((int)amount.Value == 1 && send.GetNode<Label>("%TargetLine").Text.StartsWith(camp.Name), "The form and target survive switching views");
        await Shot("map-send");

        send.GetNode<Button>("%Send").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(session.State.Movements.Any(m => m.ToVillageId == camp.Id), "The attack leaves for the village picked on the map");
        Expect(send.GetNode<Label>("%TargetLine").Text.StartsWith(camp.Name), "The target stays for the next send");

        session.Tick(session.State.Movements.Max(m => m.ArrivalTime) - session.State.ElapsedTime);
        if (session.State.Movements.Count > 0)
            session.Tick(session.State.Movements.Max(m => m.ArrivalTime) - session.State.ElapsedTime);
        world.ClearSelection();
        game.ShowView(GameScreen.MainView.Village);
        await Frames();
    }

    /// <summary>Runs with the rival's attack on the way, and adds movements of the player's own.</summary>
    private async Task TestMovementsOnMap(GameScreen game, Button incoming)
    {
        var session = game.Session;
        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var layer = world.GetNode<MovementLayer>("World/Movements");
        var hostile = session.State.Movements[0];
        var own = session.State.Villages[1];
        var rival = session.State.Villages[2];
        var camp = session.State.Villages.Values.Where(v => v.IsBarbarian).OrderByDescending(v => UnitRules.Distance(v.Position, own.Position)).First();

        session.Send(new SendTroopsCommand(1, camp.Id, MovementType.Raid, new Dictionary<UnitType, int> { [UnitType.Spearman] = 2 }));

        incoming.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(game.CurrentView == GameScreen.MainView.World, "The warning jumps to the map");
        Expect(layer.Entries.Count == 2, $"Own and hostile movements are drawn, got {layer.Entries.Count}");

        var from = WorldMapView.VillageWorldPosition(rival);
        var to = WorldMapView.VillageWorldPosition(own);

        void CheckPosition(string when)
        {
            var entry = layer.Entries.First(e => e.MovementId == hostile.Id);
            var expected = MovementLayer.PositionAt(from, to, hostile, session.State.ElapsedTime);
            Expect(entry.Position.DistanceTo(expected) < 12, $"The marker follows the route {when}: {entry.Position} vs {expected}");
        }

        CheckPosition("at the start");
        var start = layer.Entries.First(e => e.MovementId == hostile.Id).Position;
        Expect(start.DistanceTo(from) < 12, "The marker starts at the origin");

        var time = hostile.ArrivalTime - hostile.DepartureTime;
        session.Tick(time * 0.25);
        await Frames();
        CheckPosition("a quarter of the way");
        session.Tick(time * 0.25);
        await Frames();
        CheckPosition("halfway");
        var midpoint = layer.Entries.First(e => e.MovementId == hostile.Id);
        Expect(midpoint.Position.DistanceTo(from.Lerp(to, 0.5f)) < 12, "Halfway in time is halfway along the route");
        Expect(midpoint.Colour != layer.Entries.First(e => e.MovementId != hostile.Id).Colour, "Hostile and own movements differ in colour");

        // Hovering a moving marker shows what it is and when it arrives.
        var hover = world.GetNode<Label>("HoverLabel");
        var hoverAt = world.WorldToView(midpoint.Position);
        world._GuiInput(new InputEventMouseMotion { Position = hoverAt });
        await Frames();
        Expect(hover.Visible && hover.Text.StartsWith(L.T("movement.from", ("mission", UiContent.MissionName(MovementType.Attack)), ("owner", "Rival"))), $"Hovering shows the mission, got '{hover.Text}'");
        world._GuiInput(new InputEventMouseMotion { Position = new Vector2(1, 1) });
        await Frames();
        Expect(!hover.Visible, "Moving away hides it");
        await Shot("map-movements");

        var returning = session.State.Movements.FirstOrDefault(m => m.Type == MovementType.Return);
        if (returning is not null)
            Expect(layer.Entries.First(e => e.MovementId == returning.Id).Colour != midpoint.Colour, "Returning movements have their own colour");

        // Everything lands: the layer empties.
        session.Tick(time);
        await Frames();
        if (session.State.Movements.Count > 0)
        {
            await Shot("map-returning");
            session.Tick(session.State.Movements.Max(m => m.ArrivalTime) - session.State.ElapsedTime);
            await Frames();
        }
        Expect(layer.Entries.Count == 0, "Markers clear when everyone has landed");

        game.ShowView(GameScreen.MainView.Village);
        await Frames();
    }

    private async Task TestCrowdedMediumMap(GameData data)
    {
        foreach (var child in GetChildren().OfType<GameScreen>())
            child.QueueFree();
        await Frames();

        var settings = new GameSettings(TestDuration, 1, MapSize.Medium, 11);
        var slots = new[] { new PlayerSlot("You", Nation.Valdren, false), new PlayerSlot("Rival", Nation.Karuk, true) };
        var session = new LocalGameSession(GameFactory.CreateMatch(settings, slots, data), data, 1);
        var game = GD.Load<PackedScene>("res://scenes/Game.tscn").Instantiate<GameScreen>();
        game.Init(session);
        AddChild(game);
        await Frames();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        game.ShowView(GameScreen.MainView.World);
        GD.Print($"[uitest] first map open on a medium map took {watch.ElapsedMilliseconds} ms");

        var own = session.State.Villages[1];
        var targets = session.State.Villages.Values.Where(v => v.IsBarbarian).ToList();
        foreach (var target in targets.Take(8))
            session.Send(new SendTroopsCommand(1, target.Id, MovementType.Raid, new Dictionary<UnitType, int> { [UnitType.Spearman] = 1 }));
        session.Tick(5);
        await Frames();

        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var layer = world.GetNode<MovementLayer>("World/Movements");
        Expect(layer.Entries.Count == 8, $"Every movement is drawn, got {layer.Entries.Count}");
        await Shot("map-crowded");

        if (_shotsDir is not null && DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetSize(new Vector2I(1920, 800));
            await Frames(10);
            await Shot("map-crowded-wide");
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(10);
        }

        var at = world.WorldToView(WorldMapView.VillageWorldPosition(targets[^1]));
        world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
        await Frames();
        var popup = world.GetNode<VillagePopup>("Popup");
        Expect(popup.Visible && new Rect2(Vector2.Zero, world.Size).Encloses(popup.GetRect()), "The popup stays inside the view on a medium map");

        session.Tick(TestDuration);
        await Frames();
        await Shot("map-match-over");
    }

    private async Task TestLeaveMatch()
    {
        var game = Find<GameScreen>();
        var session = game.Session;
        var pause = game.GetNode<Overlay>("%PauseOverlay");
        var leave = game.GetNode<Overlay>("%LeaveOverlay");

        game._UnhandledKeyInput(new InputEventKey { Pressed = true, Keycode = Key.P });
        await Frames();
        pause.GetNode<Button>("%Secondary").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(leave.Visible && !pause.Visible, "Leave match asks for confirmation");
        Expect(session.IsPaused, "The match stays paused while confirming");
        await Shot("leave-confirm");

        game._UnhandledKeyInput(new InputEventKey { Pressed = true, Keycode = Key.P });
        await Frames();
        Expect(session.IsPaused && leave.Visible, "P does nothing while confirming");

        leave.GetNode<Button>("%Secondary").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(pause.Visible && !leave.Visible && session.IsPaused, "Keep playing returns to the paused overlay");

        pause.GetNode<Button>("%Secondary").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        leave.GetNode<Button>("%Action").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        var start = Find<StartScreen>();
        Expect(FindAll<GameScreen>(_main).FirstOrDefault() is null, "Leaving closes the match");

        start.GetNode<OptionButton>("%MapSize").Select((int)MapSize.Medium);
        start.GetNode<Button>("%Start").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        var fresh = Find<GameScreen>().Session;
        Expect(!ReferenceEquals(fresh, session) && !fresh.IsPaused && fresh.State.ElapsedTime < 1, "A new match starts fresh after leaving");
        Find<GameScreen>().ShowView(GameScreen.MainView.World);
        await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
        await Shot("map-medium");
        Find<GameScreen>().ShowView(GameScreen.MainView.Village);
        await Frames();
        var medium = fresh.Data.Game.MapSizes[MapSize.Medium];
        Expect(fresh.State.Settings.MapSize == MapSize.Medium && fresh.State.Map.Width == medium.Width && fresh.State.Map.Height == medium.Height, "The chosen map size is used");
    }

    private async Task TestTroops()
    {
        var game = Find<GameScreen>();
        var session = game.Session;
        var village = session.State.Villages[1];
        var panel = Find<BuildingPanel>();
        var troops = game.GetNode<TroopsPanel>("%Troops");
        Node Row(UnitType unit) => troops.GetNode<VBoxContainer>("%Rows").GetNode(unit.ToString());

        // Finish the Wall queued by the tiles test, then build the Barracks through the tile and panel.
        session.Tick(120);
        SelectTile(BuildingType.Barracks);
        await Frames();
        var open = panel.GetNode<Button>("%OpenTraining");
        Expect(open.Visible, "The Barracks offers a way to its training");
        panel.GetNode<Button>("%Upgrade").EmitSignal(BaseButton.SignalName.Pressed);
        session.Tick(BuildingRules.BuildTimeSeconds(session.Data, BuildingType.Barracks, 1) + 1);
        await Frames();
        Expect(village.BuildingLevel(BuildingType.Barracks) == 1, "Barracks is built");

        open.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(troops.IsVisibleInTree(), "The button opens the Troops tab");

        var spearman = Row(UnitType.Spearman);
        var before = village.TroopCount(UnitType.Spearman);
        Expect(spearman.GetNode<Label>("Header/Count").Text == $"× {before}", "Garrison count is shown");
        Expect(Row(UnitType.Catapult).GetNode<Label>("Note").Text == L.T("troops.requires", ("level", "5")), "A locked unit states its requirement");
        Expect(!Row(UnitType.Catapult).GetNode<Control>("Controls").Visible, "A locked unit can't be trained");
        await Shot("troops");

        var train = spearman.GetNode<Button>("Controls/Actions/Train");
        spearman.GetNode<SpinBox>("Controls/Actions/Amount").Value = 2;
        await Frames();
        train.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(village.TrainingQueue.Count == 1 && village.TrainingQueue[0].Count == 2, "Train queues the batch");
        Expect(troops.GetNode<QueuePanel>("%TrainingQueue").GetNode<Label>("%Count").Text == "1 / 2", "The training queue is shown");
        await Shot("training");

        session.Tick(village.TrainingQueue[0].FinishTime - session.State.ElapsedTime);
        await Frames();
        Expect(village.TroopCount(UnitType.Spearman) == before + 2, "The garrison grows when training finishes");
        Expect(spearman.GetNode<Label>("Header/Count").Text == $"× {before + 2}", "The new count is shown");

        spearman.GetNode<SpinBox>("Controls/Actions/Amount").Value = 0;
        await Frames();
        Expect(train.Disabled, "Zero units can't be trained");
        spearman.GetNode<Button>("Controls/Actions/Max").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(!train.Disabled && (int)spearman.GetNode<SpinBox>("Controls/Actions/Amount").Value == TrainPreviewMax(session, UnitType.Spearman), "Max picks the largest affordable batch");

        SelectTile(BuildingType.Woodcutter);
        await Frames();
        Expect(panel.IsVisibleInTree(), "Clicking a tile returns to the Village tab");
    }

    private async Task TestSend()
    {
        var game = Find<GameScreen>();
        var session = game.Session;
        var village = session.State.Villages[1];
        var send = game.GetNode<SendPanel>("%Send");
        var button = send.GetNode<Button>("%Send");
        var reason = send.GetNode<Label>("%Reason");
        var mission = send.GetNode<OptionButton>("%Mission");
        var spearmen = send.GetNode<VBoxContainer>("%Units").GetNode(nameof(UnitType.Spearman));

        game.ShowTab(GameScreen.SideTab.Send);
        await Frames();
        Expect(send.IsVisibleInTree(), "The Send tab opens");
        Expect(mission.ItemCount == 3, "Attack, Raid and Scout are offered, and Reinforce isn't without a second village");
        Expect(button.Disabled && reason.Text == L.T("send.pick_target"), "No target disables Send and says why");
        Expect(send.GetNode<Button>("%PickTarget").Visible, "The form offers a way to the map");
        send.SetTarget(2);
        await Frames();
        var line = send.GetNode<Label>("%TargetLine").Text;
        Expect(line.StartsWith(Format.Dotted(session.State.Villages[2].Name, "Vargas", "")), $"The target line names the village, got '{line}'");
        Expect(button.Disabled && reason.Text == UiContent.RejectionText(CommandRejection.NoTroopsSelected), "No troops selected disables Send and says why");
        Expect(send.GetNode<Label>("%None").Visible, "Nothing on the move yet");

        spearmen.GetNode<Button>("Max").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        var present = village.TroopCount(UnitType.Spearman);
        Expect((int)spearmen.GetNode<SpinBox>("Amount").Value == present, "Max selects the whole garrison");
        Expect(Shows(send.GetNode<Label>("%Arrival").Text, "send.arrives"), "The arrival time is previewed");
        Expect(!send.GetNode<Control>("%CatapultRow").Visible, "No catapult target without catapults");
        await Shot("send");

        mission.Select(1);
        await Frames();
        button.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(village.TroopCount(UnitType.Spearman) == 0, "Sent troops leave the garrison");
        Expect(session.State.Movements.Count == 1 && session.State.Movements[0].Type == MovementType.Raid, "The chosen mission is sent");
        Expect((int)spearmen.GetNode<SpinBox>("Amount").Value == 0, "The form resets after sending");
        var row = send.GetNode<VBoxContainer>("%Rows").GetNode("Moving0");
        Expect(row.GetNode<Label>("Title").Text == L.T("movement.to", ("mission", UiContent.MissionName(MovementType.Raid)), ("target", session.State.Villages[2].Name)), "The movement is listed");
        await Shot("moving");

        session.Tick(session.State.Movements[0].ArrivalTime - session.State.ElapsedTime);
        await Frames();
        Expect(session.State.Reports.Count == 1, "The raid is fought on arrival");
        if (session.State.Movements.Count > 0)
        {
            Expect(row.GetNode<Label>("Title").Text.StartsWith(L.T("movement.returning")), "Survivors are listed as returning");
            session.Tick(session.State.Movements[0].ArrivalTime - session.State.ElapsedTime);
            await Frames();
        }
        Expect(session.State.Movements.Count == 0, "Everyone is home");
        Expect(send.GetNode<Label>("%None").Visible, "The list empties again");
    }

    private async Task TestReports()
    {
        var game = Find<GameScreen>();
        var session = game.Session;
        var tabs = game.GetNode<TabContainer>("%Side");
        var reports = game.GetNode<ReportsPanel>("%Reports");
        var dialog = game.GetNode<ReportDialog>("%ReportDialog");
        var reportsTab = (int)GameScreen.SideTab.Reports;

        await Frames();
        Expect(tabs.GetTabTitle(reportsTab) == L.T("tab.reports_unread", ("count", "1")), $"An unread report is counted, got '{tabs.GetTabTitle(reportsTab)}'");

        game.ShowTab(GameScreen.SideTab.Reports);
        await Frames();
        Expect(tabs.GetTabTitle(reportsTab) == L.T("tab.reports"), "Opening the tab clears the unread count");
        var row = reports.GetNode<VBoxContainer>("%Rows").GetNode<Button>("Report1");
        Expect(row.Text.StartsWith(L.T("report.headline_attacker", ("result", L.T("report.lost")), ("mission", UiContent.MissionName(MovementType.Raid)), ("target", "Vargas"))), $"The lost raid is listed, got '{row.Text}'");
        await Shot("reports");

        row.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(dialog.Visible && dialog.GetNode<Label>("%Result").Text == L.T("report.defeat"), "The report opens as a defeat");
        Expect(dialog.GetNode<VBoxContainer>("%Attacker").GetChildCount() == 2, "The report lists the attacking side");
        Expect(!dialog.GetNode<Label>("%Loot").Visible, "A lost raid shows no loot");
        Expect(dialog.GetNode<Label>("%Powers").Text.Contains("180"), $"The report shows the weighted defence, got '{dialog.GetNode<Label>("%Powers").Text}'");
        await Shot("report-defeat");

        dialog.GetNode<Button>("%Close").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(!dialog.Visible, "Close hides the report");
    }

    /// <summary>Trains an army through the UI, then wins an attack with a catapult aimed at a chosen building.</summary>
    private async Task TestVictory(GameScreen game, Action<BuildingType, int> upgradeTo)
    {
        var session = game.Session;
        var village = session.State.Villages[1];
        var rival = session.State.Villages[2];

        upgradeTo(BuildingType.Barracks, 5);
        session.Tick(60);
        session.Send(new TrainCommand(village.Id, UnitType.Swordsman, 20));
        session.Send(new TrainCommand(village.Id, UnitType.Catapult, 1));
        session.Tick(60);

        var send = game.GetNode<SendPanel>("%Send");
        var units = send.GetNode<VBoxContainer>("%Units");
        game.ShowTab(GameScreen.SideTab.Send);
        await Frames();

        send.SetTarget(2);
        units.GetNode(nameof(UnitType.Swordsman)).GetNode<Button>("Max").EmitSignal(BaseButton.SignalName.Pressed);
        units.GetNode(nameof(UnitType.Catapult)).GetNode<Button>("Max").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(send.GetNode<Control>("%CatapultRow").Visible, "Catapults on an attack can be aimed");

        send.GetNode<OptionButton>("%CatapultTarget").Select(1 + (int)BuildingType.Woodcutter);
        await Frames();
        var mission = send.GetNode<OptionButton>("%Mission");
        mission.Select(1);
        await Frames();
        Expect(send.GetNode<Button>("%Send").Disabled && send.GetNode<Label>("%Reason").Text == UiContent.RejectionText(CommandRejection.CatapultsCannotRaid), "A raid with catapults is explained");
        mission.Select(0);
        await Frames();
        send.GetNode<Button>("%Send").EmitSignal(BaseButton.SignalName.Pressed);
        var woodcutter = rival.BuildingLevel(BuildingType.Woodcutter);
        session.Tick(60);
        await Frames();

        Expect(session.State.Reports.Count == 1 && session.State.Reports[0].AttackerWon, "The attack is won");
        Expect(rival.BuildingLevel(BuildingType.Woodcutter) == woodcutter - 1, "The catapult hits the chosen building");

        var reports = game.GetNode<ReportsPanel>("%Reports");
        game.ShowTab(GameScreen.SideTab.Reports);
        await Frames();
        var row = reports.GetNode<VBoxContainer>("%Rows").GetNode<Button>("Report1");
        Expect(row.Text.StartsWith(L.T("report.headline_attacker", ("result", L.T("report.won")), ("mission", UiContent.MissionName(MovementType.Attack)), ("target", "Rival"))), $"The won attack is listed, got '{row.Text}'");
        row.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        var dialog = game.GetNode<ReportDialog>("%ReportDialog");
        Expect(dialog.GetNode<Label>("%Result").Text == L.T("report.victory"), "The report opens as a victory");
        Expect(dialog.GetNode<Label>("%Loot").Visible, "A won battle shows loot");
        Expect(dialog.GetNode<Label>("%Damage").Text == L.T("report.catapults", ("building", UiContent.Buildings[BuildingType.Woodcutter].Name), ("from", "1"), ("to", "0")), $"The report shows the damage, got '{dialog.GetNode<Label>("%Damage").Text}'");
        await Shot("report-victory");
        dialog.GetNode<Button>("%Close").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
    }

    /// <summary>The defender's view, through the same path an AI uses.</summary>
    private async Task TestBeingAttacked(GameData data)
    {
        var settings = new GameSettings(TestDuration, 1, MapSize.Small);
        var slots = new[] { new PlayerSlot("You", Nation.Valdren, false), new PlayerSlot("Rival", Nation.Karuk, true) };
        var session = new LocalGameSession(GameFactory.CreateMatch(settings, slots, data), data, 1);
        var game = GD.Load<PackedScene>("res://scenes/Game.tscn").Instantiate<GameScreen>();
        game.Init(session);
        AddChild(game);
        await Frames();
        RivalAttacks(session);
        await Frames();

        Expect(session.State.Movements.Count == 1, "The rival sends its garrison");
        var incoming = game.GetNode<ResourceBar>("%ResourceBar").GetNode<Button>("%Incoming");
        Expect(incoming.Visible && Shows(incoming.Text, "bar.incoming"), $"The top bar warns of the attack, got '{incoming.Text}'");

        game.ShowTab(GameScreen.SideTab.Send);
        await Frames();
        var row = game.GetNode<SendPanel>("%Send").GetNode<VBoxContainer>("%Rows").GetNode("Moving0");
        Expect(row.GetNode<Label>("Title").Text == L.T("movement.from", ("mission", UiContent.MissionName(MovementType.Attack)), ("owner", "Rival")), "The hostile movement is listed");
        await Shot("incoming");

        await TestMovementsOnMap(game, incoming);

        await Frames();
        Expect(!incoming.Visible, "The warning clears after the battle");

        game.ShowTab(GameScreen.SideTab.Reports);
        await Frames();
        var report = game.GetNode<ReportsPanel>("%Reports").GetNode<VBoxContainer>("%Rows").GetChildren().OfType<Button>().First(b => b.Text.StartsWith(L.T("report.headline_defender", ("result", L.T("report.won")), ("mission", UiContent.MissionName(MovementType.Attack)), ("attacker", "Rival"))));
        Expect(report.Text.StartsWith(L.T("report.headline_defender", ("result", L.T("report.won")), ("mission", UiContent.MissionName(MovementType.Attack)), ("attacker", "Rival"))), $"The defender's report says they won, got '{report.Text}'");
        report.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        var dialog = game.GetNode<ReportDialog>("%ReportDialog");
        Expect(dialog.GetNode<Label>("%Result").Text == L.T("report.victory"), "A successful defence is a victory");
        await Shot("report-defended");
        dialog.GetNode<Button>("%Close").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();

        // A battle against barbarians, who have no player entry to look up.
        var camp = session.State.Villages.Values.First(v => v.IsBarbarian);
        session.Send(new SendTroopsCommand(1, camp.Id, MovementType.Raid, new Dictionary<UnitType, int> { [UnitType.Spearman] = 1 }));
        session.Tick(session.State.Movements[0].ArrivalTime - session.State.ElapsedTime);
        game.ShowTab(GameScreen.SideTab.Send);
        await Frames();
        game.ShowTab(GameScreen.SideTab.Reports);
        await Frames();
        var barbarian = game.GetNode<ReportsPanel>("%Reports").GetNode<VBoxContainer>("%Rows").GetChildren().OfType<Button>().First(b => b.Text.Contains(RaidOnBarbarians()));
        Expect(barbarian.Text.Contains(RaidOnBarbarians()), $"The barbarian battle is listed, got '{barbarian.Text}'");
        barbarian.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(dialog.Visible && dialog.GetNode<VBoxContainer>("%Defender").GetChild<Label>(0).Text == L.T("report.defending", ("owner", L.T("player.barbarians"))), "The report names the barbarians");
        await Shot("report-barbarian");
    }

    private static int TrainPreviewMax(Valkaris.Core.Hosting.IGameSession session, UnitType unit) =>
        TrainPreview.Evaluate(session.State, session.Data, session.LocalPlayerId, 1, unit, 1).MaxCount;

    private async Task TestFeedback()
    {
        var game = Find<GameScreen>();
        var session = game.Session;
        var toast = game.GetNode<Toast>("%Toast");

        SelectTile(BuildingType.Farm);
        await Frames();
        game.GetNode<BuildingPanel>("%BuildingPanel").GetNode<Button>("%Upgrade").EmitSignal(BaseButton.SignalName.Pressed);
        session.Tick(300);
        await Frames();
        Expect(toast.Visible, "Completing a build shows a toast");
        Expect(toast.GetNode<Label>("Label").Text == L.T("toast.building_done", ("building", UiContent.Buildings[BuildingType.Farm].Name), ("level", "3")), "Toast names the building and level");

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

        var idleAi = new[] { "--dev-idle-ai" };
        Expect(DevOptions.Parse(idleAi, isDebugBuild: true).IdleAi, "The idle AI argument applies in debug builds");
        Expect(!DevOptions.Parse(idleAi, isDebugBuild: false).IdleAi, "The idle AI argument is ignored in release builds");

        var invalid = DevOptions.Parse(new[] { "--dev-speed=abc", "--dev-duration=-5" }, isDebugBuild: true).Apply(settings);
        Expect(invalid == settings, "Invalid dev arguments are ignored");
    }

    private async Task TestMatchEnd()
    {
        var game = Find<GameScreen>();
        var session = game.Session;

        session.Tick(session.State.Settings.MatchDurationSeconds!.Value);
        await Frames();
        var endScreen = game.GetNode<MatchEndScreen>("%MatchEndScreen");
        Expect(endScreen.Visible, "Match end shows the results");

        SelectTile(BuildingType.Woodcutter);
        await Frames();
        var panel = game.GetNode<BuildingPanel>("%BuildingPanel");
        Expect(panel.GetNode<Button>("%Upgrade").Text == L.T("upgrade.unavailable"), "A finished match offers no upgrade");
        Expect(!panel.GetNode<Label>("%Effect").Text.Contains(L.T("common.level", ("level", "0"))), "A finished match shows no next level");
        await Shot("match-over");

        game._UnhandledKeyInput(new InputEventKey { Pressed = true, Keycode = Key.P });
        Expect(!session.IsPaused, "A finished match cannot be paused");

        endScreen.GetNode<Button>("%Back").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Find<StartScreen>().GetNode<Button>("%Start").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        var fresh = Find<GameScreen>().Session;
        Expect(!ReferenceEquals(fresh, session), "Starting again creates a new session");
        Expect(fresh.State.ElapsedTime < 1 && !fresh.State.IsMatchOver, "The new match starts fresh");

        await TestLevelLimits();
    }

    /// <summary>Plays a very fast real match through commands to reach the states near the level limits.</summary>
    private async Task TestLevelLimits()
    {
        var data = Find<GameScreen>().Session.Data;
        _main.QueueFree();
        await Frames();

        var settings = new GameSettings(1_000_000, 1000, MapSize.Small);
        var slots = new[] { new PlayerSlot("You", Nation.Valdren, false), new PlayerSlot("Rival", Nation.Karuk, true) };
        var session = new LocalGameSession(GameFactory.CreateMatch(settings, slots, data), data, 1);
        var game = GD.Load<PackedScene>("res://scenes/Game.tscn").Instantiate<GameScreen>();
        game.Init(session);
        AddChild(game);
        await Frames();

        var village = session.State.Villages[1];
        var view = game.GetNode<VillageView>("%VillageView");
        var panel = game.GetNode<BuildingPanel>("%BuildingPanel");
        var upgrade = panel.GetNode<Button>("%Upgrade");
        var reason = panel.GetNode<Label>("%Reason");

        void UpgradeTo(BuildingType building, int level)
        {
            for (var guard = 0; village.BuildingLevel(building) < level && guard < 50; guard++)
            {
                session.Send(new BuildCommand(village.Id, building));
                session.Tick(60);
            }
            Expect(village.BuildingLevel(building) == level, $"{building} reaches level {level}");
        }

        async Task Select(BuildingType building)
        {
            view.GetNode<BuildingTile>(building.ToString()).EmitSignal(BaseButton.SignalName.Pressed);
            await Frames();
        }

        // Level 8 costs more stone than a level 1 Warehouse holds, however long the player saves.
        UpgradeTo(BuildingType.MainBuilding, 7);
        UpgradeTo(BuildingType.Woodcutter, 7);
        session.Tick(60);
        await Select(BuildingType.Woodcutter);
        Expect(upgrade.Disabled && reason.Text == L.T("upgrade.warehouse_small"), $"Unaffordable cost points to the Warehouse, got '{reason.Text}'");
        await Shot("warehouse-hint");

        // With storage out of the way, the Main Building table blocks the next level until it grows.
        var limit = BuildingRules.MaxOtherBuildingLevel(data, village.BuildingLevel(BuildingType.MainBuilding));
        UpgradeTo(BuildingType.Warehouse, limit);
        UpgradeTo(BuildingType.Quarry, limit);
        session.Tick(60);
        await Select(BuildingType.Quarry);
        Expect(reason.Text == UiContent.RejectionText(CommandRejection.MainBuildingLevelTooLow), $"Main Building limit is explained, got '{reason.Text}'");

        // Upgrades queued up to the max level, in a full queue, must not break the panel.
        UpgradeTo(BuildingType.MainBuilding, 8);
        session.Tick(60);
        session.Send(new BuildCommand(village.Id, BuildingType.MainBuilding));
        session.Send(new BuildCommand(village.Id, BuildingType.MainBuilding));
        await Select(BuildingType.MainBuilding);
        Expect(upgrade.Text == L.T("upgrade.max") && upgrade.Disabled, $"Queued to max shows max level, got '{upgrade.Text}'");

        session.Tick(60);
        await Frames();
        Expect(village.BuildingLevel(BuildingType.MainBuilding) == 10, "Main Building reaches max level");
        Expect(upgrade.Text == L.T("upgrade.max") && upgrade.Disabled && !reason.Visible, "Max level needs no reason text");
        await Shot("max-level");

        await TestVictory(game, UpgradeTo);

        if (_shotsDir is not null && DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetSize(new Vector2I(1920, 800));
            await Frames(10);
            await Shot("wide");
            DisplayServer.WindowSetSize(new Vector2I(1024, 768));
            await Frames(10);
            await Shot("tall");
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(10);
        }

        game.QueueFree();
        await Frames();
        await TestBeingAttacked(data);
        await TestCrowdedMediumMap(data);
        await TestSwitchingVillages(data);
        await TestManyVillages(data);
        await TestReinforce(data);
        await TestTransfers(data);
        await TestStrayReturnDoesNotWarn(data);
        await TestReportsOfLoyalty(data);
        await TestScouting(data);
        await TestSurrender(data);
        await TestFounding(data);
        await TestCapitalChoice(data);
        await TestCapitalTimeout(data);
        await TestWatchingAfterElimination(data);
        await TestMatchResults(data);
        await TestGodMode(data);
        await TestLiveAi();
    }

    /// <summary>Real AI opponents through the real start screen, all the way to the results.</summary>
    private async Task TestLiveAi()
    {
        foreach (var child in GetChildren().OfType<GameScreen>())
            child.QueueFree();
        await Frames();

        // Earlier steps have finished with their own Main.
        _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        AddChild(_main);
        await Frames();

        var start = Find<StartScreen>();
        start.GetNode<OptionButton>("%Opponents").Select(2);
        start.GetNode<OptionButton>("%Difficulty").Select(Array.IndexOf(Enum.GetValues<AiDifficulty>(), AiDifficulty.Hard));
        start.GetNode<OptionButton>("%Speed").Select(start.GetNode<OptionButton>("%Speed").ItemCount - 1);
        Press(start.GetNode<Button>("%Start"));
        await Frames();

        var game = Find<GameScreen>();
        var session = game.Session;
        game.ShowView(GameScreen.MainView.World);
        await Frames();

        var ticks = new List<double>();
        var watch = new System.Diagnostics.Stopwatch();
        var mapShot = false;
        while (!session.State.IsMatchOver)
        {
            // Long enough to hold a decision from every AI, so this is a worst case for one frame at every stage of a match.
            watch.Restart();
            session.Tick(session.Data.Ai.Difficulties[AiDifficulty.Hard].DecisionSeconds / session.State.Settings.SpeedMultiplier);
            ticks.Add(watch.Elapsed.TotalMilliseconds);
            session.Tick(60);
            await Frames(1);

            if (!mapShot && session.State.ElapsedTime > session.State.Settings.MatchDurationSeconds!.Value * 0.8)
            {
                mapShot = true;
                await Shot("live-ai-map");
            }
        }

        await Frames();
        // One runtime pause in a run lands on any tick, and isn't the AI's thinking: the same spike shows at any AI cost.
        var ordered = ticks.OrderDescending().ToList();
        GD.Print($"[uitest] slowest ticks holding every AI's decision: {ordered[0]:0.00} ms, then {ordered[1]:0.00} ms");
        Expect(ordered[1] < 16, $"AI thinking fits within a frame, second slowest {ordered[1]:0.00} ms");
        Expect(session.State.Reports.Any(r => r.AttackerId != 1), "The opponents really played");
        Expect(session.State.Players.Values.Where(p => p.IsAi).All(p => p.Difficulty == AiDifficulty.Hard), "The opponents are Hard");
        Expect(session.State.ScoutReports.Any(r => r.SenderId != 1 && r.Sent.Keys.All(u => u == UnitType.Scout)), "The opponents really scouted, with Scouts");
        Expect(session.State.Villages.Values.Any(v => v.OwnerId != 1 && v.BuildingLevel(BuildingType.Watchtower) > 0), "The opponents really built a Watchtower");
        var end = game.GetNode<MatchEndScreen>("%MatchEndScreen");
        Expect(end.Visible, "A live AI match reaches the results");
        await Shot("live-ai-end");
        Press(end.GetNode<Button>("%Back"));
        await Frames();
        Expect(Find<StartScreen>() is not null, "Back to start works after a live match");

        await TestLiveUnlimited();
    }

    /// <summary>An Unlimited match against AIs ends by itself, through the real UI, once the player is out.</summary>
    private async Task TestLiveUnlimited()
    {
        var start = Find<StartScreen>();
        var durations = start.GetNode<OptionButton>("%Duration");
        durations.Select(durations.ItemCount - 1);
        start.GetNode<OptionButton>("%Opponents").Select(2);
        start.GetNode<OptionButton>("%Difficulty").Select(Array.IndexOf(Enum.GetValues<AiDifficulty>(), AiDifficulty.Hard));
        start.GetNode<OptionButton>("%Speed").Select(start.GetNode<OptionButton>("%Speed").ItemCount - 1);
        Press(start.GetNode<Button>("%Start"));
        await Frames();

        var game = Find<GameScreen>();
        var session = game.Session;
        Expect(session.State.Settings.MatchDurationSeconds is null, "The live match has no time limit");
        session.Tick(60);
        session.Send(new SurrenderCommand());
        await Frames();
        Press(game.GetNode<Overlay>("%EliminatedOverlay").GetNode<Button>("%Action"));
        await Frames();

        // About four hours of match time at this game speed: far past what the simulation shows.
        for (var i = 0; i < 400 && !session.State.IsMatchOver; i++)
        {
            session.Tick(36);
            await Frames(1);
        }

        await Frames();
        Expect(session.State.IsMatchOver, $"The AIs finish an Unlimited match by themselves, at {session.State.ElapsedTime:0} s");
        var end = game.GetNode<MatchEndScreen>("%MatchEndScreen");
        Expect(end.Visible && end.GetNode<Label>("%Title").Text == L.T("report.defeat"), "The player who left has lost");
        Expect(session.State.Outcome is { WinnerIds.Count: 1 }, "One player is left");
        await Shot("live-unlimited-end");
        Press(end.GetNode<Button>("%Back"));
        await Frames();
    }

    private static int LongestTimed(OptionButton durations) => durations.ItemCount - 2;

    private async Task TestUnlimitedFromStart()
    {
        var start = Find<StartScreen>();
        var durations = start.GetNode<OptionButton>("%Duration");
        Expect(durations.GetItemText(durations.ItemCount - 1) == L.T("start.unlimited"), "Unlimited is the last duration");
        Expect(durations.ItemCount == 4 && durations.GetItemText(0) == L.N("common.minutes", 30), $"Durations start at 30 minutes, got {durations.GetItemText(0)}");
        durations.Select(durations.ItemCount - 1);
        start.GetNode<Button>("%Start").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();

        var game = Find<GameScreen>();
        Expect(game.Session.State.Settings.MatchDurationSeconds is null, "The match has no time limit");
        game.Session.Tick(90);
        await Frames();
        var clock = Find<ResourceBar>().GetNode<Label>("%Clock").Text;
        Expect(clock == Format.Time(game.Session.State.ElapsedTime), $"The top bar counts up, got '{clock}'");
        await Shot("unlimited");

        game.Session.SetPaused(true);
        await Frames();
        Press(game.GetNode<Overlay>("%PauseOverlay").GetNode<Button>("%Secondary"));
        await Frames();
        Press(game.GetNode<Overlay>("%LeaveOverlay").GetNode<Button>("%Action"));
        await Frames();
    }

    /// <summary>Every number of opponents at every difficulty, through the real start screen.</summary>
    private async Task TestOpponentChoices()
    {
        var levels = Enum.GetValues<AiDifficulty>();
        foreach (var opponents in new[] { 1, 2, 3 })
        {
            foreach (var level in levels)
            {
                var start = Find<StartScreen>();
                var count = start.GetNode<OptionButton>("%Opponents");
                var difficulty = start.GetNode<OptionButton>("%Difficulty");
                Expect(count.ItemCount == 3, $"1 to 3 opponents are offered, got {count.ItemCount}");
                Expect(difficulty.GetItemText(difficulty.Selected) == AiDifficulty.Normal.ToString(), "Normal is the default difficulty");
                count.Select(opponents - 1);
                difficulty.Select(Array.IndexOf(levels, level));
                Press(start.GetNode<Button>("%Start"));
                await Frames();

                var game = Find<GameScreen>();
                var players = game.Session.State.Players.Values.OrderBy(p => p.Id).ToList();
                Expect(players.Count == opponents + 1, $"{opponents} opponents means {opponents + 1} players, got {players.Count}");
                Expect(!players[0].IsAi && players[0].Difficulty is null, "The first seat is the human");
                Expect(players.Skip(1).All(p => p.IsAi && p.Difficulty == level), $"Opponents are {level} AI");
                Expect(players.Skip(1).Select(p => p.Name).Distinct().Count() == opponents, "Opponents have different names");

                game.GetNode<ResourceBar>("%ResourceBar");
                game.ShowView(GameScreen.MainView.World);
                await Frames();
                game.ExitToStart();
                await Frames();
            }
        }
    }

    /// <summary>The rival sends everything it has at your capital, through the same path as any AI command.</summary>
    private void RivalAttacks(LocalGameSession session)
    {
        var rival = session.State.Villages.Values.Where(v => v.OwnerId == 2).MaxBy(v => v.Troops.Values.Sum())!;
        var troops = rival.Troops.Where(t => t.Value > 0).ToDictionary(t => t.Key, t => t.Value);
        Expect(session.SendAs(2, new SendTroopsCommand(rival.Id, 1, MovementType.Attack, troops)).Success, "The rival's attack is accepted");
    }

    private async Task<(GameScreen Game, LocalGameSession Session)> StartMatch(GameData data, string? scenario, int players = 2, bool rivalAttacks = false, double duration = TestDuration)
    {
        foreach (var child in GetChildren().OfType<GameScreen>())
            child.QueueFree();
        await Frames();

        var slots = Enumerable.Range(1, players)
            .Select(i => new PlayerSlot(i == 1 ? "You" : i == 2 ? "Rival" : $"Other {i}", Nation.Valdren, i > 1))
            .ToArray();
        var state = GameFactory.CreateMatch(new GameSettings(duration, 1, MapSize.Small, 11), slots, data);
        if (scenario is not null)
            Expect(Valkaris.Core.Dev.DevScenario.Apply(state, data, scenario), $"Scenario {scenario} exists");

        var session = new LocalGameSession(state, data, 1);
        var game = GD.Load<PackedScene>("res://scenes/Game.tscn").Instantiate<GameScreen>();
        game.Init(session);
        AddChild(game);
        await Frames();
        if (rivalAttacks)
            RivalAttacks(session);
        return (game, session);
    }

    private static void Click(WorldMapView world, Vector2 at)
    {
        world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private static void PressKey(GameScreen game, Key key) =>
        game._UnhandledKeyInput(new InputEventKey { Pressed = true, Keycode = key });

    private static void Press(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);

    /// <summary>Runs until nothing is on the way, or the match ends.</summary>
    private async Task RunUntilArrived(LocalGameSession session)
    {
        while (session.State.Movements.Count > 0 && !session.State.IsMatchOver)
            session.Tick(session.State.Movements.Min(m => m.ArrivalTime) - session.State.ElapsedTime + 0.001);
        await Frames();
    }

    private async Task TestSwitchingVillages(GameData data)
    {
        var (game, session) = await StartMatch(data, null);
        var strip = game.GetNode<VillageStrip>("%VillageStrip");
        Expect(!strip.Visible, "The strip is hidden with one village");
        PressKey(game, Key.E);
        Expect(game.CurrentVillageId == 1, "Cycling does nothing with one village");

        (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.Villages);
        strip = game.GetNode<VillageStrip>("%VillageStrip");
        await Frames();
        var own = UiContent.OwnVillages(session);
        var chips = strip.GetChildren().OfType<Button>().ToList();
        Expect(strip.Visible && chips.Count == 3, $"One chip per village, got {chips.Count}");
        Expect(chips[0].ButtonPressed && !chips[1].ButtonPressed, "The current village's chip is pressed");
        Expect(chips[0].Icon is not null && chips[1].Icon is null, "The capital's chip has the star");
        await Shot("villages-strip");

        Press(chips[1]);
        await Frames();
        Expect(game.CurrentVillageId == own[1].Id, "Clicking a chip switches village");
        Expect(chips[1].ButtonPressed && !chips[0].ButtonPressed, "The chips follow the switch");
        Expect(game.CurrentView == GameScreen.MainView.Village, "Switching keeps the view");

        PressKey(game, Key.E);
        Expect(game.CurrentVillageId == own[2].Id, "E moves to the next village");
        PressKey(game, Key.E);
        Expect(game.CurrentVillageId == own[0].Id, "E wraps around");
        PressKey(game, Key.Q);
        Expect(game.CurrentVillageId == own[2].Id, "Q moves to the previous village");

        var send = game.GetNode<SendPanel>("%Send");
        game.ShowTab(GameScreen.SideTab.Send);
        Press(chips[1]);
        await Frames();
        Expect(send.GetNode<Label>("%FromLine").Text == L.T("send.from", ("village", own[1].Name)), "The Send form names the village");
        var amount = send.GetNode<SpinBox>("%Units/Swordsman/Amount");
        amount.Value = 50;
        await Frames();
        Press(chips[0]);
        await Frames();
        Expect(amount.Value == 0, "Switching clears the amounts");
        Expect(game.GetNode<TabContainer>("%Side").CurrentTab == (int)GameScreen.SideTab.Send, "Switching keeps the tab");

        amount.GetLineEdit().GrabFocus();
        await Frames();
        var typed = new InputEventKey { Pressed = true, Keycode = Key.E, Unicode = 'e' };
        var before = game.CurrentVillageId;
        GetViewport().PushInput(typed);
        await Frames();
        Expect(game.CurrentVillageId == before, "E does not switch while typing in a number field");
        amount.GetLineEdit().ReleaseFocus();

        game.ShowView(GameScreen.MainView.World);
        await Frames();
        var world = game.GetNode<WorldMapView>("%WorldMapView");
        for (var i = 0; i < 12; i++)
            world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = world.Size / 2 });
        await Frames();
        Press(chips[2]);
        await Frames();
        var centred = world.WorldToView(WorldMapView.VillageWorldPosition(own[2]));
        Expect(new Rect2(Vector2.Zero, world.Size).HasPoint(centred), "Switching on the map brings the village into view");
        Expect(game.CurrentView == GameScreen.MainView.World, "Switching keeps the map open");
        await Shot("villages-map");
    }

    /// <summary>The strip wraps rather than overflowing, and nothing is pushed off the window at any size.</summary>
    private async Task TestManyVillages(GameData data)
    {
        var (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.Empire);
        await Frames();
        var strip = game.GetNode<VillageStrip>("%VillageStrip");
        Expect(strip.GetChildren().OfType<Button>().Count() == UiContent.OwnVillages(session).Count, "A chip for every village");
        foreach (var tab in Enum.GetValues<GameScreen.SideTab>())
        {
            game.ShowTab(tab);
            await Frames();
            Expect(game.GetNode<Control>("Layout").Position.Y >= 0 && game.GetNode<Control>("Layout").Size.Y <= GetViewportRect().Size.Y + 0.5f, $"The {tab} tab fits the window");
        }

        game.ShowTab(GameScreen.SideTab.Send);
        game.ShowView(GameScreen.MainView.World);
        await Frames();
        Expect(game.GetNode<SendPanel>("%Send").GetNode<Label>("%None").Size.Y > 0, "The empty list of movements is readable");
        game.ShowTab(GameScreen.SideTab.Village);
        await Frames();
        Expect(game.GetNode<BuildingPanel>("%BuildingPanel").Position.Y >= game.GetNode<Label>("%VillageInfo").GetRect().End.Y, "The Village tab stacks its panels");
        game.ShowTab(GameScreen.SideTab.Send);
        await Frames();
        if (_shotsDir is not null && DisplayServer.GetName() != "headless")
        {
            await Shot("empire-1280");
            foreach (var (size, name) in new[] { (new Vector2I(1024, 640), "empire-1024"), (new Vector2I(1920, 800), "empire-wide") })
            {
                DisplayServer.WindowSetSize(size);
                await Frames(10);
                Expect(game.GetNode<Control>("Layout").Size.Y <= GetViewportRect().Size.Y + 0.5f, $"The layout fits at {name}");
                await Shot(name);
                game.ShowTab(GameScreen.SideTab.Village);
                game.ShowView(GameScreen.MainView.Village);
                await Frames();
                Expect(game.GetNode<Control>("Layout").Size.Y <= GetViewportRect().Size.Y + 0.5f, $"The Village tab fits at {name}");
                await Shot($"{name}-village");
                game.ShowTab(GameScreen.SideTab.Send);
                game.ShowView(GameScreen.MainView.World);
                await Frames();
            }
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(10);
        }
    }

    private async Task TestReinforce(GameData data)
    {
        var (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.Villages);
        var own = UiContent.OwnVillages(session);
        game.SwitchVillage(own[1].Id);
        game.ShowView(GameScreen.MainView.World);
        await Frames();

        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var popup = world.GetNode<VillagePopup>("Popup");
        Click(world, world.WorldToView(WorldMapView.VillageWorldPosition(own[2])));
        await Frames();
        Expect(popup.GetNode<Button>("%Reinforce").Visible, "Your other village offers Reinforce");
        Expect(popup.GetNode<Button>("%Enter").Visible, "Your other village offers Enter");
        Expect(!popup.GetNode<Button>("%Send").Visible, "Your own village offers no attack");
        await Shot("reinforce-popup");

        Press(popup.GetNode<Button>("%Reinforce"));
        await Frames();
        var send = game.GetNode<SendPanel>("%Send");
        Expect(game.GetNode<TabContainer>("%Side").CurrentTab == (int)GameScreen.SideTab.Send, "Reinforce opens the Send tab");
        Expect(send.GetNode<OptionButton>("%Mission").GetItemText(0) == UiContent.MissionName(MovementType.Reinforce), "The mission is Reinforce");

        send.GetNode<SpinBox>("%Units/Swordsman/Amount").Value = 40;
        await Frames();
        var before = own[2].TroopCount(UnitType.Swordsman);
        Press(send.GetNode<Button>("%Send"));
        await Frames();
        Expect(session.State.Movements.Any(m => m.Type == MovementType.Reinforce), "Reinforcements leave");
        await RunUntilArrived(session);
        Expect(own[2].TroopCount(UnitType.Swordsman) == before + 40, "Reinforcements join the garrison");

        Click(world, world.WorldToView(WorldMapView.VillageWorldPosition(own[1])));
        await Frames();
        Expect(!popup.GetNode<Button>("%Reinforce").Visible, "The current village can't reinforce itself");
    }

    private async Task TestStrayReturnDoesNotWarn(GameData data)
    {
        var (game, _) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.StrayReturn);
        await Frames();
        Expect(!game.GetNode<ResourceBar>("%ResourceBar").GetNode<Button>("%Incoming").Visible, "An enemy's troops walking home don't warn");
    }

    private async Task TestTransfers(GameData data)
    {
        var (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.Villages);
        var own = UiContent.OwnVillages(session);
        var from = own[1];
        var to = own[2];
        game.SwitchVillage(from.Id);

        // From the Marketplace, with no target yet.
        var panel = game.GetNode<BuildingPanel>("%BuildingPanel");
        SelectGameTile(game, BuildingType.Marketplace);
        await Frames();
        var openForm = panel.GetNode<Button>("%OpenTransfer");
        Expect(openForm.Visible, "The Marketplace offers Send resources");
        Expect(panel.GetNode<Label>("%Effect").Text.Contains(L.T("effect.carriers_free", ("free", "6"), ("capacity", Format.Whole(data.Villages.Carriers.Capacity)))), $"The Marketplace shows free carriers, got '{panel.GetNode<Label>("%Effect").Text}'");
        await Shot("marketplace");
        Press(openForm);
        await Frames();
        var send = game.GetNode<SendPanel>("%Send");
        var button = send.GetNode<Button>("%Send");
        var reason = send.GetNode<Label>("%Reason");
        var arrival = send.GetNode<Label>("%Arrival");
        var warning = send.GetNode<Label>("%Warning");
        Expect(game.GetNode<TabContainer>("%Side").CurrentTab == (int)GameScreen.SideTab.Send, "The Marketplace opens the Send tab");
        Expect(send.GetNode<Control>("%ResourceRows").Visible && !send.GetNode<Control>("%Units").Visible, "The form shows resources instead of troops");
        Expect(button.Disabled && reason.Text == L.T("send.pick_own"), $"With no target the form says why, got '{reason.Text}'");

        // From the map popup.
        game.ShowView(GameScreen.MainView.World);
        await Frames();
        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var popup = world.GetNode<VillagePopup>("Popup");
        Click(world, world.WorldToView(WorldMapView.VillageWorldPosition(to)));
        await Frames();
        Expect(popup.GetNode<Button>("%SendResources").Visible, "Your other village offers Send resources");
        await Shot("transfer-popup");
        Press(popup.GetNode<Button>("%SendResources"));
        world.ClearSelection();
        await Frames();
        Expect(send.GetNode<OptionButton>("%Mission").GetItemText(send.GetNode<OptionButton>("%Mission").Selected) == UiContent.MissionName(MovementType.Transfer), "The popup selects the resource form");
        Expect(send.GetNode<Label>("%TargetLine").Text.StartsWith(to.Name), "The popup sets the target");

        var wood = send.GetNode<SpinBox>("%ResourceRows/Wood/Amount");
        var stone = send.GetNode<SpinBox>("%ResourceRows/Stone/Amount");
        Expect(button.Disabled && reason.Text == UiContent.RejectionText(CommandRejection.NoResourcesSelected), "An empty load says why");

        // Max respects stock and carriers.
        Press(send.GetNode<Button>("%ResourceRows/Wood/Max"));
        var capacity = data.Villages.Carriers.Capacity;
        var free = CarrierRules.Free(session.State, data, from);
        Expect(wood.Value == Math.Min(Math.Floor(from.Resources.Wood), free * capacity), $"Max respects stock and carriers, got {wood.Value}");
        Press(send.GetNode<Button>("%ResourceRows/Stone/Max"));
        Expect(wood.Value + stone.Value <= free * capacity, "Max leaves room only for what carriers can hold");
        wood.Value = 0;
        stone.Value = 0;

        // Too much for the carriers. Stock is lifted through time, since the form caps amounts at stock.
        session.Tick(400);
        await Frames();
        wood.Value = Math.Floor(from.Resources.Wood);
        stone.Value = Math.Floor(from.Resources.Stone);
        await Frames();
        Expect(wood.Value + stone.Value > free * capacity, "This load is more than the carriers can hold");
        Expect(button.Disabled && reason.Text == L.T("send.needs_carriers", ("needed", Format.Whole(CarrierRules.Needed(data, (int)(wood.Value + stone.Value)))), ("free", Format.Whole(free))), $"Too large a load names the carriers, got '{reason.Text}'");
        await Shot("transfer-too-many");

        // A real send, with the preview matching the shared rules.
        wood.Value = capacity + 1;
        stone.Value = 0;
        await Frames();
        var command = new SendResourcesCommand(from.Id, to.Id, capacity + 1, 0, 0);
        var option = SendResourcesPreview.Evaluate(session.State, data, 1, command);
        Expect(!button.Disabled, "A valid load can be sent");
        Expect(arrival.Text == L.T("send.carriers_preview", ("used", Format.Whole(option.CarriersUsed)), ("free", Format.Whole(option.CarriersFree)), ("time", Format.Time(option.DurationSeconds))), $"The preview matches the rules, got '{arrival.Text}'");
        Expect(option.CarriersUsed == 2, "One over a carrier's load needs two");
        await Shot("transfer-form");

        var stock = from.Resources.Wood;
        Press(button);
        await Frames();
        var movement = session.State.Movements.Single(m => m.Type == MovementType.Transfer);
        Expect(from.Resources.Wood < stock, "Sending takes the resources");
        Expect(wood.Value == 0, "Sending clears the form");
        var layer = world.GetNode<MovementLayer>("World/Movements");
        Expect(layer.Entries.Any(e => Shows(e.Description.Split("  ·  ")[0], "movement.carrying")), "The transfer is on the map");
        Expect(send.GetNode<Label>("%Rows/Moving0/Title").Text == L.T("movement.carrying", ("amount", Format.Whole(capacity + 1)), ("target", to.Name)), "The transfer is in the list");
        await Shot("transfer-moving");

        // The overflow warning.
        var room = BuildingRules.VillageCapacity(data, to) - to.Resources.Stone;
        var fill = Math.Floor(Math.Min(from.Resources.Stone, (free - 2) * capacity));
        Expect(fill > room, $"This load overflows the target: fill {fill}, room {room}");
        stone.Value = fill;
        await Frames();
        Expect(warning.Visible && Shows(warning.Text, "send.warning"), $"An overflow warns, got '{warning.Text}'");
        Expect(!button.Disabled, "A warning does not block sending");
        await Shot("transfer-warning");
        stone.Value = 0;
        await Frames();
        Expect(!warning.Visible, "No warning without overflow");

        // Delivery, then the carriers' walk home.
        ResourcesDelivered? delivery = null;
        session.EventRaised += e => delivery ??= e as ResourcesDelivered;
        session.Tick(movement.ArrivalTime - session.State.ElapsedTime);
        await Frames();
        Expect(delivery is not null && delivery.Delivered + delivery.Lost == movement.Loot, "The whole load is accounted for on delivery");
        Expect(session.State.Movements.Any(m => m.Type == MovementType.CarrierReturn), "The carriers walk home");
        Expect(send.GetNode<Label>("%Rows/Moving0/Title").Text == L.T("movement.carriers_returning"), "The list shows them returning");
        await RunUntilArrived(session);
        Expect(CarrierRules.Free(session.State, data, from) == free, "The carriers are free again");

        // Sending to itself, and to a rival.
        send.SetTarget(from.Id, sendResources: true);
        await Frames();
        wood.Value = 10;
        await Frames();
        Expect(button.Disabled && reason.Text == UiContent.RejectionText(CommandRejection.SameVillage), "The same village says why");

        // Another player's transfers stay hidden.
        var strike = session.State.Villages.Values.Where(v => v.OwnerId == 2).MaxBy(v => v.Troops.Values.Sum())!;
        var drawn = layer.Entries.Count;
        Expect(session.SendAs(2, new SendResourcesCommand(strike.Id, 2, 10, 0, 0)).Success, "The rival can send a transfer");
        await Frames();
        Expect(layer.Entries.Count == drawn, "A rival's transfer isn't drawn");
        Expect(!game.GetNode<ResourceBar>("%ResourceBar").GetNode<Button>("%Incoming").Visible, "A rival's transfer doesn't warn");

        // Switching to a troop mission still works.
        Click(world, world.WorldToView(WorldMapView.VillageWorldPosition(to)));
        await Frames();
        Press(popup.GetNode<Button>("%Reinforce"));
        await Frames();
        Expect(send.GetNode<Control>("%Units").Visible, "Reinforce shows the troops again");
        Expect(button.Text == L.T("send.troops"), "The button follows the form");
    }

    private static void SelectGameTile(GameScreen game, BuildingType building) =>
        game.GetNode<VillageView>("%VillageView").GetNode<BuildingTile>(building.ToString()).EmitSignal(BaseButton.SignalName.Pressed);

    private async Task TrainAndWait(LocalGameSession session, int player, int count, UnitType unit = UnitType.Spearman)
    {
        Expect(session.SendAs(player, new TrainCommand(player, unit, count)).Success, $"More {unit} are ordered");
        var village = session.State.Villages[player];
        session.Tick(village.TrainingQueue[^1].FinishTime - session.State.ElapsedTime + 0.001);
        await Frames();
    }

    private async Task TestScouting(GameData data)
    {
        var (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.Scouting, duration: 36000);
        var state = session.State;
        var send = game.GetNode<SendPanel>("%Send");
        var mission = send.GetNode<OptionButton>("%Mission");
        var intel = send.GetNode<Label>("%Intel");
        var rule = send.GetNode<Label>("%ScoutRule");
        var reports = game.GetNode<ReportsPanel>("%Reports");
        var dialog = game.GetNode<ReportDialog>("%ReportDialog");
        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var home = state.Villages[1];
        var rival = state.Villages[2];
        var unitRows = send.GetNode<VBoxContainer>("%Units");
        var scoutRow = unitRows.GetNode<Control>(nameof(UnitType.Scout));

        game.ShowTab(GameScreen.SideTab.Send);
        send.SetTarget(2);
        await Frames();
        mission.Select(2);
        await Frames();
        Expect(!intel.Visible, "No intel before any scouting");
        Expect(rule.Visible && rule.Text == L.T("send.scout_rule"), $"The form states the scouting rule, got '{rule.Text}'");
        Expect(scoutRow.Visible && !unitRows.GetNode<Control>(nameof(UnitType.Spearman)).Visible, "A Scout mission offers only Scouts");
        mission.Select(0);
        await Frames();
        Expect(!scoutRow.Visible && unitRows.GetNode<Control>(nameof(UnitType.Spearman)).Visible, "An attack doesn't offer Scouts");
        mission.Select(2);
        await Frames();
        scoutRow.GetNode<SpinBox>("Amount").Value = 1;
        await Frames();
        await Shot("scout-form");
        Press(send.GetNode<Button>("%Send"));
        await Frames();
        var scoutTrip = state.Movements.Single();
        Expect(scoutTrip.Type == MovementType.Scout, "The form sends a Scout mission");
        Expect(UiContent.IsVisibleMovement(session, scoutTrip), "The sender sees their scouts");

        await RunUntilArrived(session);
        Expect(state.ScoutReports.Single().Succeeded, "A village with no Scouts is always scouted");
        Expect(state.Reports.Count == 0, "Scouting fights no battle");
        game.ShowTab(GameScreen.SideTab.Send);
        send.SetTarget(2);
        await Frames();
        Expect(intel.Visible && intel.Text.Contains(L.N("intel.troops", 5)) && intel.Text.Contains(L.T("intel.wall", ("level", "2"))), $"The form shows the latest intel, got '{intel.Text}'");
        await Shot("scout-intel");

        game.ShowTab(GameScreen.SideTab.Reports);
        await Frames();
        var row = reports.GetNode<VBoxContainer>("%Rows").GetNode<Button>($"Report{state.ScoutReports.Single().Id}");
        Expect(row.Text.StartsWith(L.T("scout.headline_seen", ("target", rival.Name))), $"The scout report is listed, got '{row.Text}'");
        Press(row);
        await Frames();
        Expect(dialog.Visible && dialog.GetNode<Label>("%Result").Text == L.T("scout.result_full"), "An undefended village gives the full report");
        Expect(dialog.GetNode<Label>("%Powers").Text.StartsWith(L.T("scout.wall_level", ("level", "2"))), "The report gives the Wall level");
        Expect(Shows(dialog.GetNode<Label>("%Loot").Text, "scout.resources"), "The report gives the resources");
        Expect(dialog.GetNode<Label>("%Damage").Visible && dialog.GetNode<Label>("%Damage").Text.Contains(L.T("scout.building_level", ("building", UiContent.Buildings[BuildingType.Wall].Name), ("level", "2"))), "The full report gives the buildings");
        Expect(Shows(dialog.GetNode<Label>("%Loyalty").Text, "scout.loyalty"), "The full report gives the loyalty");
        await Shot("scout-report");
        Press(dialog.GetNode<Button>("%Close"));

        // Scouts that are caught, seen from the side that sent them. This uses up every Scout at home.
        await TrainAndWait(session, 2, 40, UnitType.Scout);
        Expect(session.SendAs(1, new SendTroopsCommand(1, 2, MovementType.Scout, new Dictionary<UnitType, int> { [UnitType.Scout] = home.TroopCount(UnitType.Scout) })).Success, "A second scouting goes out");
        await RunUntilArrived(session);
        Expect(!state.ScoutReports[^1].Succeeded, "Many Scouts catch a few");
        Expect(home.TroopCount(UnitType.Scout) == 0 && state.Movements.Count == 0, "Scouts that are caught all die");
        game.ShowTab(GameScreen.SideTab.Send);
        send.SetTarget(2);
        await Frames();
        Expect(Shows(intel.Text, "intel.lost_only") || Shows(intel.Text, "intel.seen_then_lost"), $"The form shows the failure, got '{intel.Text}'");

        // Both directions, seen from the side being scouted. With no Watchtower, a success goes unnoticed.
        Expect(session.SendAs(2, new SendTroopsCommand(2, 1, MovementType.Scout, new Dictionary<UnitType, int> { [UnitType.Scout] = 1 })).Success, "The rival sends a Scout");
        await Frames();
        var incoming = state.Movements.Single(m => m.OwnerId == 2);
        Expect(!UiContent.IsVisibleMovement(session, incoming), "Incoming scouts are hidden");
        Expect(!FindAll<ResourceBar>(game).First().GetNode<Button>("%Incoming").Visible, "Incoming scouts raise no warning");
        Expect(world.GetNode<MovementLayer>("World/Movements").Entries.All(e => e.MovementId != incoming.Id), "Incoming scouts have no marker");
        var known = reports.UnreadCount(session);
        await RunUntilArrived(session);
        Expect(state.ScoutReports[^1].Succeeded, "A village with no Scouts is scouted");
        Expect(reports.UnreadCount(session) == known, "A successful scout tells a defender with no Watchtower nothing");

        // A Watchtower sees even a success, without learning what was seen.
        Expect(session.SendAs(1, new BuildCommand(1, BuildingType.Watchtower)).Success, "A Watchtower is ordered");
        session.Tick(home.BuildQueue[^1].FinishTime - state.ElapsedTime + 0.001);
        await Frames();
        Expect(session.SendAs(2, new SendTroopsCommand(2, 1, MovementType.Scout, new Dictionary<UnitType, int> { [UnitType.Scout] = 1 })).Success, "The rival tries again");
        await RunUntilArrived(session);
        Expect(state.ScoutReports[^1].Succeeded, "The Watchtower doesn't stop anyone by itself");
        Expect(reports.UnreadCount(session) == known + 1, "The Watchtower tells the defender of a success");
        game.ShowTab(GameScreen.SideTab.Reports);
        await Frames();
        var seen = reports.GetNode<VBoxContainer>("%Rows").GetNode<Button>($"Report{state.ScoutNotices[^1].Id}");
        Expect(seen.Text.StartsWith(L.T("notice.seen", ("sender", "Rival"), ("target", "")).TrimEnd()), $"The notice names the sender, got '{seen.Text}'");
        Press(seen);
        await Frames();
        Expect(dialog.GetNode<Label>("%Result").Text == L.T("notice.result_seen"), "The Watchtower's notice opens");
        Press(dialog.GetNode<Button>("%Close"));

        game.ShowTab(GameScreen.SideTab.Send);
        await Frames();
        known = reports.UnreadCount(session);
        await TrainAndWait(session, 1, 40, UnitType.Scout);
        Expect(session.SendAs(2, new SendTroopsCommand(2, 1, MovementType.Scout, new Dictionary<UnitType, int> { [UnitType.Scout] = 1 })).Success, "The rival tries once more");
        await RunUntilArrived(session);
        Expect(!state.ScoutReports[^1].Succeeded, "The player's Scouts catch the rival's");
        Expect(reports.UnreadCount(session) == known + 1, "The defender is told about caught scouts");
        game.ShowTab(GameScreen.SideTab.Reports);
        await Frames();
        var notice = reports.GetNode<VBoxContainer>("%Rows").GetNode<Button>($"Report{state.ScoutNotices[^1].Id}");
        Expect(Shows(notice.Text.Split("  ·  ")[0], "notice.caught") && notice.Text.Contains("Rival"), $"The notice names the sender, got '{notice.Text}'");
        Press(notice);
        await Frames();
        Expect(dialog.GetNode<Label>("%Result").Text == L.T("notice.result_caught"), "The notice opens");
        await Shot("scout-caught");
        Press(dialog.GetNode<Button>("%Close"));

        // The margin decides how much is seen. The ratios stay inside each quality's limits, and it tries until one succeeds.
        async Task<ScoutReport> ScoutedAt(double ratio)
        {
            for (var attempt = 0; attempt < 60; attempt++)
            {
                if (rival.TroopCount(UnitType.Scout) < 10)
                    await TrainAndWait(session, 2, 40, UnitType.Scout);
                var scouts = (int)Math.Ceiling(rival.TroopCount(UnitType.Scout) * ratio);
                if (home.TroopCount(UnitType.Scout) < scouts)
                    await TrainAndWait(session, 1, scouts, UnitType.Scout);
                Expect(session.SendAs(1, new SendTroopsCommand(1, 2, MovementType.Scout, new Dictionary<UnitType, int> { [UnitType.Scout] = scouts })).Success, "Another scouting goes out");
                await RunUntilArrived(session);
                if (state.ScoutReports[^1] is { Succeeded: true } report && report.SenderId == 1)
                    return report;
            }
            throw new InvalidOperationException("No scouting succeeded.");
        }

        var narrow = await ScoutedAt(1.5);
        Expect(narrow.Quality == ScoutQuality.Narrow && narrow.Stock is null, "A narrow success gives no exact numbers");
        game.ShowTab(GameScreen.SideTab.Send);
        send.SetTarget(2);
        await Frames();
        Expect(intel.Text.Contains(L.T("intel.narrow", ("troops", UiContent.RangeText(narrow.TroopRange!)), ("wall", UiContent.RangeText(narrow.WallRange!)))), $"The form shows the ranges, got '{intel.Text}'");
        Expect(narrow.TroopRange!.Low <= 5 && 5 <= narrow.TroopRange.High && narrow.WallRange!.Low <= 2 && 2 <= narrow.WallRange.High, "The ranges contain the truth");
        dialog.ShowScoutReport(session, narrow);
        await Frames();
        Expect(dialog.GetNode<Label>("%Result").Text == L.T("scout.result_narrow"), "The report names a narrow success");
        Expect(dialog.GetNode<Label>("%Powers").Text.Contains($"{narrow.WallRange!.Low}-{narrow.WallRange.High}") && !dialog.GetNode<Label>("%Loot").Visible, "The narrow report gives a range and no resources");
        await Shot("scout-narrow");
        Press(dialog.GetNode<Button>("%Close"));

        var clear = await ScoutedAt(3);
        Expect(clear.Quality == ScoutQuality.Clear && clear.Buildings is null && clear.Garrison![UnitType.Spearman] == 3, "A clear success gives exact troops, without buildings");
        await Frames();
        Expect(intel.Text.Contains(L.N("intel.troops", 5)) && intel.Text.Contains(L.T("intel.wall", ("level", "2"))), $"The form shows the exact numbers, got '{intel.Text}'");
    }

    private async Task TestGodMode(GameData data)
    {
        var god = new[] { "--dev-god" };
        Expect(DevOptions.Parse(god, isDebugBuild: true).God, "The god mode argument applies in debug builds");
        Expect(!DevOptions.Parse(god, isDebugBuild: false).God, "The god mode argument is ignored in release builds");
        Expect(!DevOptions.Parse(Array.Empty<string>(), isDebugBuild: true).God, "God mode needs its argument");

        var (game, session) = await StartMatch(data, null);
        var control = new GodModeControl();
        control.Init(session);
        game.AddChild(control);
        await Frames();
        var badge = game.GetNode<ResourceBar>("%ResourceBar").GetNode<Label>("Box/GodBadge");
        Expect(!badge.Visible, "No badge while god mode is off");

        void PressF9() => control._Input(new InputEventKey { Keycode = Key.F9, Pressed = true });

        PressF9();
        await Frames();
        Expect(session.IsGodMode && badge.Visible && badge.Text == "GOD MODE", "F9 turns god mode on, with a badge");
        await Shot("god-mode");

        var state = session.State;
        var home = state.Villages[1];
        var capacity = BuildingRules.VillageCapacity(data, home);
        Expect(home.Resources.Wood == capacity && home.Resources.Food == capacity, "Resources show as full");

        var panel = FindAll<BuildingPanel>(game).First();
        SelectGameTile(game, BuildingType.Barracks);
        await Frames();
        var level = home.BuildingLevel(BuildingType.Barracks);
        Press(panel.GetNode<Button>("%Upgrade"));
        await Frames();
        Expect(home.BuildingLevel(BuildingType.Barracks) == level + 1 && home.BuildQueue.Count == 0, "The upgrade button works instantly");
        Expect(home.Resources.Wood == capacity, "Upgrading spends nothing");

        for (var i = 0; i < 5; i++)
        {
            if (!panel.GetNode<Button>("%Upgrade").Disabled)
                Press(panel.GetNode<Button>("%Upgrade"));
            await Frames(1);
        }
        await Frames();
        var limit = BuildingRules.MaxOtherBuildingLevel(data, home.BuildingLevel(BuildingType.MainBuilding));
        Expect(home.BuildingLevel(BuildingType.Barracks) == limit && panel.GetNode<Button>("%Upgrade").Disabled, "The Main Building limit still disables the button");

        game.ShowTab(GameScreen.SideTab.Troops);
        await Frames();
        var row = game.GetNode<TroopsPanel>("%Troops").GetNode<VBoxContainer>("%Rows").GetNode(nameof(UnitType.Swordsman));
        Press(row.GetNode<Button>("Controls/Actions/Max"));
        await Frames();
        Expect((int)row.GetNode<SpinBox>("Controls/Actions/Amount").Value == 9999, "Max offers the largest batch");
        row.GetNode<SpinBox>("Controls/Actions/Amount").Value = 500;
        await Frames();
        Press(row.GetNode<Button>("Controls/Actions/Train"));
        await Frames();
        Expect(home.TroopCount(UnitType.Swordsman) == 500 && home.TrainingQueue.Count == 0, "Training is instant");

        session.Send(new SendTroopsCommand(1, 2, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 500 }));
        await Frames();
        Expect(state.Reports.Count == 1 && state.Movements.Count == 0, "An attack arrives and returns instantly");

        PressF9();
        await Frames();
        Expect(!session.IsGodMode && !badge.Visible, "F9 turns god mode off, and the badge goes");
        Expect(home.BuildingLevel(BuildingType.Barracks) == limit, "Levels stay after turning it off");
        session.Send(new SendTroopsCommand(1, 2, MovementType.Raid, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 1 }));
        await Frames();
        Expect(state.Movements.Count == 1, "Travel takes time again");
    }

    private async Task TestSurrender(GameData data)
    {
        var (game, session) = await StartMatch(data, null, players: 3);
        var state = session.State;
        var pause = game.GetNode<Overlay>("%PauseOverlay");
        var confirm = game.GetNode<Overlay>("%SurrenderOverlay");
        var eliminated = game.GetNode<Overlay>("%EliminatedOverlay");
        var surrender = pause.GetNode<Button>("%Tertiary");

        game._UnhandledKeyInput(new InputEventKey { Pressed = true, Keycode = Key.P });
        await Frames();
        Expect(surrender.Visible && surrender.Text == L.T("overlay.surrender"), "The pause menu offers surrender");
        await Shot("pause-surrender");

        Press(surrender);
        await Frames();
        Expect(confirm.Visible && !pause.Visible, "Surrender asks for confirmation");
        Expect(session.IsPaused, "The match stays paused while confirming");
        await Shot("surrender-confirm");
        game._UnhandledKeyInput(new InputEventKey { Pressed = true, Keycode = Key.P });
        await Frames();
        Expect(session.IsPaused && confirm.Visible, "P does nothing while confirming");

        Press(confirm.GetNode<Button>("%Secondary"));
        await Frames();
        Expect(pause.Visible && !confirm.Visible && !state.Players[1].IsEliminated, "Keep playing goes back without surrendering");

        Press(surrender);
        await Frames();
        Press(confirm.GetNode<Button>("%Action"));
        await Frames();
        Expect(state.Players[1].HasSurrendered && !session.IsPaused, "Confirming surrenders and resumes");
        Expect(state.Villages[1].IsAbandoned, "The village is abandoned");
        Expect(!state.IsMatchOver && eliminated.Visible, "With two left the match goes on");
        Expect(eliminated.GetNode<Label>("%Title").Text == L.T("overlay.surrendered.title"), "The message says you surrendered");
        await Shot("surrendered");

        Press(eliminated.GetNode<Button>("%Action"));
        await Frames();
        Expect(!eliminated.Visible && game.CurrentView == GameScreen.MainView.World, "Watching shows the map");
        game._UnhandledKeyInput(new InputEventKey { Pressed = true, Keycode = Key.P });
        await Frames();
        Expect(!surrender.Visible, "A player who is out has nothing to surrender");
        game._UnhandledKeyInput(new InputEventKey { Pressed = true, Keycode = Key.P });
        await Frames();

        // Abandoned villages on the map, from a player still in the match.
        var (other, otherSession) = await StartMatch(data, null, players: 3);
        Expect(otherSession.SendAs(2, new SurrenderCommand()).Success, "The rival can surrender");
        other.ShowView(GameScreen.MainView.World);
        await Frames();
        var world = other.GetNode<WorldMapView>("%WorldMapView");
        var popup = world.GetNode<VillagePopup>("Popup");
        popup.Refresh(otherSession, otherSession.State.Villages[2], otherSession.State.Villages[1]);
        await Frames();
        Expect(popup.GetNode<Label>("%Owner").Text.EndsWith(L.T("popup.abandoned")), $"The popup says it is abandoned, got '{popup.GetNode<Label>("%Owner").Text}'");
        await Shot("abandoned");

        // Two players: it ends at once, as a defeat, with the score table.
        var (last, lastSession) = await StartMatch(data, null);
        last._UnhandledKeyInput(new InputEventKey { Pressed = true, Keycode = Key.P });
        await Frames();
        Press(last.GetNode<Overlay>("%PauseOverlay").GetNode<Button>("%Tertiary"));
        await Frames();
        Press(last.GetNode<Overlay>("%SurrenderOverlay").GetNode<Button>("%Action"));
        await Frames();
        Expect(lastSession.State.IsMatchOver, "With one left the match ends at once");
        var end = last.GetNode<MatchEndScreen>("%MatchEndScreen");
        Expect(end.Visible && end.GetNode<Label>("%Title").Text == L.T("report.defeat"), "Surrendering is a defeat");
        Expect(FindAll<Label>(end).Any(l => l.Text.EndsWith(L.T("end.surrendered", ("name", "")))), "The table shows who surrendered");
        await Shot("surrender-defeat");
    }

    private async Task TestReportsOfLoyalty(GameData data)
    {
        var (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.Villages);
        var own = UiContent.OwnVillages(session);
        var army = own[1];
        var dialog = game.GetNode<ReportDialog>("%ReportDialog");
        var label = dialog.GetNode<Label>("%Loyalty");

        session.Send(new SendTroopsCommand(army.Id, 2, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 40, [UnitType.Lord] = 1 }));
        await RunUntilArrived(session);
        dialog.ShowReport(session, session.State.Reports[^1]);
        Expect(label.Visible && label.Text == L.T("report.capital_protected"), $"A locked capital is reported, got '{label.Text}'");
        await Shot("report-protected");
        dialog.Visible = false;

        // A big enough force that the Lords survive the battle.
        var camp = session.State.Villages.Values.Where(v => v.IsBarbarian).MinBy(v => v.Troops.Values.Sum())!;
        session.Send(new SendTroopsCommand(army.Id, camp.Id, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 100, [UnitType.Lord] = 2 }));
        await RunUntilArrived(session);
        dialog.ShowReport(session, session.State.Reports[^1]);
        Expect(label.Visible && Shows(label.Text, "report.loyalty_change"), $"A lowered loyalty is reported, got '{label.Text}'");
        dialog.Visible = false;

        var (endGame, endSession) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.Endgame, players: 3);
        endSession.Send(new SendTroopsCommand(1, 2, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 100, [UnitType.Lord] = 1 }));
        endSession.Tick(endSession.State.Movements[0].ArrivalTime);
        await Frames();
        var endDialog = endGame.GetNode<ReportDialog>("%ReportDialog");
        endDialog.ShowReport(endSession, endSession.State.Reports[^1]);
        Expect(endDialog.GetNode<Label>("%Loyalty").Text == L.T("report.captured"), "A capture is reported");
        await Shot("report-captured");
        endDialog.Visible = false;

        endGame.ShowTab(GameScreen.SideTab.Village);
        await Frames();
        var info = endGame.GetNode<Label>("%VillageInfo").Text;
        Expect(Shows(info.Split('\n')[0], "village.info") && info.Contains(L.T("popup.capital")), $"The Village tab shows loyalty and the capital, got '{info}'");
    }

    private async Task TestFounding(GameData data)
    {
        var (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.Founding);
        game.ShowView(GameScreen.MainView.World);
        await Frames();
        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var popup = world.GetNode<VillagePopup>("Popup");
        var found = popup.GetNode<Button>("%Found");
        var map = session.State.Map;
        var tiles = (from x in Enumerable.Range(0, map.Width) from y in Enumerable.Range(0, map.Height) select new TileCoord(x, y)).ToList();

        var water = tiles.Where(t => map.TileAt(t).Terrain == Terrain.Water)
            .Where(t => session.State.Villages.Values.All(v => UnitRules.Distance(v.Position, t) >= 2)).ToList();
        Expect(water.Count > 0, "This seed has water to try");
        if (water.Count > 0)
        {
            Click(world, world.WorldToView(WorldMapView.TileWorldPosition(water[0])));
            await Frames();
            Expect(popup.Visible && found.Visible && found.Disabled, "Water can't be settled");
            Expect(popup.GetNode<Label>("%Reason").Text == UiContent.RejectionText(CommandRejection.TileIsWater), "Water states the reason");
        }

        var near = tiles.First(t => map.TileAt(t).VillageId is null && map.TileAt(t).Terrain != Terrain.Water
            && UnitRules.Distance(t, session.State.Villages[1].Position) is > 1 and < 2.9);
        Click(world, world.WorldToView(WorldMapView.TileWorldPosition(near)));
        await Frames();
        Expect(found.Disabled, "A tile too close can't be settled");
        Expect(popup.GetNode<Label>("%Reason").Text == UiContent.RejectionText(CommandRejection.TooCloseToVillage), "Too close states the reason");
        await Shot("found-too-close");

        var free = tiles.First(t => FoundPreview.Evaluate(session.State, data, 1, 1, t).CanFound);
        Click(world, world.WorldToView(WorldMapView.TileWorldPosition(free)));
        await Frames();
        Expect(popup.Visible && !found.Disabled, "Free land can be settled");
        await Shot("found-popup");
        Press(found);
        await Frames();
        Expect(!popup.Visible, "Sending closes the popup");
        Expect(session.State.Movements.Any(m => m.Type == MovementType.Settle), "The Settler leaves");
        var layer = world.GetNode<MovementLayer>("World/Movements");
        Expect(layer.Entries.Count == 1, "The Settler's route is drawn");
        await Shot("found-travelling");

        var villages = session.State.Villages.Count;
        await RunUntilArrived(session);
        Expect(session.State.Villages.Count == villages + 1, "The village is founded");
        var founded = session.State.Villages[map.TileAt(free).VillageId ?? -1];
        Expect(world.GetNode("World/Markers").HasNode($"Village{founded.Id}"), "The new village gets a marker");
        Click(world, world.WorldToView(WorldMapView.VillageWorldPosition(founded)));
        await Frames();
        Expect(world.SelectedVillageId == founded.Id && popup.GetNode<Label>("%Name").Text == founded.Name, "The new village can be selected");
        Expect(game.GetNode<VillageStrip>("%VillageStrip").Visible, "The strip appears with a second village");
        await Shot("found-done");

        var (plain, plainSession) = await StartMatch(data, null);
        plain.ShowView(GameScreen.MainView.World);
        await Frames();
        var plainWorld = plain.GetNode<WorldMapView>("%WorldMapView");
        Click(plainWorld, plainWorld.WorldToView(WorldMapView.TileWorldPosition(tiles.First(t => FoundPreview.Evaluate(plainSession.State, data, 1, 1, t).Rejection == CommandRejection.VillageLimitReached))));
        await Frames();
        Expect(!plainWorld.GetNode<VillagePopup>("Popup").Visible, "Without a Settler, clicking land opens nothing");
    }

    private async Task<(GameScreen Game, LocalGameSession Session)> LoseCapital(GameData data)
    {
        var (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.Villages, rivalAttacks: true);
        await Frames();
        await RunUntilCapitalLost(session);
        await Frames();
        Expect(session.State.Villages[1].OwnerId == 2, "The rival takes the capital");
        return (game, session);
    }

    private static Task RunUntilCapitalLost(LocalGameSession session)
    {
        while (session.State.Players[1].CapitalChoiceDeadline is null && session.State.Movements.Any(m => m.ToVillageId == 1))
            session.Tick(session.State.Movements.Where(m => m.ToVillageId == 1).Min(m => m.ArrivalTime) - session.State.ElapsedTime);
        return Task.CompletedTask;
    }

    private async Task TestCapitalChoice(GameData data)
    {
        var (game, session) = await LoseCapital(data);
        var dialog = game.GetNode<CapitalDialog>("%CapitalDialog");
        var warning = game.GetNode<ResourceBar>("%ResourceBar").GetNode<Button>("%CapitalWarning");
        var own = UiContent.OwnVillages(session);

        Expect(dialog.Visible, "Losing the capital opens the dialog");
        Expect(game.CurrentVillageId == own[0].Id, "The view moves to a village you still own");
        Expect(!warning.Visible, "No warning while the dialog is open");
        await Shot("capital-dialog");

        PressKey(game, Key.E);
        Expect(game.CurrentVillageId == own[0].Id, "Keys don't reach the game behind the dialog");

        var countdown = dialog.GetNode<Label>("%Countdown").Text;
        session.Tick(5);
        await Frames();
        Expect(dialog.GetNode<Label>("%Countdown").Text != countdown, "The countdown runs");

        Press(dialog.GetNode<Button>("%Later"));
        await Frames();
        Expect(!dialog.Visible, "The dialog can be set aside");
        Expect(warning.Visible && Shows(warning.Text, "bar.choose_capital"), $"The top bar warns, got '{warning.Text}'");
        PressKey(game, Key.E);
        Expect(game.CurrentVillageId == own[1].Id, "The game can be played with the dialog set aside");
        await Shot("capital-warning");

        Press(warning);
        await Frames();
        Expect(dialog.Visible && !warning.Visible, "The warning reopens the dialog");

        var row = dialog.GetNode<VBoxContainer>("%Villages").GetNode<Button>("Village0");
        Expect(row.Text.Contains(own[0].Name), "The dialog lists your villages");
        Press(row);
        await Frames();
        var player = session.State.Players[1];
        Expect(player.CapitalVillageId == own[0].Id, "Choosing sets the capital");
        Expect(own[0].BuildingLevel(BuildingType.MainBuilding) == data.Villages.Capital.PenaltyMainLevel, "Choosing applies the penalty");
        Expect(!dialog.Visible && !warning.Visible, "Choosing closes the dialog and warning");
    }

    private async Task TestCapitalTimeout(GameData data)
    {
        var (game, session) = await LoseCapital(data);
        var dialog = game.GetNode<CapitalDialog>("%CapitalDialog");
        var expected = CapitalRules.AutomaticChoice(session.State, 1).Id;
        Press(dialog.GetNode<Button>("%Later"));
        await Frames();

        session.Tick(data.Villages.Capital.ChoiceSeconds);
        await Frames();

        Expect(session.State.Players[1].CapitalVillageId == expected, "Time running out picks the capital");
        Expect(!dialog.Visible, "The dialog stays closed after the timeout");
        Expect(!game.GetNode<ResourceBar>("%ResourceBar").GetNode<Button>("%CapitalWarning").Visible, "The warning clears after the timeout");
    }

    private async Task TestWatchingAfterElimination(GameData data)
    {
        var (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.LastStand, players: 3, rivalAttacks: true);
        await RunUntilArrived(session);
        await Frames();

        Expect(session.State.Players[1].IsEliminated, "Losing the last village eliminates you");
        Expect(!session.State.IsMatchOver, "The match goes on with two players left");
        Expect(game.CurrentVillageId is null, "No village is current");
        var overlay = game.GetNode<Overlay>("%EliminatedOverlay");
        Expect(overlay.Visible, "Elimination is announced");
        await Shot("eliminated");

        Press(overlay.GetNode<Button>("%Action"));
        await Frames();
        Expect(!overlay.Visible, "Keep watching closes the message");
        Expect(game.CurrentView == GameScreen.MainView.World, "Watching shows the map");
        var tabs = game.GetNode<TabContainer>("%Side");
        Expect(tabs.CurrentTab == (int)GameScreen.SideTab.Reports, "Watching shows the reports");
        Expect(tabs.IsTabHidden((int)GameScreen.SideTab.Send), "Watching hides the Send tab");

        PressKey(game, Key.M);
        Expect(game.CurrentView == GameScreen.MainView.World, "M can't leave the map while watching");

        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var popup = world.GetNode<VillagePopup>("Popup");
        Click(world, world.WorldToView(WorldMapView.VillageWorldPosition(session.State.Villages[2])));
        await Frames();
        Expect(popup.Visible && !popup.GetNode<Button>("%Send").Visible, "A watcher can look but not send");
        await Shot("watching");

        session.Tick(10);
        await Frames();
        session.Tick(TestDuration);
        await Frames();
        Expect(game.GetNode<MatchEndScreen>("%MatchEndScreen").Visible, "The results appear for a watcher too");
        Expect(game.GetNode<MatchEndScreen>("%MatchEndScreen").GetNode<Label>("%Title").Text == L.T("report.defeat"), "A watcher has lost");
    }

    private async Task TestMatchResults(GameData data)
    {
        var (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.Endgame);
        session.Send(new SendTroopsCommand(1, 2, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 100, [UnitType.Lord] = 1 }));
        session.Tick(session.State.Movements[0].ArrivalTime);
        await Frames();
        var end = game.GetNode<MatchEndScreen>("%MatchEndScreen");
        Expect(end.Visible && end.GetNode<Label>("%Title").Text == L.T("report.victory"), "Taking the last village wins");
        Expect(end.GetNode<Label>("%Reason").Text == L.T("end.you_last"), "The reason is stated");
        var cells = end.GetNode<GridContainer>("%Table").GetChildren().OfType<Label>().Select(l => l.Text).ToList();
        var top = session.State.Outcome!.Scores[0];
        Expect(cells.Count == 18, $"A header and one row per player, got {cells.Count} cells");
        Expect(cells[7] == L.T("player.you") && cells[8] == $"{top.Villages}" && cells[9] == $"{top.Population}" && cells[10] == $"{top.Captures}" && cells[11] == $"{top.Score:0}", "The table matches the score");
        Expect(cells[13].EndsWith(L.T("end.eliminated", ("name", ""))), "Eliminated players are marked");
        await Shot("end-victory");

        (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.LastStand, rivalAttacks: true);
        await RunUntilArrived(session);
        end = game.GetNode<MatchEndScreen>("%MatchEndScreen");
        Expect(end.Visible && end.GetNode<Label>("%Title").Text == L.T("report.defeat"), "Losing the last village loses");
        Expect(!game.GetNode<Overlay>("%EliminatedOverlay").Visible, "The results replace the elimination message");
        await Shot("end-defeat");

        (game, session) = await StartMatch(data, null, duration: 30);
        session.Tick(30);
        await Frames();
        end = game.GetNode<MatchEndScreen>("%MatchEndScreen");
        Expect(end.GetNode<Label>("%Title").Text == L.T("end.draw"), "Equal scores at time-out are a draw");
        Expect(end.GetNode<Label>("%Reason").Text == L.T("end.tied", ("names", L.T("common.and", ("first", L.T("player.you")), ("second", "Rival")))), "The draw is explained");
        await Shot("end-draw");

        (game, session) = await StartMatch(data, null, duration: 100);
        session.Send(new BuildCommand(1, BuildingType.Farm));
        session.Tick(100);
        await Frames();
        end = game.GetNode<MatchEndScreen>("%MatchEndScreen");
        Expect(end.GetNode<Label>("%Title").Text == L.T("report.victory"), "The higher score wins at time-out");
        Expect(end.GetNode<Label>("%Reason").Text == L.T("end.you_highest"), "The time-out win is explained");

        foreach (var child in GetChildren().OfType<GameScreen>())
            child.QueueFree();
        await Frames();
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

        // Let view fades finish, so the picture shows the settled screen.
        await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng($"{_shotsDir}/{name}.png");
    }
}
