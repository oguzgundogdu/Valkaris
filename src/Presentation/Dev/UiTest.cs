using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Valkaris.Core.Commands;
using Valkaris.Core.Data;
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

    private async Task Run()
    {
        _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        AddChild(_main);
        await Frames();

        var start = Find<StartScreen>();
        await Shot("start");

        // The longest match, so the steps below can use plenty of game time.
        Expect(start.GetNode<OptionButton>("%MapSize").ItemCount == Enum.GetValues<MapSize>().Length, "Every map size is offered");
        var durations = start.GetNode<OptionButton>("%Duration");
        durations.Select(durations.ItemCount - 1);
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
        Expect(panel.GetNode<Label>("%Reason").Text == "The queue is full.", "The button says why it is disabled");

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

        await TestTroops();
        await TestSend();
        await TestReports();
        await TestFeedback();
        await TestPause();
        TestDevOptions();
        await TestLeaveMatch();
        await TestMatchEnd();
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
        Expect(Row(UnitType.Catapult).GetNode<Label>("Note").Text == "Requires Barracks level 5", "A locked unit states its requirement");
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
        Expect(mission.ItemCount == 2, "Reinforce isn't offered without a second village");
        var target = send.GetNode<VBoxContainer>("%Targets").GetNode<Button>("Target2");
        target.EmitSignal(BaseButton.SignalName.Pressed);
        Expect(target.Text.StartsWith("Rival's village"), $"The target list names the rival, got '{target.Text}'");
        Expect(button.Disabled && reason.Text == UiContent.RejectionText(CommandRejection.NoTroopsSelected), "No troops selected disables Send and says why");
        Expect(send.GetNode<Label>("%None").Visible, "Nothing on the move yet");

        spearmen.GetNode<Button>("Max").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        var present = village.TroopCount(UnitType.Spearman);
        Expect((int)spearmen.GetNode<SpinBox>("Amount").Value == present, "Max selects the whole garrison");
        Expect(send.GetNode<Label>("%Arrival").Text.StartsWith("Arrives in"), "The arrival time is previewed");
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
        Expect(row.GetNode<Label>("Title").Text == "Raid → Rival", "The movement is listed");
        await Shot("moving");

        session.Tick(session.State.Movements[0].ArrivalTime - session.State.ElapsedTime);
        await Frames();
        Expect(session.State.Reports.Count == 1, "The raid is fought on arrival");
        if (session.State.Movements.Count > 0)
        {
            Expect(row.GetNode<Label>("Title").Text.StartsWith("Returning"), "Survivors are listed as returning");
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
        Expect(tabs.GetTabTitle(reportsTab) == "Reports (1)", $"An unread report is counted, got '{tabs.GetTabTitle(reportsTab)}'");

        game.ShowTab(GameScreen.SideTab.Reports);
        await Frames();
        Expect(tabs.GetTabTitle(reportsTab) == "Reports", "Opening the tab clears the unread count");
        var row = reports.GetNode<VBoxContainer>("%Rows").GetNode<Button>("Report1");
        Expect(row.Text.StartsWith("Lost: Raid on Rival"), $"The lost raid is listed, got '{row.Text}'");
        await Shot("reports");

        row.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(dialog.Visible && dialog.GetNode<Label>("%Result").Text == "Defeat", "The report opens as a defeat");
        Expect(dialog.GetNode<VBoxContainer>("%Attacker").GetChildCount() == 2, "The report lists the attacking side");
        Expect(!dialog.GetNode<Label>("%Loot").Visible, "A lost raid shows no loot");
        Expect(dialog.GetNode<Label>("%Powers").Text.Contains("vs  defence 200"), $"The report shows the weighted defence, got '{dialog.GetNode<Label>("%Powers").Text}'");
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

        send.GetNode<VBoxContainer>("%Targets").GetNode<Button>("Target2").EmitSignal(BaseButton.SignalName.Pressed);
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
        Expect(row.Text.StartsWith("Won: Attack on Rival"), $"The won attack is listed, got '{row.Text}'");
        row.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        var dialog = game.GetNode<ReportDialog>("%ReportDialog");
        Expect(dialog.GetNode<Label>("%Result").Text == "Victory", "The report opens as a victory");
        Expect(dialog.GetNode<Label>("%Loot").Visible, "A won battle shows loot");
        Expect(dialog.GetNode<Label>("%Damage").Text == "Catapults hit the Woodcutter: level 1 → 0", $"The report shows the damage, got '{dialog.GetNode<Label>("%Damage").Text}'");
        await Shot("report-victory");
        dialog.GetNode<Button>("%Close").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
    }

    /// <summary>The defender's view, through the same dev path as --dev-rival-attack.</summary>
    private async Task TestBeingAttacked(GameData data)
    {
        var settings = new GameSettings(TestDuration, 1, MapSize.Small);
        var slots = new[] { new PlayerSlot("You", Nation.Valdren, false), new PlayerSlot("Rival", Nation.Karuk, true) };
        var session = new LocalGameSession(GameFactory.CreateMatch(settings, slots, data), data, 1);
        var game = GD.Load<PackedScene>("res://scenes/Game.tscn").Instantiate<GameScreen>();
        game.Init(session, devRivalAttackSeconds: 0.001);
        AddChild(game);
        await Frames();
        session.Tick(0.01);
        await Frames();

        Expect(session.State.Movements.Count == 1, "The rival sends its garrison");
        var incoming = game.GetNode<ResourceBar>("%ResourceBar").GetNode<Label>("%Incoming");
        Expect(incoming.Visible && incoming.Text.StartsWith("Attack arriving in"), $"The top bar warns of the attack, got '{incoming.Text}'");

        game.ShowTab(GameScreen.SideTab.Send);
        await Frames();
        var row = game.GetNode<SendPanel>("%Send").GetNode<VBoxContainer>("%Rows").GetNode("Moving0");
        Expect(row.GetNode<Label>("Title").Text == "Attack from Rival", "The hostile movement is listed");
        await Shot("incoming");

        session.Tick(session.State.Movements[0].ArrivalTime - session.State.ElapsedTime);
        await Frames();
        Expect(!incoming.Visible, "The warning clears after the battle");

        game.ShowTab(GameScreen.SideTab.Reports);
        await Frames();
        var report = game.GetNode<ReportsPanel>("%Reports").GetNode<VBoxContainer>("%Rows").GetNode<Button>("Report1");
        Expect(report.Text.StartsWith("Won: Attack by Rival"), $"The defender's report says they won, got '{report.Text}'");
        report.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        var dialog = game.GetNode<ReportDialog>("%ReportDialog");
        Expect(dialog.GetNode<Label>("%Result").Text == "Victory", "A successful defence is a victory");
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
        var barbarian = game.GetNode<ReportsPanel>("%Reports").GetNode<VBoxContainer>("%Rows").GetNode<Button>("Report2");
        Expect(barbarian.Text.Contains("Raid on Barbarians"), $"The barbarian battle is listed, got '{barbarian.Text}'");
        barbarian.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(dialog.Visible && dialog.GetNode<VBoxContainer>("%Defender").GetChild<Label>(0).Text == "Barbarians (defending)", "The report names the barbarians");
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
        Expect(toast.GetNode<Label>("Label").Text == "Farm reached level 3", "Toast names the building and level");

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

        var rivalAttack = new[] { "--dev-rival-attack=90" };
        Expect(DevOptions.Parse(rivalAttack, isDebugBuild: true).RivalAttackSeconds == 90, "The rival attack argument applies in debug builds");
        Expect(DevOptions.Parse(rivalAttack, isDebugBuild: false).RivalAttackSeconds is null, "The rival attack argument is ignored in release builds");

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

        SelectTile(BuildingType.Woodcutter);
        await Frames();
        var panel = game.GetNode<BuildingPanel>("%BuildingPanel");
        Expect(panel.GetNode<Button>("%Upgrade").Text == "Unavailable", "A finished match offers no upgrade");
        Expect(!panel.GetNode<Label>("%Effect").Text.Contains("Level 0"), "A finished match shows no next level");
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
        Expect(upgrade.Disabled && reason.Text.Contains("Warehouse"), $"Unaffordable cost points to the Warehouse, got '{reason.Text}'");
        await Shot("warehouse-hint");

        // With storage out of the way, the Main Building table blocks the next level until it grows.
        var limit = BuildingRules.MaxOtherBuildingLevel(data, village.BuildingLevel(BuildingType.MainBuilding));
        UpgradeTo(BuildingType.Warehouse, limit);
        UpgradeTo(BuildingType.Quarry, limit);
        session.Tick(60);
        await Select(BuildingType.Quarry);
        Expect(reason.Text == "Upgrade the Main Building to go higher.", $"Main Building limit is explained, got '{reason.Text}'");

        // Upgrades queued up to the max level, in a full queue, must not break the panel.
        UpgradeTo(BuildingType.MainBuilding, 8);
        session.Tick(60);
        session.Send(new BuildCommand(village.Id, BuildingType.MainBuilding));
        session.Send(new BuildCommand(village.Id, BuildingType.MainBuilding));
        await Select(BuildingType.MainBuilding);
        Expect(upgrade.Text == "Max level" && upgrade.Disabled, $"Queued to max shows max level, got '{upgrade.Text}'");

        session.Tick(60);
        await Frames();
        Expect(village.BuildingLevel(BuildingType.MainBuilding) == 10, "Main Building reaches max level");
        Expect(upgrade.Text == "Max level" && upgrade.Disabled && !reason.Visible, "Max level needs no reason text");
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
