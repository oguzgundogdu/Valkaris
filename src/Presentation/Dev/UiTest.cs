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
using Valkaris.Core.Network;
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
    private Vector2I _windowSize;

    public override async void _Ready()
    {
        _windowSize = DisplayServer.WindowGetSize();
        Main.SkipIntro = true;
        _shotsDir = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith("--shots="))?["--shots=".Length..];

        try
        {
            var netWindow = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--net-window="))?["--net-window=".Length..];
            if (OS.GetCmdlineUserArgs().Contains("--frame-report"))
                await FrameReport();
            else if (OS.GetCmdlineUserArgs().Contains("--net-report"))
                await NetReport();
            else if (netWindow is not null)
                await NetWindow(netWindow);
            else if (OS.GetCmdlineUserArgs().Contains("--layout-sweep"))
                await LayoutSweep();
            else if (OS.GetCmdlineUserArgs().Contains("--real-input"))
                await RealInputOnly();
            else
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

    /// <summary>Tests keep to the window they were given, whatever a first launch would do.</summary>
    private static void UseSettings(string path)
    {
        Drawer.Instant = true;
        Language.SettingsPath = path;
        UserSettings.Fullscreen = false;
        UserSettings.Nation = Nation.Valdren;
    }

    private void TestFullscreenDefault()
    {
        Expect(UserSettings.Fullscreen, "A first launch is fullscreen");
        UserSettings.Fullscreen = false;
    }

    /// <summary>Only headless, where changing the window mode can't disturb the sizes later steps rely on.</summary>
    private async Task TestFullscreenKey()
    {
        if (DisplayServer.GetName() != "headless")
            return;

        Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = Key.F11, PhysicalKeycode = Key.F11 });
        Input.FlushBufferedEvents();
        await Frames();
        Expect(UserSettings.Fullscreen, "F11 turns fullscreen on and remembers it");
        _main.ToggleFullscreen();
        Expect(!UserSettings.Fullscreen, "It toggles back");
    }

    /// <summary>A run under tr-TR proves nothing unless that culture is really in force.</summary>
    private void TestCulture()
    {
        if (DevOptions.Parse(OS.GetCmdlineUserArgs(), OS.IsDebugBuild()).Language is { } language)
            Expect(Language.Current == language && TranslationServer.GetLocale().StartsWith(language, StringComparison.Ordinal), $"The language is {language}");

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

    private async Task TestStartVillageName()
    {
        var start = Find<StartScreen>();
        var field = start.GetNode<LineEdit>("%VillageName");
        var button = start.GetNode<Button>("%Start");
        var reason = start.GetNode<Label>("%VillageNameReason");
        void Type(string text)
        {
            field.Text = text;
            field.EmitSignal(LineEdit.SignalName.TextChanged, text);
        }

        Expect(!button.Disabled && !reason.Visible, "An empty name is allowed");

        foreach (var (typed, rejection) in new[]
        {
            ("Ab", CommandRejection.VillageNameTooShort),
            ("B<a>d", CommandRejection.VillageNameInvalid),
            ("Ashfort", CommandRejection.VillageNameTaken),
        })
        {
            Type(typed);
            Expect(button.Disabled && reason.Visible && reason.Text == UiContent.RejectionText(rejection), $"'{typed}' is refused, got '{reason.Text}'");
        }

        Type("Twenty One Characters");
        Expect(field.Text.Length == field.MaxLength && field.MaxLength > 0, "The field stops at the limit");

        const string turkish = "\u0130\u015f\u0131kl\u0131 K\u00f6y";
        Type($"  {turkish} ");
        Expect(!button.Disabled && !reason.Visible, "A Turkish name is allowed");

        var languages = Language.Codes;
        start.GetNode<OptionButton>("%Language").EmitSignal(OptionButton.SignalName.ItemSelected, Array.IndexOf(languages, Language.Current));
        await Frames();
        start = Find<StartScreen>();
        Expect(start.GetNode<LineEdit>("%VillageName").Text.Trim() == turkish, "The typed name survives a language change");

        // A real match with that name, then the rename form in its Village tab.
        start.GetNode<Button>("%Start").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        var game = Find<GameScreen>();
        var session = game.Session;
        var village = session.State.Villages[1];
        Expect(village.Name == turkish, $"The match uses the typed name, got '{village.Name}'");

        game.ShowPanel(GameScreen.GamePanel.Building);
        await Frames();
        var panel = game.GetNode<RenamePanel>("%BuildingPopup/Frame/Scroll/Box/RenamePanel");
        var edit = panel.GetNode<Button>("Edit");
        var form = panel.GetNode<Control>("Form");
        var rename = panel.GetNode<LineEdit>("Form/Field");
        var save = panel.GetNode<Button>("Form/Save");
        var why = panel.GetNode<Label>("Reason");
        void Rename(string text)
        {
            rename.Text = text;
            rename.EmitSignal(LineEdit.SignalName.TextChanged, text);
        }

        Press(edit);
        await Frames();
        Expect(form.Visible && !edit.Visible && rename.Text == turkish && save.Disabled && !why.Visible, "Rename opens on the current name, without a complaint");

        rename.GrabFocus();
        await Frames();
        var paused = session.IsPaused;
        GetViewport().PushInput(new InputEventKey { Pressed = true, Keycode = Key.P, Unicode = 'p' });
        await Frames();
        Expect(session.IsPaused == paused && rename.Text.Contains('p'), "Typing P in the field goes to the field, not the pause key");

        foreach (var (typed, rejection) in new[]
        {
            ("Ab", CommandRejection.VillageNameTooShort),
            ("B<a>d", CommandRejection.VillageNameInvalid),
            ("Karvik", CommandRejection.VillageNameTaken),
            (session.State.Villages[2].Name, CommandRejection.VillageNameTaken),
        })
        {
            Rename(typed);
            Expect(save.Disabled && why.Visible && why.Text == UiContent.RejectionText(rejection), $"'{typed}' is refused, got '{why.Text}'");
        }

        Press(panel.GetNode<Button>("Form/Cancel"));
        await Frames();
        Expect(edit.Visible && !form.Visible && village.Name == turkish, "Cancel keeps the name");

        Press(edit);
        await Frames();
        Rename("\u00c7\u0131nar\u00fcst\u00fc");
        Expect(!save.Disabled && !why.Visible, "A new name can be saved");
        await Shot("rename_form");
        Press(save);
        await Frames();
        Expect(village.Name == "\u00c7\u0131nar\u00fcst\u00fc" && edit.Visible && !form.Visible, $"Save renames the village, got '{village.Name}'");
        Expect(game.GetNode<Label>("%VillageInfo").Text.Contains(village.Name), "The Village tab shows the new name");

        // Godot draws these with the bundled font, so any letter the rules allow must be in it.
        var font = ThemeDB.GetProjectTheme().DefaultFont;
        var undrawable = Enumerable.Range(0xC0, 0x17F - 0xC0 + 1).Select(c => (char)c).Where(c => VillageNameRules.FormatRejection(session.Data.Game.VillageNames, $"Ab{c}") is null).Where(c => !font.HasChar(c)).ToList();
        Expect(undrawable.Count == 0, $"The font draws every letter a name may use, missing '{string.Concat(undrawable)}'");

        game.ExitToStart();
        await Frames();
    }

    /// <summary>Every building at every stage, with the rules for plates and badges.</summary>
    private async Task TestBuildingStages()
    {
        var game = Find<GameScreen>();
        var village = game.Session.State.Villages[game.CurrentVillageId!.Value];
        var tiles = Find<VillageView>().GetChildren().OfType<BuildingTile>().ToList();
        var saved = tiles.ToDictionary(t => t.Building, t => village.BuildingLevel(t.Building));
        Find<Toast>().Visible = false;

        SelectTile(BuildingType.MainBuilding);
        await Frames();
        var other = tiles.First(t => t.Building == BuildingType.Farm);
        var selected = tiles.First(t => t.Building == BuildingType.MainBuilding);
        Expect(selected.GetNode<Control>("%Plate").Visible && !selected.GetNode<Control>("%Badge").Visible, "The selected building shows its name plate");
        Expect(!other.GetNode<Control>("%Plate").Visible && other.GetNode<Control>("%Badge").Visible, "Other buildings show only a level badge");

        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Alt, Pressed = true });
        await Frames();
        if (DisplayServer.GetName() != "headless")
            Expect(tiles.All(t => t.GetNode<Control>("%Plate").Visible), "Holding Alt shows every name plate");
        await Shot("stage-all-plates");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Alt, Pressed = false });
        await Frames();

        var lastHouses = 0;
        for (var stage = 0; stage <= VillageArt.StageCount; stage++)
        {
            foreach (var tile in tiles)
            {
                var max = game.Session.Data.Buildings.Buildings[tile.Building].MaxLevel;
                Valkaris.Core.Dev.DevScenario.SetLevel(village, tile.Building, Enumerable.Range(0, max + 1).First(level => VillageArt.Stage(level, max) == stage));
            }
            await Frames();
            Expect(tiles.All(t => t.Stage == stage), $"Every building shows stage {stage}");
            Expect((Find<VillageView>().Ring.Count > 0) == (stage > 0), $"The Wall's ring shows only once built, stage {stage}");
            var houses = Find<VillageView>().Scenery.Count(item => item.Sprite.Contains("house_"));
            Expect(houses >= lastHouses, $"The village never loses houses as it grows, went {lastHouses} to {houses}");
            lastHouses = houses;
            await Shot($"stage-{stage}");
        }
        Expect(lastHouses > 0, "A grown village has houses");

        foreach (var (building, level) in saved)
            Valkaris.Core.Dev.DevScenario.SetLevel(village, building, level);
        await Frames();
    }

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

        // The font must draw every character the game can show in Turkish, in both weights.
        var theme = ThemeDB.GetProjectTheme();
        var catalogue = FileAccess.GetFileAsString("res://locale/tr.po");
        var missing = catalogue.Where(c => c > 127 && !char.IsWhiteSpace(c)).Distinct().Where(c => !theme.DefaultFont.HasChar(c)).ToList();
        Expect(missing.Count == 0, $"The font has every Turkish character, missing '{string.Concat(missing)}'");
        Expect("\u011f\u011e\u015f\u015e\u0131\u0130\u00e7\u00c7\u00f6\u00d6\u00fc\u00dc".All(c => theme.DefaultFont.HasChar(c)), "The font has the Turkish letters");

        Find<StartScreen>().GetNode<OptionButton>("%Duration").Select(0);
        Find<StartScreen>().GetNode<OptionButton>("%Language").EmitSignal(OptionButton.SignalName.ItemSelected, Array.IndexOf(Language.Codes, language));
        await Frames();
    }

    /// <summary>What the village view costs per frame, in a window: "-- --frame-report". No pass or fail, since it depends on the machine.</summary>
    private async Task FrameReport()
    {
        UseSettings("user://uitest_settings.cfg");
        _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        _main.IdleOpponents = true;
        AddChild(_main);
        await Frames();
        Press(Find<StartScreen>().GetNode<Button>("%Start"));
        await Frames();
        await PrintFrameCost("village");

        var game = Find<GameScreen>();
        var village = game.Session.State.Villages[game.CurrentVillageId!.Value];
        foreach (var (building, definition) in game.Session.Data.Buildings.Buildings)
            Valkaris.Core.Dev.DevScenario.SetLevel(village, building, definition.MaxLevel);
        await PrintFrameCost("fully grown village");
        game.ShowPanel(GameScreen.GamePanel.Overview);
        await PrintFrameCost("fully grown village with the drawer open");
        game.ShowPanel(GameScreen.GamePanel.Reports);
        await PrintFrameCost("fully grown village with Reports open");
        game.GetNode<Drawer>("%Drawer").Close();
        game.ShowView(GameScreen.MainView.World);
        await PrintFrameCost("map");
        game.ShowPanel(GameScreen.GamePanel.Overview);
        await PrintFrameCost("map with the drawer open");
        game.GetNode<Drawer>("%Drawer").Close();
        game.ShowView(GameScreen.MainView.Village);

        for (var seed = 1; seed <= 3; seed++)
        {
            VillageGround.For(seed, Find<VillageView>().Size);
            GD.Print($"[frames] ground paint {seed}: {VillageGround.LastPaintMilliseconds:0.0} ms");
        }
    }

    /// <summary>
    /// What a late match costs on a real connection: "-- --net-report". Everyone but the guest being measured is an AI, and
    /// the guest watches an AI's view, so its state is as busy as a real player's. No pass or fail, since it reports numbers.
    /// </summary>
    private async Task NetReport()
    {
        UseSettings("user://uitest_settings.cfg");
        _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        AddChild(_main);
        await Frames();

        const int port = 24999;
        var data = GameData.Load(new GodotDataSource());
        var slots = Enumerable.Range(1, 4).Select(i => new PlayerSlot($"AI {i}", Nation.Valdren, IsAi: true, AiDifficulty.Hard)).ToList();
        var state = GameFactory.CreateMatch(new GameSettings(3600, 3, MapSize.Medium, 12), slots, data);
        var local = new LocalGameSession(state, data, 1);
        foreach (var id in state.Players.Keys)
            local.AddAgent(new Valkaris.Ai.AiPlayer(new Valkaris.Ai.AiView(state, data, id, command => local.SendAs(id, command))));
        var network = EnetHost.Listen(port)!;
        var host = new HostGameSession(local, network, (_, _) => throw new InvalidOperationException());

        var started = new MatchStartMessage?[3];
        var connections = new List<EnetClient>();
        var peers = new List<int>();
        network.PeerConnected += peers.Add;
        for (var i = 0; i < 3; i++)
        {
            var connection = EnetClient.Connect("127.0.0.1", port)!;
            var index = i;
            connection.Received += bytes => { if (WireCodec.DecodeFromHost(bytes) is MatchStartMessage start) started[index] ??= start; };
            connections.Add(connection);
        }
        while (peers.Count < 3)
        {
            network.Poll();
            connections.ForEach(c => c.Poll());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        for (var i = 0; i < 3; i++)
            host.AddClient(peers[i], i + 2);
        while (started.Any(s => s is null))
        {
            connections.ForEach(c => c.Poll());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        // On to the late match, in big steps. The clients have nothing to do yet.
        var sessions = new List<ClientGameSession>();
        for (var i = 0; i < 3; i++)
        {
            sessions.Add(new ClientGameSession(connections[i], data, started[i]!));
        }
        while (state.ElapsedTime < 30 * 60 && !state.IsMatchOver)
        {
            host.Tick(1);
            sessions.ForEach(s => s.Tick(0));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        GD.Print($"[net] late match: {state.ElapsedTime / 60:0} min, {state.Villages.Count} villages, {state.Movements.Count} movements, {state.Reports.Count} reports, over: {state.IsMatchOver}");

        // The busiest view left in the match.
        var guest = sessions.Where(s => s.State.Players[s.LocalPlayerId].CapitalVillageId is not null)
            .OrderByDescending(s => s.State.Villages.Values.Count(v => v.OwnerId == s.LocalPlayerId))
            .First();
        GD.Print($"[net] measuring a guest with {guest.State.Villages.Values.Count(v => v.OwnerId == guest.LocalPlayerId)} villages");
        _main.PlayNetworkMatch(guest, () => { });
        await Frames();
        network.TakeBytesSent();
        var applies = new List<double>();
        var sizes = new List<int>();
        double hostSeconds = 0, screenSeconds = 0;
        var frames = 0;
        var last = guest.LastApplySeconds;
        var from = Time.GetTicksMsec();
        var game = Find<GameScreen>();
        foreach (var view in new[] { GameScreen.MainView.Village, GameScreen.MainView.World })
        {
            game.ShowView(view);
            var viewFrom = Time.GetTicksMsec();
            while (Time.GetTicksMsec() - viewFrom < 15000)
            {
                var ticked = System.Diagnostics.Stopwatch.GetTimestamp();
                host.Tick(GetProcessDeltaTime());
                hostSeconds += System.Diagnostics.Stopwatch.GetElapsedTime(ticked).TotalSeconds;
                sessions.Where(s => s != guest).ToList().ForEach(s => s.Tick(0));
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                screenSeconds += game.LastFrameSeconds;
                frames++;
                if (guest.LastApplySeconds != last)
                {
                    last = guest.LastApplySeconds;
                    applies.Add(last * 1000);
                    sizes.Add(guest.LastMessageBytes);
                }
            }
        }

        var seconds = (Time.GetTicksMsec() - from) / 1000.0;
        var sent = network.TakeBytesSent();
        GD.Print($"[net] sent {sent / seconds / 3 / 1024:0.0} KB/s per client after compression");
        GD.Print($"[net] update before compression: {sizes.Average() / 1024:0.0} KB average, {sizes.Max() / 1024:0.0} KB largest, {sizes.Count / seconds:0.0} per second");
        GD.Print($"[net] client apply: {applies.Average():0.00} ms average, {applies.Max():0.00} ms worst, {applies.Sum() / frames:0.000} ms per frame");
        GD.Print($"[net] host tick with 3 clients: {hostSeconds * 1000 / frames:0.000} ms per frame; client screen update including apply: {screenSeconds * 1000 / frames:0.000} ms per frame; {frames / seconds:0.0} fps");
        host.Stop();
    }

    /// <summary>
    /// One window of a real match over the real network, driven through the real buttons: "-- --net-window=host" or "guest".
    /// The guest surrenders after a while, and both windows must then show the results.
    /// </summary>
    private async Task NetWindow(string role)
    {
        UseSettings($"user://uitest_{role}.cfg");
        _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        AddChild(_main);
        await Frames();
        DisplayServer.WindowSetTitle(role);
        async Task Until(Func<bool> condition, string what, double limit = 60)
        {
            var from = Time.GetTicksMsec();
            while (!condition())
            {
                if ((Time.GetTicksMsec() - from) / 1000.0 > limit)
                {
                    Expect(false, $"{role}: {what}");
                    return;
                }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

        var hosting = role == "host";
        Press(Find<StartScreen>().GetNode<Button>(hosting ? "%Host" : "%Join"));
        await Frames();
        var connect = Find<ConnectScreen>();
        var name = connect.GetNode<LineEdit>("%PlayerName");
        name.Text = hosting ? "Host Window" : "Guest Window";
        name.EmitSignal(LineEdit.SignalName.TextChanged, name.Text);
        if (!hosting)
        {
            var address = connect.GetNode<LineEdit>("%Address");
            address.Text = "127.0.0.1";
            address.EmitSignal(LineEdit.SignalName.TextChanged, address.Text);
        }
        Press(connect.GetNode<Button>("%Continue"));
        await Frames();

        var lobby = Find<LobbyScreen>();
        if (hosting)
        {
            await Until(() => !lobby.GetNode<Button>("%Start").Disabled, "a guest arrives");
            await Shot("window_host_lobby");
            Press(lobby.GetNode<Button>("%Start"));
        }
        else
        {
            await Until(() => lobby.GetNode<VBoxContainer>("%Slots").GetChild(1).GetNode<Label>("Player").Text == "Guest Window", "the guest gets a seat");
            await Shot("window_guest_lobby");
        }
        await Until(() => _main.GetChildren().OfType<GameScreen>().Any(), "the match starts");
        var game = Find<GameScreen>();
        Expect(hosting ? game.Session is HostGameSession : game.Session is ClientGameSession, $"{role}: the right kind of session");
        await Wait(3);

        var villageId = game.CurrentVillageId!.Value;
        game.Session.Send(new BuildCommand(villageId, BuildingType.Farm));
        game.Session.Send(new TrainCommand(villageId, UnitType.Spearman, 1));
        await Until(() => game.Session.State.Villages[villageId].BuildQueue.Count > 0, "a build order takes effect", 5);
        await Shot($"window_{role}_village");
        game.ShowView(GameScreen.MainView.World);
        await Wait(1);
        await Shot($"window_{role}_map");

        if (!hosting)
        {
            await Wait(5);
            Press(Find<ResourceBar>().GetNode<Button>("%Pause"));
            await Frames();
            Press(game.GetNode<Overlay>("%PauseOverlay").GetNode<Button>("%Tertiary"));
            await Frames();
            Press(game.GetNode<Overlay>("%SurrenderOverlay").GetNode<Button>("%Action"));
        }

        await Until(() => game.Session.State.IsMatchOver && game.GetNode<MatchEndScreen>("%MatchEndScreen").Visible, "the results show");
        await Shot($"window_{role}_end");
        Expect(game.Session.State.Outcome!.WinnerIds.Contains(1), $"{role}: the host wins after the guest surrenders");
        await Wait(1);
        Press(game.GetNode<MatchEndScreen>("%MatchEndScreen").GetNode<Button>("%Back"));
        await Frames();
        Expect(Find<StartScreen>() is not null, $"{role}: back at the start");
    }

    private async Task PrintFrameCost(string name)
    {
        const int frames = 300;
        await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);

        double process = 0, drawCalls = 0, objects = 0;
        var started = Time.GetTicksMsec();
        for (var i = 0; i < frames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            process += Find<GameScreen>().LastFrameSeconds * 1000;
            drawCalls += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
            objects += Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        }

        var seconds = (Time.GetTicksMsec() - started) / 1000.0;
        GD.Print($"[frames] {name}: {frames / seconds:0.0} fps, {process / frames:0.000} ms screen update, {drawCalls / frames:0} draw calls, {objects / frames:0} nodes");
    }

    private async Task Run()
    {
        // Never touches the language saved for real play.
        UseSettings("user://uitest_settings.cfg");
        DirAccess.RemoveAbsolute(Language.SettingsPath);
        TestFullscreenDefault();
        await TestIntro();

        _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        _main.IdleOpponents = true;
        AddChild(_main);
        await Frames();
        TestCulture();
        await TestLanguage();
        await TestSettingsFromStart();
        await TestNationPicker();
        await TestQuit();

        await TestFullscreenKey();
        await TestStartVillageName();
        await TestLobbyScreens();
        await TestGuestMatch();
        await TestHostMatch();
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

        session.Tick(BuildingRules.BuildTimeSeconds(session.Data, BuildingType.Woodcutter, 2, NationRules.Of(session.Data, session.State, 1)));
        await Frames();
        Expect(panel.GetNode<Label>("%Level").Text == L.T("common.level", ("level", "2")), "Level changes when the build completes");

        var woodLabel = Find<ResourceBar>().GetNode<Label>("%Chips/Wood/Amount");
        var shownWood = woodLabel.Text;
        session.Tick(10);
        await Frames();
        Expect(woodLabel.Text != shownWood, "Resource amount ticks up");

        var rate = Find<ResourceBar>().GetNode<Label>("%Chips/Stone/Rate").Text;
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
        Expect(wall.Stage == 0 && wall.GetNode<TextureRect>("%Icon").Texture.ResourcePath == VillageArt.PlotSprite, "An unbuilt building is an empty plot");

        var tiles = Find<VillageView>().GetChildren().OfType<BuildingTile>().ToList();
        Expect(tiles.Count == Enum.GetValues<BuildingType>().Length, "Every building has a tile");
        Expect(!tiles.Any(t => tiles.Any(o => o != t && t.GetRect().Intersects(o.GetRect()))), "No building tiles overlap");
        SelectTile(BuildingType.Watchtower);
        await Frames();
        Expect(panel.GetNode<Label>("%Name").Text == UiContent.Buildings[BuildingType.Watchtower].Name, "The Watchtower can be selected");
        Expect(panel.GetNode<Button>("%Upgrade").Text == L.T("upgrade.build"), $"The Watchtower can be built, got '{panel.GetNode<Button>("%Upgrade").Text}'");

        await TestBuildingStages();

        SelectTile(BuildingType.Wall);
        await Frames();
        panel.GetNode<Button>("%Upgrade").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(wall.GetNode<ProgressBar>("%Progress").Visible, "Tile under construction shows progress");
        Expect(wall.GetNode<Control>("%Scaffold").Visible, "A building under construction shows scaffolding");
        await Shot("stage-building");
        await Shot("village");

        var ground = Find<VillageView>().Ground;
        Find<GameScreen>().ShowView(GameScreen.MainView.World);
        await Frames();
        Find<GameScreen>().ShowView(GameScreen.MainView.Village);
        await Frames();
        Expect(Find<VillageView>().Ground == ground, "The ground stays the same between visits");

        await TestTroops();
        await TestSend();
        await TestReports();
        await TestFeedback();
        await TestPause();
        TestDevOptions();
        await TestViewSwitching();
        await TestThirdParty();
        await TestTopBar();
        await TestPopups();
        await TestHoverCard();
        await TestBuildingCard();
        await TestDrawer();
        await TestSpectator(GameData.Load(new GodotDataSource()));
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

        game.ShowPanel(GameScreen.GamePanel.Send);
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

    private async Task TestLobbyScreens()
    {
        var data = GameData.Load(new GodotDataSource());
        var hash = Handshake.DataHash(new GodotDataSource());

        Press(Find<StartScreen>().GetNode<Button>("%Host"));
        await Frames();
        var connect = Find<ConnectScreen>();
        var carry = connect.GetNode<Button>("%Continue");
        var player = connect.GetNode<LineEdit>("%PlayerName");
        Expect(!connect.GetNode<Control>("%AddressRow").Visible, "Hosting asks for no address");
        player.Text = "";
        player.EmitSignal(LineEdit.SignalName.TextChanged, "");
        Expect(carry.Disabled, "A name is needed to go on");
        player.Text = "Oğuz";
        player.EmitSignal(LineEdit.SignalName.TextChanged, "Oğuz");
        Expect(!carry.Disabled, "A good name allows going on");
        await Shot("connect_host");
        Press(connect.GetNode<Button>("%Back"));
        await Frames();

        Press(Find<StartScreen>().GetNode<Button>("%Join"));
        await Frames();
        connect = Find<ConnectScreen>();
        connect.GetNode<LineEdit>("%PlayerName").Text = "Ayla";
        var address = connect.GetNode<LineEdit>("%Address");
        address.EmitSignal(LineEdit.SignalName.TextChanged, "");
        Expect(connect.GetNode<Button>("%Continue").Disabled, "An address is needed to join");
        address.Text = "192.168.1.20";
        address.EmitSignal(LineEdit.SignalName.TextChanged, "192.168.1.20");
        Expect(!connect.GetNode<Button>("%Continue").Disabled, "A good address allows going on");
        await Shot("connect_join");
        Press(connect.GetNode<Button>("%Back"));
        await Frames();

        // A host's lobby, with someone joining.
        var network = new MemoryNetwork();
        _main.OpenHostLobby(network.Host, "Oğuz", "Evim", new[] { "192.168.1.20", "100.70.1.2" }, Handshake.DefaultPort);
        await Frames();
        var lobby = Find<LobbyScreen>();
        Expect(lobby.GetNode<Button>("%Start").Disabled, "A lone host can't start");
        Expect(lobby.GetNode<Label>("%Status").Text.Contains("100.70.1.2"), "The host sees the addresses to share");
        var guest = new LobbyClient(network.Connect(), data, hash, "Ayla", null);
        guest.Poll();
        await Frames();
        guest.Poll();
        Expect(!lobby.GetNode<Button>("%Start").Disabled, "A second player allows starting");
        var row = lobby.GetNode<VBoxContainer>("%Slots").GetChild(1);
        Expect(row.GetNode<Label>("Player").Text == "Ayla" && guest.Lobby is not null, "The guest takes a seat");

        var aiRow = lobby.GetNode<VBoxContainer>("%Slots").GetChild(2);
        var kind = aiRow.GetNode<OptionButton>("Kind");
        kind.Select(kind.ItemCount - 2);
        kind.EmitSignal(OptionButton.SignalName.ItemSelected, kind.ItemCount - 2);
        await Frames();
        guest.Poll();
        Expect(guest.Lobby!.Slots[2].Kind == SlotKind.Ai && guest.Lobby.Slots[2].Difficulty == Enum.GetValues<AiDifficulty>()[^1], "A seat can hold an AI of its own difficulty");
        var map = lobby.GetNode<OptionButton>("%MapSize");
        map.Select(1);
        map.EmitSignal(OptionButton.SignalName.ItemSelected, 1);
        await Frames();
        guest.Poll();
        Expect(guest.Lobby.Settings.MapSize == Enum.GetValues<MapSize>()[1], "The guest sees the host's settings");
        await Shot("lobby_host");
        Press(lobby.GetNode<Button>("%Leave"));
        await Frames();
        Expect(Find<StartScreen>() is not null, "Leaving the lobby returns to the start");

        // A guest's lobby: it can look, and not touch.
        network = new MemoryNetwork();
        var host = new LobbyHost(network.Host, data, hash, "Oğuz", null);
        host.SetSlot(3, SlotKind.Ai, AiDifficulty.Easy);
        var connection = network.Connect();
        _main.OpenClientLobby(connection, "Ayla", null);
        await Frames();
        lobby = Find<LobbyScreen>();
        host.Poll();
        await Frames();
        Expect(lobby.GetNode<OptionButton>("%Duration").Disabled && !lobby.GetNode<Button>("%Start").Visible, "A guest can't change the settings or start");
        Expect(!lobby.GetNode<VBoxContainer>("%Slots").GetChild(3).GetNode<OptionButton>("Kind").Visible, "A guest can't change the seats");
        await Shot("lobby_client");
        host.SetSlot(1, SlotKind.Closed);
        await Frames();
        Expect(Find<ConnectScreen>().GetNode<Label>("%Reason").Text == L.T("join.removed_by_host"), "Being removed says so");
        Press(Find<ConnectScreen>().GetNode<Button>("%Back"));
        await Frames();

        // Different data is refused with a clear message.
        network = new MemoryNetwork();
        _ = new LobbyHost(network.Host, data, "other", "Oğuz", null);
        _main.OpenClientLobby(network.Connect(), "Ayla", null);
        await Frames();
        network.Host.Poll();
        await Frames();
        Expect(Find<ConnectScreen>().GetNode<Label>("%Reason").Text == L.T("join.data_mismatch"), "Different data is refused with a reason");
        Press(Find<ConnectScreen>().GetNode<Button>("%Back"));
        await Frames();
    }

    private async Task TestGuestMatch()
    {
        var data = GameData.Load(new GodotDataSource());
        var network = new MemoryNetwork();
        var lobby = new LobbyHost(network.Host, data, Handshake.DataHash(new GodotDataSource()), "Oğuz", null);
        var connection = network.Connect();
        _main.OpenClientLobby(connection, "Ayla", null);
        await Frames();
        lobby.Poll();
        await Frames();
        var host = lobby.Start(5, (session, id) => throw new InvalidOperationException());
        await Frames();

        var game = Find<GameScreen>();
        var session = (ClientGameSession)game.Session;
        async Task Steps(double seconds = 0.5)
        {
            host.Tick(seconds);
            await Frames();
        }

        await Steps();
        var menu = Find<ResourceBar>().GetNode<Button>("%Pause");
        var overlay = game.GetNode<Overlay>("%PauseOverlay");
        Expect(!session.CanPause && menu.Visible && menu.Text == L.T("bar.menu"), "Multiplayer offers a menu in place of pause");
        Press(menu);
        await Frames();
        var before = host.State.ElapsedTime;
        await Steps();
        Expect(overlay.Visible && host.State.ElapsedTime > before && !session.IsPaused, "The match runs on behind the menu");
        await Shot("mp_menu");
        Press(overlay.GetNode<Button>("%Action"));
        await Frames();
        Expect(!overlay.Visible, "The menu closes");

        var villageId = game.CurrentVillageId!.Value;
        Expect(session.State.Villages[villageId].OwnerId == session.LocalPlayerId, "A guest starts in their own village");
        var other = session.State.Villages.Values.First(v => v.OwnerId == 1);
        Expect(session.State.Villages[other.Id].Troops.Values.Sum() == 0, "A guest's state holds no rival troops");

        // Everything a guest can look at must survive the state being replaced under it.
        session.Send(new BuildCommand(villageId, BuildingType.Farm));
        await Steps();
        await Steps();
        Expect(session.State.Villages[villageId].BuildQueue.Count == 1, "A guest's command reaches the host and comes back");
        foreach (var tab in Enum.GetValues<GameScreen.GamePanel>())
        {
            game.ShowPanel(tab);
            await Steps();
        }
        game.ShowView(GameScreen.MainView.World);
        await Steps();
        await Shot("mp_map");
        game.ShowView(GameScreen.MainView.Village);
        game.ShowPanel(GameScreen.GamePanel.Building);

        host.Stop();
        await Frames();
        var leave = game.GetNode<Overlay>("%LeaveOverlay");
        Expect(leave.Visible && leave.GetNode<Label>("%Message").Text == L.T("overlay.host_left.message"), "A guest is told when the host leaves");
        await Shot("mp_host_left");
        Press(leave.GetNode<Button>("%Action"));
        await Frames();
        Expect(Find<StartScreen>() is not null, "The message leads back to the start");
    }

    private async Task TestHostMatch()
    {
        var data = GameData.Load(new GodotDataSource());
        var hash = Handshake.DataHash(new GodotDataSource());
        var network = new MemoryNetwork();
        _main.OpenHostLobby(network.Host, "Oğuz", null, Array.Empty<string>(), Handshake.DefaultPort);
        await Frames();
        var lobby = Find<LobbyScreen>();
        var guests = new[] { "Ayla", "Deniz" }.Select(name => new LobbyClient(network.Connect(), data, hash, name, null)).ToList();
        var sessions = new List<ClientGameSession>();
        foreach (var guest in guests)
        {
            guest.MatchStarted += sessions.Add;
            guest.Poll();
        }
        await Frames();

        // Each seat's nation shows for everyone, and each person sets only what's theirs.
        OptionButton NationOf(int slot) => lobby.GetNode<OptionButton>($"%Slots/Slot{slot}/Nation");
        lobby.GetNode<OptionButton>("%Slots/Slot3/Kind").Select(1);
        lobby.GetNode<OptionButton>("%Slots/Slot3/Kind").EmitSignal(OptionButton.SignalName.ItemSelected, 1);
        await Frames();
        Expect(NationOf(0).Selected == StartScreen.NationIndex(UserSettings.Nation) && !NationOf(0).Disabled, "The host's seat starts on the remembered nation");
        Expect(NationOf(1).Disabled && NationOf(2).Disabled, "The host can't set a guest's nation");
        Expect(!NationOf(3).Disabled && NationOf(3).Selected == 0, "An AI is Random until the host chooses");
        NationOf(3).Select(StartScreen.NationIndex(Nation.Karuk));
        NationOf(3).EmitSignal(OptionButton.SignalName.ItemSelected, NationOf(3).Selected);
        guests[0].ChooseNation(Nation.Solari);
        await Frames();
        foreach (var guest in guests)
            guest.Poll();
        await Frames();
        Expect(NationOf(1).Selected == StartScreen.NationIndex(Nation.Solari), "A guest's choice shows in the host's lobby");
        Expect(guests[1].Lobby!.Slots[3].Nation == Nation.Karuk && guests[1].Lobby!.Slots[1].Nation == Nation.Solari, "Other guests see every nation");
        Expect(guests[1].OwnSlot == 2, "A guest knows their own seat");
        CheckLayout("lobby nations");
        await Shot("lobby-nations");

        Press(lobby.GetNode<Button>("%Start"));
        await Frames();
        foreach (var guest in guests)
            guest.Poll();

        var game = Find<GameScreen>();
        var host = (HostGameSession)game.Session;
        Expect(host.State.Players[2].Nation == Nation.Solari && host.State.Players[4].Nation == Nation.Karuk, "The match uses the lobby's nations");
        Expect(sessions.Count == 2 && !host.CanPause, "The match starts for everyone, and can't be paused");
        Expect(host.State.Settings.SpeedMultiplier == data.Game.SpeedMultipliers[0], "Dev settings don't reach a multiplayer match");
        Expect(Find<ResourceBar>().GetNode<Button>("%Pause").Text == L.T("bar.menu"), "The host has the menu as well");

        sessions[0].Leave();
        await Frames();
        Expect(host.State.Players[sessions[0].LocalPlayerId].IsAi, "An AI takes the seat of someone who leaves");
        sessions[1].Tick(0);
        Expect(sessions[1].State.Players[sessions[0].LocalPlayerId].IsAi, "Others can see that");

        // The multiplayer menu doesn't pause, so nothing else is holding the game still behind it.
        Drawer.Instant = false;
        await OverlaysByMouse(game, 1);
        await ClickAndSettle(Find<ResourceBar>().GetNode<Button>("%Pause"));
        var viewBehindMenu = game.CurrentView;
        await PressKeyEvent(Key.M);
        Expect(game.CurrentView == viewBehindMenu, "Keys don't reach the game behind the multiplayer menu");
        await EscapeAndSettle();
        Drawer.Instant = true;

        var heardLoss = false;
        sessions[1].ConnectionLost += () => heardLoss = true;
        Press(Find<ResourceBar>().GetNode<Button>("%Pause"));
        await Frames();
        var overlay = game.GetNode<Overlay>("%PauseOverlay");
        Expect(overlay.GetNode<Button>("%Tertiary").Visible, "The menu offers surrender");
        Press(overlay.GetNode<Button>("%Secondary"));
        await Frames();
        var leave = game.GetNode<Overlay>("%LeaveOverlay");
        Expect(leave.GetNode<Label>("%Message").Text == L.T("overlay.leave.message_host"), "The host is warned that leaving ends the match");
        await Shot("mp_host_leave");
        Press(leave.GetNode<Button>("%Action"));
        await Frames();
        sessions[1].Tick(0);
        Expect(heardLoss && Find<StartScreen>() is not null, "The host leaving reaches the others");
    }

    /// <summary>The bar with everything showing at once, as in the screenshot that found the overflow.</summary>
    private async Task TestHoverCard()
    {
        var game = Find<GameScreen>();
        var session = game.Session;
        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var card = world.GetNode<VillagePopup>("Popup");
        var send = game.GetNode<PopupFrame>("%SendPopup");
        var panel = game.GetNode<SendPanel>("%Send");
        game.ShowPanel(GameScreen.GamePanel.Reports);
        game.ShowPanel(GameScreen.GamePanel.Building);
        game.GetNode<PopupFrame>("%BuildingPopup").Close();
        game.ShowView(GameScreen.MainView.World);
        world.ClearSelection();
        await Frames();

        var rival = session.State.Villages.Values.First(v => !v.IsBarbarian && v.OwnerId != session.LocalPlayerId);
        var own = session.State.Villages[game.CurrentVillageId!.Value];
        Vector2 At(Village village) => world.WorldToView(WorldMapView.VillageWorldPosition(village));
        void Hover(Vector2 point) => world._GuiInput(new InputEventMouseMotion { Position = point });
        async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

        var empty = new Vector2(world.Size.X - 2, world.Size.Y - 2);
        Hover(At(rival));
        await Frames();
        Expect(!card.Visible, "The card waits a moment");
        await Wait(0.45);
        Expect(card.Visible && card.GetNode<Label>("%Name").Text == rival.Name, "Resting on a village opens its card");
        Expect(card.GetNode<Button>("%Attack").Visible && card.GetNode<Button>("%Raid").Visible && card.GetNode<Button>("%Scout").Visible, "A rival's card offers Attack, Raid and Scout");
        Expect(new Rect2(Vector2.Zero, world.Size).Encloses(card.GetRect()), "The card stays in the view");
        await Shot("hover-card");

        Hover(empty);
        world.TestCursorOnCard = true;
        await Wait(0.45);
        Expect(card.Visible, "The card stays while the cursor is on it");
        world.TestCursorOnCard = false;
        await Wait(0.45);
        Expect(!card.Visible, "The card closes when the cursor leaves");

        // Shortcuts act on what is under the cursor, with no wait.
        foreach (var (key, mission) in new[] { (Key.A, MovementType.Attack), (Key.R, MovementType.Raid), (Key.S, MovementType.Scout) })
        {
            Hover(At(rival));
            PressKey(game, key);
            await Frames();
            await Frames();
            Expect(send.Visible && panel.GetNode<OptionButton>("%Mission").Text == UiContent.MissionName(mission), $"{key} opens the send popup for {mission}");
            Expect(panel.GetNode<Label>("%TargetLine").Text.Contains(rival.Name), $"{key} aims at the village under the cursor");
            Expect(!send.GetGlobalRect().Intersects(world.VillageScreenRect(rival.Id)), $"The send popup doesn't cover its target: {send.GetGlobalRect()} and {world.VillageScreenRect(rival.Id)}");
            Expect(GetViewportRect().Encloses(send.GetGlobalRect()), "The send popup stays in the window");
            if (key == Key.R)
                await Shot("send-beside-target");
            PressKey(game, Key.Escape);
            await Frames();
        }

        Hover(At(rival));
        PressKey(game, Key.T);
        await Frames();
        Expect(!send.Visible, "T on a rival opens nothing");
        PressKey(game, Key.Enter);
        await Frames();
        Expect(game.CurrentView == GameScreen.MainView.World, "Enter on a rival doesn't enter it");

        Hover(At(own));
        PressKey(game, Key.A);
        await Frames();
        Expect(!send.Visible, "A on your own village opens nothing");
        PressKey(game, Key.Enter);
        await Frames();
        Expect(game.CurrentView == GameScreen.MainView.Village && game.CurrentVillageId == own.Id, "Enter walks into your own village");

        // Nothing fires while typing.
        game.ShowView(GameScreen.MainView.World);
        Hover(At(rival));
        PressKey(game, Key.R);
        await Frames();
        await Frames();
        var field = panel.GetNode<SpinBox>("%Units/Spearman/Amount").GetLineEdit();
        field.GrabFocus();
        await Frames();
        Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = Key.A, PhysicalKeycode = Key.A, Unicode = 'a' });
        Input.FlushBufferedEvents();
        await Frames();
        Expect(panel.GetNode<OptionButton>("%Mission").Text == UiContent.MissionName(MovementType.Raid), "A goes to the field while typing");
        field.ReleaseFocus();
        send.Close();
        Hover(empty);
        world.ClearSelection();
        game.ShowView(GameScreen.MainView.Village);
        await Frames();
    }

    private async Task TestBuildingCard()
    {
        var game = Find<GameScreen>();
        var view = game.GetNode<VillageView>("%VillageView");
        var card = view.Card;
        async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

        game.ShowView(GameScreen.MainView.Village);
        game.ShowPanel(GameScreen.GamePanel.Building);
        game.GetNode<PopupFrame>("%BuildingPopup").Close();
        await Frames();

        view.TestHoveredBuilding = BuildingType.Barracks;
        await Wait(0.45);
        Expect(card.Visible && card.GetNode<Label>("Box/Name").Text == UiContent.Buildings[BuildingType.Barracks].Name, "Hovering a building shows its card");
        Expect(new Rect2(Vector2.Zero, view.Size).Encloses(card.GetRect()), "The building card stays in the view");
        Expect(!card.GetGlobalRect().Intersects(view.BuildingScreenRect(BuildingType.Barracks)), "The card doesn't cover its building");
        await Shot("building-card");
        view.TestHoveredBuilding = null;
        view.TestCursorOnCard = true;
        await Wait(0.45);
        Expect(card.Visible, "The building card stays while the cursor is on it");

        Press(card.GetNode<Button>("Box/Action"));
        await Frames();
        await Frames();
        var troops = game.GetNode<PopupFrame>("%TroopsPopup");
        Expect(troops.Visible, "Train troops opens the training popup");
        Expect(GetViewportRect().Encloses(troops.GetGlobalRect()), "The training popup stays in the window");
        await Shot("train-popup");
        troops.Close();
        view.TestCursorOnCard = false;
        await Wait(0.45);
        Expect(!card.Visible, "The building card closes when the cursor leaves");

        view.TestHoveredBuilding = BuildingType.Marketplace;
        await Wait(0.45);
        Press(card.GetNode<Button>("Box/Action"));
        await Frames();
        await Frames();
        Expect(game.IsShowing(GameScreen.GamePanel.Send), "Send resources opens the transfer popup");
        game.GetNode<PopupFrame>("%SendPopup").Close();
        view.TestHoveredBuilding = null;
        await Wait(0.45);

        view.TestHoveredBuilding = BuildingType.Farm;
        await Wait(0.45);
        Expect(!card.GetNode<Button>("Box/Action").Visible, "Other buildings have no action on their card");
        view.TestHoveredBuilding = null;
        await Wait(0.45);

        // A click opens the full popup, beside the building, with the village's details only on the Main Building.
        var building = game.GetNode<PopupFrame>("%BuildingPopup");
        foreach (var (type, details) in new[] { (BuildingType.Farm, false), (BuildingType.MainBuilding, true) })
        {
            view.GetNode<BuildingTile>(type.ToString()).EmitSignal(BaseButton.SignalName.Pressed);
            await Frames();
            await Frames();
            Expect(building.Visible, $"Clicking the {type} opens its popup");
            Expect(GetViewportRect().Encloses(building.GetGlobalRect()), "The building popup stays in the window");
            Expect(game.GetNode<Label>("%VillageInfo").Visible == details, $"The village's details show only on the Main Building ({type})");
        }
        await Shot("building-popup");
        building.Close();

        var session = game.Session;
        var village = session.State.Villages[game.CurrentVillageId!.Value];
        if (village.BuildQueue.Count == 0)
            session.Send(new BuildCommand(village.Id, BuildingType.Farm));
        await Frames();
        Expect(view.QueueLine.Visible == (village.BuildQueue.Count > 0), "The queue line follows the build queue");
    }

    private async Task TestDrawer()
    {
        var game = Find<GameScreen>();
        var drawer = game.GetNode<Drawer>("%Drawer");
        var bar = Find<ResourceBar>();
        var tabs = drawer.GetNode<Control>("Tabs");
        drawer.Close();
        await Frames();

        Press(drawer.GetNode<Button>("Tabs/OverviewTab"));
        await Frames();
        Expect(drawer.IsOpen && game.IsShowing(GameScreen.GamePanel.Overview), "A tab opens the drawer");
        Expect(game.GetNode<OverviewPanel>("%Overview").GetNode<Button>("%VillageGrid/Village0").Text == UiContent.OwnVillages(game.Session)[0].Name, "Overview lists my villages");
        Press(drawer.GetNode<Button>("Tabs/OverviewTab"));
        await Frames();
        Expect(!drawer.IsOpen, "The active tab closes it");

        Press(drawer.GetNode<Button>("Tabs/ReportsTab"));
        await Frames();
        PressKey(game, Key.Escape);
        await Frames();
        Expect(!drawer.IsOpen, "Esc closes the drawer");

        var strip = game.GetNode<Control>("%VillageStrip");
        var send = game.GetNode<PopupFrame>("%SendPopup");
        var heights = new[] { ("min", Drawer.MinFraction), ("default", 0.2f), ("max", Drawer.MaxFraction) };
        foreach (var (name, fraction) in heights)
        {
            drawer.SetHeightFraction(fraction);
            game.ShowPanel(GameScreen.GamePanel.Overview);
            game.ShowPanel(GameScreen.GamePanel.Send);
            await Frames();
            await Frames();
            var sheet = drawer.SheetRect;
            var screen = GetViewportRect().Size;
            Expect(Mathf.Abs(sheet.Size.Y - fraction * game.GetNode<Control>("%MainArea").Size.Y) < 1 && sheet.End.Y <= screen.Y + 1, $"The drawer takes its height at {name}");
            Expect(bar.GetGlobalRect().End.Y <= tabs.GetGlobalRect().Position.Y, $"The drawer leaves the top bar clear at {name}");
            Expect(!strip.Visible || strip.GetGlobalRect().End.Y <= tabs.GetGlobalRect().Position.Y, $"The drawer leaves the village strip clear at {name}");
            Expect(GetViewportRect().Encloses(send.GetGlobalRect()) && send.ZIndex + send.GetParent<Control>().ZIndex > drawer.ZIndex, $"A popup stays above the drawer at {name}");
            await Shot($"drawer-{name}");
            send.Close();
        }

        drawer.SetHeightFraction(Drawer.MaxFraction + 0.5f);
        Expect(Mathf.IsEqualApprox(drawer.HeightFraction, Drawer.MaxFraction), "Dragging stops at the maximum");
        Expect(Mathf.IsEqualApprox(UserSettings.DrawerHeight, Drawer.MaxFraction), "The height is remembered");
        drawer.SetHeightFraction(0.2f);
        game.ShowPanel(GameScreen.GamePanel.Reports);
        await Frames();
        await Shot("drawer-reports");
        drawer.Close();
        await Frames();
        await Shot("drawer-closed");
    }

    /// <summary>After surrender, with the option on: everything that moves, and every battle.</summary>
    private async Task TestSpectator(GameData data)
    {
        foreach (var seeAll in new[] { true, false })
        {
            var slots = new[] { new PlayerSlot("You", Nation.Valdren, false), new PlayerSlot("Rival", Nation.Valdren, true), new PlayerSlot("Other", Nation.Valdren, true) };
            var state = GameFactory.CreateMatch(new GameSettings(3600, 1, MapSize.Small, 11, seeAll), slots, data);
            var session = new LocalGameSession(state, data, 1);
            var game = GD.Load<PackedScene>("res://scenes/Game.tscn").Instantiate<GameScreen>();
            game.Init(session);
            AddChild(game);
            await Frames();

            var movements = game.GetNode<WorldMapView>("%WorldMapView").GetNode<MovementLayer>("World/Movements");
            var from = state.Villages.Values.First(v => v.OwnerId == 2);
            var to = state.Villages.Values.First(v => v.OwnerId == 3);
            var sent = session.SendAs(2, new SendTroopsCommand(from.Id, to.Id, MovementType.Raid, new Dictionary<UnitType, int> { [UnitType.Spearman] = 5 }));
            Expect(sent.Success, "The rival can send a raid");
            await Frames();
            Expect(movements.Entries.All(e => e.ShowRoute == false), "A player still in sees no route for others' movements");

            session.Send(new SurrenderCommand());
            await Frames();
            game.GetNode<Overlay>("%EliminatedOverlay").GetNode<Button>("%Action").EmitSignal(BaseButton.SignalName.Pressed);
            game.ShowView(GameScreen.MainView.World);
            await Frames();
            Expect(movements.Entries.Any(e => e.MovementId > 0 && e.ShowRoute) == seeAll, $"Spectators see all routes: {seeAll}");
            if (seeAll)
                await Shot("spectator-map");

            session.Tick(state.Movements.Max(m => m.ArrivalTime) - state.ElapsedTime + 1);
            game.ShowPanel(GameScreen.GamePanel.Reports);
            await Frames();
            var reports = game.GetNode<ReportsPanel>("%Reports");
            Expect(reports.GetNode<VBoxContainer>("%Rows").GetChildCount() > 0 == seeAll, $"Spectators see all battle reports: {seeAll}");
            if (seeAll)
                await Shot("spectator-reports");

            game.QueueFree();
            await Frames();
        }
    }

    private async Task TestPopups()
    {
        var game = Find<GameScreen>();
        var popup = game.GetNode<PopupFrame>("%SendPopup");

        game.ShowPanel(GameScreen.GamePanel.Send);
        await Frames();
        await Frames();
        var rect = popup.GetGlobalRect();
        Expect(popup.Visible && GetViewportRect().Encloses(rect), $"A popup stays inside the window: {rect}");
        Expect(rect.Position.Y >= Find<ResourceBar>().Size.Y, "A popup stays below the top bar");
        await Shot("popup-send");

        PressKey(game, Key.Escape);
        await Frames();
        Expect(!popup.Visible, "Esc closes a popup");

        game.ShowPanel(GameScreen.GamePanel.Training);
        await Frames();
        Press(game.GetNode<PopupFrame>("%TroopsPopup").GetNode<Button>("Frame/Header/Close"));
        await Frames();
        Expect(!game.IsShowing(GameScreen.GamePanel.Training), "The close button closes a popup");

        // Beside a target: right next to it, and never over it.
        var target = new Rect2(GetViewportRect().Size * 0.5f - new Vector2(30, 30), new Vector2(60, 60));
        popup.Open(target);
        await Frames();
        await Frames();
        rect = popup.GetGlobalRect();
        Expect(!rect.Intersects(target) && GetViewportRect().Encloses(rect), $"A popup opens beside its target: {rect}");
        await Shot("popup-beside");
        popup.Close();
        game.ShowPanel(GameScreen.GamePanel.Building);
        await Frames();
        await Shot("popup-building");
        game.GetNode<PopupFrame>("%BuildingPopup").Close();
        await Frames();
    }

    private async Task TestTopBar()
    {
        var game = Find<GameScreen>();
        var bar = Find<ResourceBar>();
        var session = game.Session;
        var state = session.State;
        var other = state.Villages.Values.First(v => !v.IsBarbarian && v.OwnerId != session.LocalPlayerId);
        var capital = bar.GetNode<Button>("%CapitalWarning");

        Expect(bar.GetNode<Label>("%Population").Text == Format.Whole(ScoreRules.For(state, session.Data, state.Players[1]).Population), "The bar shows my population");
        Expect(bar.GetNode<Label>("%Score").Text == Format.Whole((int)ScoreRules.For(state, session.Data, state.Players[1]).Score), "The bar shows my score");

        var badge = new Label { Name = "TestBadge", Text = "GOD MODE" };
        bar.GetNode<HBoxContainer>("%Box").AddChild(badge);
        bar.AddFixedItem(badge);
        Valkaris.Core.Dev.DevScenario.AddThirdPartyActivity(state, session.LocalPlayerId, other.Id);
        var deadline = state.ElapsedTime + 45;
        Valkaris.Core.Dev.DevScenario.SetCapitalDeadline(state, session.LocalPlayerId, deadline);
        await Frames();
        Press(game.GetNode<CapitalDialog>("%CapitalDialog").GetNode<Button>("%Later"));
        await Frames();
        Expect(capital.Visible && Find<ResourceBar>().GetNode<Button>("%Incoming").Visible && !game.GetNode<CapitalDialog>("%CapitalDialog").Visible, "Both warnings and the badge show at once");

        var window = GetWindow().Size;
        foreach (var size in new[] { window, new Vector2I(1024, 640), new Vector2I(1280, 720), new Vector2I(2560, 1080) })
        {
            GetWindow().Size = size;
            await Frames();
            await Frames();
            Expect(GetWindow().Size.X == size.X || DisplayServer.GetName() == "headless", $"The window took size {size}");
            Expect(bar.ContentWidth <= bar.GetNode<Control>("%Clip").Size.X + 1, $"Everything in the bar fits at {GetWindow().Size}: needs {bar.ContentWidth}, has {bar.GetNode<Control>("%Clip").Size.X}");
            Expect(bar.Size.X <= GetViewportRect().Size.X + 1 && game.Size.X <= GetViewportRect().Size.X + 1, $"The bar never pushes the layout wider at {GetWindow().Size}");
            Expect(capital.Text.Contains(Format.Time(deadline - state.ElapsedTime)), $"A warning always keeps its time: {capital.Text}");
            await Shot($"topbar-{size.X}");
        }
        GetWindow().Size = window;
        await Frames();

        Valkaris.Core.Dev.DevScenario.SetCapitalDeadline(state, session.LocalPlayerId, null);
        bar.RemoveFixedItem(badge);
        badge.Free();
        Valkaris.Core.Dev.DevScenario.RemoveMovementsOf(state, other.OwnerId);
        await Frames();
    }

    private async Task TestThirdParty()
    {
        var game = Find<GameScreen>();
        var state = game.Session.State;
        var layer = Find<MovementLayer>();
        var effects = Find<ClashLayer>();
        var other = state.Villages.Values.First(v => !v.IsBarbarian && v.OwnerId != game.Session.LocalPlayerId);
        game.ShowView(GameScreen.MainView.World);

        var marks = layer.Entries.Count;
        Valkaris.Core.Dev.DevScenario.AddThirdPartyActivity(state, game.Session.LocalPlayerId, other.Id);
        await Frames();
        Expect(layer.Entries.Count == marks + 2, "A sighted movement and an incoming raid show on the map");
        var sighted = layer.Entries.Single(e => e.MovementId < 0);
        Expect(!sighted.ShowRoute && sighted.Description == UiContent.MissionName(MovementType.Settle), "A sighting shows its mission and no route");
        var raid = layer.Entries.Single(e => e.MovementId > 0 && e.Description.Contains(UiContent.MissionName(MovementType.Raid)));
        Expect(raid.IconPath.Contains("mission_"), "An incoming raid's icon shows the mission, not its troops");
        Expect(effects.ClashesShowing == 1 && effects.NoticesShowing == 1, "Only a battle in sight shows a clash, and news shows a marker");
        await Shot("clash");

        var noticeWidth = effects.NoticeWidthOnScreen;
        var map = Find<WorldMapView>();
        var zoomBefore = map.Zoom;
        map._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = map.Size / 2 });
        await Frames();
        Expect(map.Zoom > zoomBefore && Mathf.IsEqualApprox(effects.NoticeWidthOnScreen, noticeWidth), "The notice marker stays one size on screen at any zoom");
        map.FitToView();

        game.Session.Tick(2.5);
        await Frames();
        Expect(effects.ClashesShowing == 0, "The clash effect ends");
        Expect(effects.NoticesShowing == 1, "The notice marker outlasts the clash");
        game.Session.Tick(13);
        await Frames();
        Expect(effects.NoticesShowing == 0, "The notice marker ends");
        Valkaris.Core.Dev.DevScenario.RemoveMovementsOf(state, other.OwnerId);
        await Frames();
        Expect(layer.Entries.Count == marks, "The markers go when the movements end");

        game.ShowPanel(GameScreen.GamePanel.Reports);
        var panel = Find<ReportsPanel>();
        panel.GetNode<Button>("%News").ButtonPressed = true;
        await Frames();
        var rows = panel.GetNode<VBoxContainer>("%NewsRows");
        var near = panel.GetNode<CheckBox>("%Near");
        Expect(near.Visible, "News can be limited to nearby villages");
        near.ButtonPressed = true;
        await Frames();
        var nearCount = rows.GetChildCount();
        near.ButtonPressed = false;
        await Frames();
        Expect(nearCount < rows.GetChildCount() || rows.GetChildCount() == 0, "The near filter leaves out far news");
        Expect(rows.GetChildCount() > 0 && rows.GetChild(0).GetNode<Label>("Text").Text.Contains(L.T("notice.attacked_won", ("attacker", ""), ("target", "")).Split(' ')[^1]), "News names the winner");
        Expect(rows.Visible && rows.GetChildCount() == NewsFeed.Build(game.Session.PublicNotices, game.Session.State.ElapsedTime).Count && rows.GetChildCount() > 0, "Reports has a news filter");
        await Shot("news");
        panel.GetNode<Button>("%Mine").ButtonPressed = true;
        await Frames();
        Expect(!rows.Visible, "The news filter switches back");
        game.ShowView(GameScreen.MainView.Village);
        game.ShowPanel(GameScreen.GamePanel.Building);
        await Frames();
    }

    private async Task TestCamera()
    {
        var game = Find<GameScreen>();
        game.ShowView(GameScreen.MainView.World);
        await Frames();
        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var mapSize = new Vector2(game.Session.State.Map.Width, game.Session.State.Map.Height) * WorldMapView.TileSize;
        Expect(mapSize.X * world.Zoom >= world.Size.X - 0.5f && mapSize.Y * world.Zoom >= world.Size.Y - 0.5f, "The map opens filling the view");
        var capital = game.Session.State.Villages[game.Session.State.Players[game.Session.LocalPlayerId].CapitalVillageId!.Value];
        var opening = world.WorldToView(WorldMapView.VillageWorldPosition(capital));
        Expect(new Rect2(Vector2.Zero, world.Size).HasPoint(opening), "The map opens with the capital in view");
        await Shot("map-opening");
        var openingZoom = world.Zoom;
        var openingCamera = world.CameraPosition;
        PressKey(game, Key.Space);
        await Frames();
        var fit = world.Zoom;
        Expect(fit < openingZoom && mapSize.Y * fit <= world.Size.Y + 0.5f, "Space fits the whole map");
        await Shot("map-whole");
        PressKey(game, Key.Space);
        await Frames();
        Expect(Mathf.IsEqualApprox(world.Zoom, openingZoom) && world.CameraPosition.IsEqualApprox(openingCamera), "Space again goes back");
        world.FitToView();
        await Frames();

        Expect(mapSize.X * fit <= world.Size.X + 0.5f && mapSize.Y * fit <= world.Size.Y + 0.5f, "The map opens fitted to the view");

        void Wheel(MouseButton button, Vector2 at) =>
            world._GuiInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at });

        // Once the map is larger than the view, zooming keeps the point under the cursor fixed.
        var cursor = world.Size / 2 + new Vector2(30, -20);
        while (mapSize.X * world.Zoom <= world.Size.X || mapSize.Y * world.Zoom <= world.Size.Y)
            Wheel(MouseButton.WheelUp, cursor);
        Expect(world.Zoom > fit, "The wheel zooms in");
        var under = world.ViewToWorld(cursor);
        Wheel(MouseButton.WheelUp, cursor);
        Expect(world.WorldToView(under).DistanceTo(cursor) < 1, "Zoom keeps the point under the cursor");

        for (var i = 0; i < 40; i++)
            Wheel(MouseButton.WheelUp, cursor);
        Expect(world.Zoom <= world.FitZoom * 2 + 0.001f, "Zoom in stops at twice the fit");

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
        Expect(popup.GetNode<Control>("%Missions").Visible && !popup.GetNode<Button>("%Enter").Visible, "An enemy village offers Send");
        Expect(new Rect2(Vector2.Zero, world.Size).Encloses(popup.GetRect()), "The popup stays inside the view");
        await Shot("map-popup");

        // No garrison numbers anywhere in the popup.
        var texts = new[] { "%Name", "%Owner", "%Distance" }.Select(n => popup.GetNode<Label>(n).Text);
        Expect(!texts.Any(t => t.Contains(UiContent.Units[UnitType.Spearman].Name) || t.Contains("×")), "The popup doesn't reveal the garrison");

        await Click(At(own));
        Expect(world.SelectedVillageId == own.Id, "Clicking another marker moves the selection");
        Expect(popup.GetNode<Button>("%Enter").Visible && !popup.GetNode<Control>("%Missions").Visible, "Your own village offers Enter");
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
        game.ShowPanel(GameScreen.GamePanel.Send);
        await Frames();
        send.GetNode<Button>("%PickTarget").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Expect(game.CurrentView == GameScreen.MainView.World, "The form's button opens the map");
        Expect(!send.GetNode<Button>("%PickTarget").Visible, "The button hides once the map is open");

        var at = world.WorldToView(WorldMapView.VillageWorldPosition(camp));
        world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        world._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
        await Frames();
        game.ShowPanel(GameScreen.GamePanel.Building);
        await Frames();
        world.GetNode<VillagePopup>("Popup").GetNode<Button>("%Attack").EmitSignal(BaseButton.SignalName.Pressed);
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
            DisplayServer.WindowSetSize(_windowSize);
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
        session.Tick(BuildingRules.BuildTimeSeconds(session.Data, BuildingType.Barracks, 1, NationRules.Of(session.Data, session.State, 1)) + 1);
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

        game.ShowPanel(GameScreen.GamePanel.Send);
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
        game.ShowPanel(GameScreen.GamePanel.Overview);
        await Frames();
        Expect(game.GetNode<OverviewPanel>("%Overview").GetNode<Label>("%None").Visible, "Nothing on the move yet");

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
        game.ShowPanel(GameScreen.GamePanel.Overview);
        await Frames();
        var row = game.GetNode<OverviewPanel>("%Overview").GetNode<VBoxContainer>("%Rows").GetNode("Moving0");
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
        game.ShowPanel(GameScreen.GamePanel.Overview);
        await Frames();
        Expect(game.GetNode<OverviewPanel>("%Overview").GetNode<Label>("%None").Visible, "The list empties again");
    }

    private async Task TestReports()
    {
        var game = Find<GameScreen>();
        var session = game.Session;
        var drawer = game.GetNode<Drawer>("%Drawer");
        var reports = game.GetNode<ReportsPanel>("%Reports");
        var dialog = game.GetNode<ReportDialog>("%ReportDialog");

        await Frames();
        Expect(drawer.ReportsTabText == L.T("tab.reports_unread", ("count", "1")), $"An unread report is counted, got '{drawer.ReportsTabText}'");

        game.ShowPanel(GameScreen.GamePanel.Reports);
        await Frames();
        Expect(drawer.ReportsTabText == L.T("tab.reports_unread", ("count", "1")), "Opening the tab doesn't mark anything read");
        Expect(reports.IsPinned(1), "An unread battle of mine is pinned");
        var row = reports.GetNode<VBoxContainer>("%Rows").GetNode<Button>("Report1");
        Expect(row.Icon is not null && row.GetNode<Label>("Time").Text == Format.Time(game.Session.State.Reports[0].Time), "A pinned row is marked, and its time has its own column");
        reports.GetNode<Button>("%News").ButtonPressed = true;
        await Frames();
        Expect(row.Visible, "A pinned report stays on top in every filter");
        reports.GetNode<Button>("%Scouting").ButtonPressed = true;
        await Frames();
        Expect(row.Visible, "It stays on the scouting filter too");
        reports.GetNode<Button>("%Mine").ButtonPressed = true;
        await Frames();
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
        game.ShowPanel(GameScreen.GamePanel.Send);
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
        game.ShowPanel(GameScreen.GamePanel.Reports);
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

        game.ShowPanel(GameScreen.GamePanel.Send);
        await Frames();
        game.ShowPanel(GameScreen.GamePanel.Overview);
        await Frames();
        var row = game.GetNode<OverviewPanel>("%Overview").GetNode<VBoxContainer>("%Rows").GetNode("Moving0");
        Expect(row.GetNode<Label>("Title").Text == L.T("movement.from", ("mission", UiContent.MissionName(MovementType.Attack)), ("owner", "Rival")), "The hostile movement is listed");
        await Shot("incoming");

        await TestMovementsOnMap(game, incoming);

        await Frames();
        Expect(!incoming.Visible, "The warning clears after the battle");

        game.ShowPanel(GameScreen.GamePanel.Reports);
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
        game.ShowPanel(GameScreen.GamePanel.Send);
        await Frames();
        game.ShowPanel(GameScreen.GamePanel.Reports);
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
        var amount = Find<ResourceBar>().GetNode<Label>("%Chips/Stone/Amount");
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
        var slots = new[] { new PlayerSlot("You", Nation.Karuk, false), new PlayerSlot("Rival", Nation.Karuk, true) };
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

        // Level 8 costs more stone than a level 1 Warehouse holds, however long the player saves. Karuk has no larger Warehouse.
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
            DisplayServer.WindowSetSize(_windowSize);
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
        await TestRealInput(data);
        await TestSounds(data);
        await TestSettings(data);
        await TestCursors(data);
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
        game.ShowPanel(GameScreen.GamePanel.Send);
        Press(chips[1]);
        await Frames();
        Expect(send.GetNode<OptionButton>("%From").Text == own[1].Name, "The Send form names the village");
        var amount = send.GetNode<SpinBox>("%Units/Swordsman/Amount");
        amount.Value = 50;
        await Frames();
        Press(chips[0]);
        await Frames();
        Expect(amount.Value == 0, "Switching clears the amounts");
        Expect(game.IsShowing(GameScreen.GamePanel.Send), "Switching keeps the tab");

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
        foreach (var tab in Enum.GetValues<GameScreen.GamePanel>())
        {
            game.ShowPanel(tab);
            await Frames();
            Expect(game.GetNode<Control>("Layout").Position.Y >= 0 && game.GetNode<Control>("Layout").Size.Y <= GetViewportRect().Size.Y + 0.5f, $"The {tab} tab fits the window");
        }

        game.ShowPanel(GameScreen.GamePanel.Send);
        game.ShowView(GameScreen.MainView.World);
        await Frames();
        game.ShowPanel(GameScreen.GamePanel.Overview);
        await Frames();
        Expect(game.GetNode<OverviewPanel>("%Overview").GetNode<Label>("%None").Size.Y > 0, "The empty list of movements is readable");
        game.ShowPanel(GameScreen.GamePanel.Building);
        await Frames();
        Expect(game.GetNode<BuildingPanel>("%BuildingPanel").Position.Y >= game.GetNode<Label>("%VillageInfo").GetRect().End.Y, "The Village tab stacks its panels");
        game.ShowPanel(GameScreen.GamePanel.Send);
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
                game.ShowPanel(GameScreen.GamePanel.Building);
                game.ShowView(GameScreen.MainView.Village);
                await Frames();
                Expect(game.GetNode<Control>("Layout").Size.Y <= GetViewportRect().Size.Y + 0.5f, $"The Village tab fits at {name}");
                await Shot($"{name}-village");
                game.ShowPanel(GameScreen.GamePanel.Send);
                game.ShowView(GameScreen.MainView.World);
                await Frames();
            }
            DisplayServer.WindowSetSize(_windowSize);
            await Frames(10);
        }
    }

    /// <summary>The Villages scenario's other two villages. Their ids depend on the map, so this goes by what they hold.</summary>
    private static (Village Army, Village Reserve) ScenarioVillages(IGameSession session)
    {
        var own = UiContent.OwnVillages(session);
        var army = own.First(v => v.TroopCount(UnitType.Swordsman) > 0);
        return (army, own.First(v => v != army && v.TroopCount(UnitType.Spearman) == 30));
    }

    private async Task TestReinforce(GameData data)
    {
        var (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.Villages);
        var (army, reserve) = ScenarioVillages(session);
        game.SwitchVillage(army.Id);
        game.ShowView(GameScreen.MainView.World);
        await Frames();

        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var popup = world.GetNode<VillagePopup>("Popup");
        Click(world, world.WorldToView(WorldMapView.VillageWorldPosition(reserve)));
        await Frames();
        Expect(popup.GetNode<Button>("%Reinforce").Visible, "Your other village offers Reinforce");
        Expect(popup.GetNode<Button>("%Enter").Visible, "Your other village offers Enter");
        Expect(!popup.GetNode<Control>("%Missions").Visible, "Your own village offers no attack");
        await Shot("reinforce-popup");

        Press(popup.GetNode<Button>("%Reinforce"));
        await Frames();
        var send = game.GetNode<SendPanel>("%Send");
        Expect(game.IsShowing(GameScreen.GamePanel.Send), "Reinforce opens the Send tab");
        Expect(send.GetNode<OptionButton>("%Mission").GetItemText(0) == UiContent.MissionName(MovementType.Reinforce), "The mission is Reinforce");

        send.GetNode<SpinBox>("%Units/Swordsman/Amount").Value = 40;
        await Frames();
        var before = reserve.TroopCount(UnitType.Swordsman);
        Press(send.GetNode<Button>("%Send"));
        await Frames();
        Expect(session.State.Movements.Any(m => m.Type == MovementType.Reinforce), "Reinforcements leave");
        await RunUntilArrived(session);
        Expect(reserve.TroopCount(UnitType.Swordsman) == before + 40, "Reinforcements join the garrison");

        Click(world, world.WorldToView(WorldMapView.VillageWorldPosition(army)));
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
        var (from, to) = ScenarioVillages(session);
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
        Expect(game.IsShowing(GameScreen.GamePanel.Send), "The Marketplace opens the Send tab");
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
        game.ShowPanel(GameScreen.GamePanel.Overview);
        await Frames();
        Expect(game.GetNode<OverviewPanel>("%Overview").GetNode<Label>("%Rows/Moving0/Title").Text == L.T("movement.carrying", ("amount", Format.Whole(capacity + 1)), ("target", to.Name)), "The transfer is in the list");
        await Shot("transfer-moving");

        // The overflow warning.
        var room = BuildingRules.VillageCapacity(data, session.State, to) - to.Resources.Stone;
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
        game.ShowPanel(GameScreen.GamePanel.Overview);
        await Frames();
        Expect(game.GetNode<OverviewPanel>("%Overview").GetNode<Label>("%Rows/Moving0/Title").Text == L.T("movement.carriers_returning"), "The list shows them returning");
        await RunUntilArrived(session);
        Expect(CarrierRules.Free(session.State, data, from) == free, "The carriers are free again");

        // Sending to itself, and to a rival.
        send.SetTarget(from.Id, MovementType.Transfer);
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

        game.ShowPanel(GameScreen.GamePanel.Send);
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
        game.ShowPanel(GameScreen.GamePanel.Send);
        send.SetTarget(2);
        await Frames();
        Expect(intel.Visible && intel.Text.Contains(L.N("intel.troops", 5)) && intel.Text.Contains(L.T("intel.wall", ("level", "2"))), $"The form shows the latest intel, got '{intel.Text}'");
        await Shot("scout-intel");

        game.ShowPanel(GameScreen.GamePanel.Reports);
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
        game.ShowPanel(GameScreen.GamePanel.Send);
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
        game.GetNode<Drawer>("%Drawer").Close();
        await RunUntilArrived(session);
        Expect(state.ScoutReports[^1].Succeeded, "The Watchtower doesn't stop anyone by itself");
        Expect(reports.UnreadCount(session) == known + 1, "The Watchtower tells the defender of a success");
        game.ShowPanel(GameScreen.GamePanel.Reports);
        await Frames();
        var seen = reports.GetNode<VBoxContainer>("%Rows").GetNode<Button>($"Report{state.ScoutNotices[^1].Id}");
        Expect(seen.Text.StartsWith(L.T("notice.seen", ("sender", "Rival"), ("target", "")).TrimEnd()), $"The notice names the sender, got '{seen.Text}'");
        Press(seen);
        await Frames();
        Expect(dialog.GetNode<Label>("%Result").Text == L.T("notice.result_seen"), "The Watchtower's notice opens");
        Press(dialog.GetNode<Button>("%Close"));

        game.ShowPanel(GameScreen.GamePanel.Send);
        await Frames();
        known = reports.UnreadCount(session);
        await TrainAndWait(session, 1, 40, UnitType.Scout);
        Expect(session.SendAs(2, new SendTroopsCommand(2, 1, MovementType.Scout, new Dictionary<UnitType, int> { [UnitType.Scout] = 1 })).Success, "The rival tries once more");
        game.GetNode<Drawer>("%Drawer").Close();
        await RunUntilArrived(session);
        Expect(!state.ScoutReports[^1].Succeeded, "The player's Scouts catch the rival's");
        Expect(reports.UnreadCount(session) == known + 1, "The defender is told about caught scouts");
        game.ShowPanel(GameScreen.GamePanel.Reports);
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
        game.ShowPanel(GameScreen.GamePanel.Send);
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
        var badge = game.GetNode<ResourceBar>("%ResourceBar").GetNode<Label>("%Box/GodBadge");
        Expect(!badge.Visible, "No badge while god mode is off");

        void PressF9() => control._Input(new InputEventKey { Keycode = Key.F9, Pressed = true });

        PressF9();
        await Frames();
        Expect(session.IsGodMode && badge.Visible && badge.Text == "GOD MODE", "F9 turns god mode on, with a badge");
        await Shot("god-mode");

        var state = session.State;
        var home = state.Villages[1];
        var capacity = BuildingRules.VillageCapacity(data, session.State, home);
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

        game.ShowPanel(GameScreen.GamePanel.Training);
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
        var (army, _) = ScenarioVillages(session);
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

        endGame.ShowPanel(GameScreen.GamePanel.Building);
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
        Expect(game.IsShowing(GameScreen.GamePanel.Reports), "Watching shows the reports");
        Expect(!game.IsShowing(GameScreen.GamePanel.Send), "Watching closes the Send popup");

        PressKey(game, Key.M);
        Expect(game.CurrentView == GameScreen.MainView.World, "M can't leave the map while watching");

        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var popup = world.GetNode<VillagePopup>("Popup");
        Click(world, world.WorldToView(WorldMapView.VillageWorldPosition(session.State.Villages[2])));
        await Frames();
        Expect(popup.Visible && !popup.GetNode<Control>("%Missions").Visible, "A watcher can look but not send");
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
        Expect(cells.Count == 21, $"A header and one row per player, got {cells.Count} cells");
        Expect(cells[8] == L.T("player.you") && cells[9] == session.State.Players[1].Nation.ToString() && cells[10] == $"{top.Villages}" && cells[11] == $"{top.Population}" && cells[12] == $"{top.Captures}" && cells[13] == $"{top.Score:0}", "The table matches the score");
        Expect(cells[15].EndsWith(L.T("end.eliminated", ("name", ""))), "Eliminated players are marked");
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

    /// <summary>The right effect at the right moment. How they sound is for a person to judge.</summary>
    private async Task TestSounds(GameData data)
    {
        // Earlier steps may have freed the Main that owns it.
        if (!FindAll<Sound>(this).Any())
            AddChild(new Sound { Name = "Sound" });
        var heard = new List<SoundEffect>();
        Sound.Recorded = heard;
        async Task Expects(SoundEffect effect, string when)
        {
            await Frames();
            Expect(heard.Contains(effect), $"{effect} plays when {when}, heard {string.Join(',', heard)}");
            heard.Clear();
            await Settle(0.1);
        }

        var (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.Villages);
        var tab = game.GetNode<Button>("%Drawer/Tabs/OverviewTab");
        await MoveMouse(tab.GetGlobalRect().GetCenter());
        await Expects(SoundEffect.Hover, "the mouse enters a button");
        await ClickAndSettle(tab);
        await Expects(SoundEffect.Click, "a button is clicked");

        tab.EmitSignal(BaseButton.SignalName.Pressed);
        tab.EmitSignal(BaseButton.SignalName.Pressed);
        Expect(heard.Count(e => e == SoundEffect.Click) == 1, "Rapid repeats play once");
        await Settle(0.1);
        heard.Clear();

        var village = session.State.Villages[1];
        session.Send(new BuildCommand(1, BuildingType.Farm));
        session.Tick(village.BuildQueue[0].FinishTime - session.State.ElapsedTime);
        await Expects(SoundEffect.BuildingComplete, "a building finishes");

        await TrainAndWait(session, 1, 1);
        await Expects(SoundEffect.TrainingComplete, "training finishes");

        RivalAttacks(session);
        await Expects(SoundEffect.AttackWarning, "an attack starts on my village");
        await Frames();
        Expect(!heard.Contains(SoundEffect.AttackWarning), "The warning plays once per attack");

        await RunUntilCapitalLost(session);
        await Frames();
        Expect(heard.Contains(SoundEffect.BattleClash), $"The clash plays at my battle, heard {string.Join(',', heard)}");
        Expect(heard.Contains(SoundEffect.VillageCaptured), "The capture plays when I lose a village");
        heard.Clear();

        (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.Endgame);
        session.Send(new SendTroopsCommand(1, 2, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 100, [UnitType.Lord] = 1 }));
        session.Tick(session.State.Movements[0].ArrivalTime);
        await Expects(SoundEffect.Victory, "I win");

        (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.LastStand, rivalAttacks: true);
        await RunUntilArrived(session);
        await Expects(SoundEffect.Defeat, "I lose");

        Sound.Recorded = null;

        UserSettings.MusicVolume = 0.5f;
        UserSettings.Muted = true;
        Sound.ApplySettings();
        Expect(Mathf.IsEqualApprox(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Music")), Mathf.LinearToDb(0.5f)), "The music volume reaches its bus");
        Expect(AudioServer.IsBusMute(AudioServer.GetBusIndex("Master")), "Mute reaches the master bus");
        UserSettings.Muted = false;
        Sound.ApplySettings();
    }

    private async Task TestIntro()
    {
        Expect(!ProjectSettings.GetSetting("application/boot_splash/show_image").AsBool(), "Godot's own image is off");
        Main.SkipIntro = false;
        var skips = new (string Name, Func<Task> Act)[]
        {
            ("a key", () => PressKeyEvent(Key.Space)),
            ("a click", () => ClickAndSettle(new Vector2(200, 200))),
            ("waiting", () => Settle(IntroScreen.Seconds + 0.3)),
        };
        foreach (var (name, act) in skips)
        {
            _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
            AddChild(_main);
            await Frames();
            Expect(FindAll<IntroScreen>(_main).Any() && !FindAll<StartScreen>(_main).Any(), "A launch opens on the intro");
            await Shot("intro");
            await act();
            await Frames();
            Expect(FindAll<StartScreen>(_main).Any() && !FindAll<IntroScreen>(_main).Any(), $"The intro ends with {name}");
            _main.QueueFree();
            await Frames();
        }
        Main.SkipIntro = true;
    }

    private async Task TestCursors(GameData data)
    {
        foreach (var name in new[] { "arrow", "hand", "attack", "forbidden" })
            Expect(GD.Load<Texture2D>($"res://assets/cursors/{name}.png")?.GetWidth() <= 64, $"The {name} cursor loads at a cursor's size");

        var (game, session) = await StartMatch(data, null);
        if (!FindAll<Cursors>(this).Any())
            AddChild(new Cursors { Name = "Cursors" });
        var world = game.GetNode<WorldMapView>("%WorldMapView");
        game.ShowView(GameScreen.MainView.World);
        await Frames();

        async Task At(Village village)
        {
            world.CentreOn(village);
            await Frames();
            await MoveMouse(world.GetGlobalTransform() * world.WorldToView(WorldMapView.VillageWorldPosition(village)));
        }

        await At(session.State.Villages[2]);
        Expect(world.MouseDefaultCursorShape == Control.CursorShape.Cross, "A rival's village shows the attack cursor");
        await At(session.State.Villages[1]);
        Expect(world.MouseDefaultCursorShape == Control.CursorShape.PointingHand, "My own village shows the hand");
        await MoveMouse(EmptySpot(game));
        Expect(world.MouseDefaultCursorShape == Control.CursorShape.Arrow, "Empty land shows the arrow");

        var (other, _) = await StartMatch(data, null);
        var button = other.GetNode<Button>("%Drawer/Tabs/OverviewTab");
        await MoveMouse(button.GetGlobalRect().GetCenter());
        Expect(button.MouseDefaultCursorShape == Control.CursorShape.PointingHand, "A button shows the hand");
        await MoveMouse(EmptySpot(other));
        button.Disabled = true;
        await MoveMouse(button.GetGlobalRect().GetCenter());
        Expect(button.MouseDefaultCursorShape == Control.CursorShape.Forbidden, "A disabled button shows it can't be pressed");
        button.Disabled = false;
        foreach (var cursors in FindAll<Cursors>(this).ToList())
            if (cursors.GetParent() == this)
                cursors.QueueFree();
    }

    /// <summary>The picker through real clicks' worth of signals, then a match that uses the choice and shows it.</summary>
    private async Task TestNationPicker()
    {
        var start = Find<StartScreen>();
        var picker = start.GetNode<OptionButton>("%Nation");
        var opponents = start.GetNode<OptionButton>("%OpponentNation");
        var info = start.GetNode<Label>("%NationInfo");
        var names = Enum.GetValues<Nation>();

        Expect(picker.ItemCount == names.Length + 1 && picker.GetItemText(0) == L.T("start.nation_random"), "The picker offers Random and every nation");
        Expect(opponents.GetItemText(1) == names[0].ToString() && opponents.Selected == 0, "Opponents are Random by default");
        Expect(picker.GetItemText(1) == "Valdren", "Nation names aren't translated");

        foreach (var nation in names)
        {
            picker.Select(StartScreen.NationIndex(nation));
            picker.EmitSignal(OptionButton.SignalName.ItemSelected, picker.Selected);
            Expect(info.Text == UiContent.NationDescription(nation), $"{nation} shows its description");
            Expect(UserSettings.Nation == nation, $"{nation} is remembered");
            CheckLayout($"nation picker {nation}");
            await Shot($"picker-{nation}");
        }

        picker.Select(StartScreen.NationIndex(Nation.Karuk));
        picker.EmitSignal(OptionButton.SignalName.ItemSelected, picker.Selected);
        opponents.Select(StartScreen.NationIndex(Nation.Solari));
        Press(start.GetNode<Button>("%Start"));
        await Frames();

        var game = Find<GameScreen>();
        var session = game.Session;
        Expect(session.State.Players[1].Nation == Nation.Karuk, "The match uses my nation");
        Expect(session.State.Players[2].Nation == Nation.Solari, "The match uses the opponents' nation");

        var world = game.GetNode<WorldMapView>("%WorldMapView");
        game.ShowView(GameScreen.MainView.World);
        world.CentreOn(session.State.Villages[2]);
        await Frames();
        world._GuiInput(new InputEventMouseMotion { Position = world.WorldToView(WorldMapView.VillageWorldPosition(session.State.Villages[2])) });
        await Settle(0.45);
        var owner = world.GetNode<VillagePopup>("Popup").GetNode<Label>("%Owner").Text;
        Expect(owner.Contains("Solari"), $"The hover card names the nation, got '{owner}'");

        UserSettings.Nation = Nation.Valdren;
        game.ExitToStart();
        await Frames();
    }

    /// <summary>Every way out reaches Main, and a host's guests hear at once.</summary>
    private async Task TestQuit()
    {
        var quits = 0;
        Main.QuitForTest = () => quits++;

        var start = Find<StartScreen>();
        await ClickAndSettle(start.GetNode<Button>("%Quit"));
        Expect(quits == 1, "The start screen's Quit game asks nothing and quits");

        _main.Notification((int)Node.NotificationWMCloseRequest);
        await Frames();
        Expect(quits == 2, "Closing the window quits, as Cmd+Q and Alt+F4 do");

        var data = GameData.Load(new GodotDataSource());
        var network = new MemoryNetwork();
        _main.OpenHostLobby(network.Host, "Oğuz", null, Array.Empty<string>(), Handshake.DefaultPort);
        await Frames();
        var guest = new LobbyClient(network.Connect(), data, Handshake.DataHash(new GodotDataSource()), "Ayla", null);
        ClientGameSession? session = null;
        guest.MatchStarted += started => session = started;
        guest.Poll();
        await Frames();
        Press(Find<LobbyScreen>().GetNode<Button>("%Start"));
        await Frames();
        guest.Poll();

        var game = Find<GameScreen>();
        var heardLoss = false;
        session!.ConnectionLost += () => heardLoss = true;
        await ClickAndSettle(Find<ResourceBar>().GetNode<Button>("%Pause"));
        var menuChoices = new[] { "%Action", "%Settings", "%Secondary", "%Tertiary", "%Quit" }.Select(name => game.GetNode<Overlay>("%PauseOverlay").GetNode<Button>(name).Text).ToList();
        Expect(menuChoices.Distinct().Count() == menuChoices.Count, $"Every menu choice reads differently: {string.Join(", ", menuChoices)}");
        var confirm = game.GetNode<Overlay>("%QuitOverlay");
        await ClickAndSettle(game.GetNode<Overlay>("%PauseOverlay").GetNode<Button>("%Quit"));
        Expect(confirm.Visible && confirm.GetNode<Label>("%Message").Text == L.T("overlay.leave.message_host"), "A host is told that quitting ends the match");
        CheckLayout("quit confirmation");
        await Shot("quit-confirm");
        await ClickAndSettle(confirm.GetNode<Button>("%Action"));
        Expect(quits == 3, "Confirming quits");
        session.Tick(0);
        Expect(heardLoss, "A host quitting tells the guests at once");

        Main.QuitForTest = null;
        game.ExitToStart();
        await Frames();
    }

    private async Task TestSettingsFromStart()
    {
        var start = Find<StartScreen>();
        var screen = start.GetNode<SettingsScreen>("%SettingsScreen");
        await ClickAndSettle(start.GetNode<Button>("%Settings"));
        Expect(screen.Visible, "The start screen opens the settings");
        Expect(screen.GetGlobalRect().Encloses(GetViewportRect()), $"They cover the whole window, got {screen.GetGlobalRect()}");
        Expect(!screen.GetNode<OptionButton>("%Language").Disabled && !screen.GetNode<Control>("%LanguageNote").Visible, "Language can change from the start screen");
        CheckLayout("settings from start");
        await Shot("settings-start");

        screen.GetNode<CheckBox>("%Fullscreen").ButtonPressed = false;
        await ClickAndSettle(screen.GetNode<Button>("%Close"));
        Expect(!screen.Visible, "Close hides them");
        await ClickAndSettle(start.GetNode<Button>("%Settings"));

        var chosen = Language.Current;
        screen.GetNode<OptionButton>("%Language").EmitSignal(OptionButton.SignalName.ItemSelected, Array.IndexOf(Language.Codes, chosen));
        await Frames();
        Expect(Find<StartScreen>() != start && Language.Current == chosen, "Choosing a language rebuilds the start screen");
    }

    /// <summary>Reachable from the in-game menu, and remembered.</summary>
    private async Task TestSettings(GameData data)
    {
        var (game, _) = await StartMatch(data, null);
        var pause = game.GetNode<Overlay>("%PauseOverlay");
        var screen = game.GetNode<SettingsScreen>("%SettingsScreen");
        var bar = game.GetNode<ResourceBar>("%ResourceBar");
        var mute = bar.GetNode<Button>("%Mute");

        await ClickAndSettle(mute);
        Expect(Sound.IsMuted && UserSettings.Muted, "The top bar's button mutes, and it's remembered");
        await ClickAndSettle(mute);
        Expect(!Sound.IsMuted && !UserSettings.Muted, "It turns sound back on");

        for (var i = 0; i < 2; i++)
        {
            await ClickAndSettle(bar.GetNode<Button>("%Pause"));
            await ClickAndSettle(pause.GetNode<Button>("%Settings"));
            Expect(screen.Visible && !pause.Visible, "The menu opens the settings");
            Expect(screen.GetNode<OptionButton>("%Language").Disabled && screen.GetNode<Control>("%LanguageNote").Visible, "Language is locked during a match, with a note");
            CheckLayout("settings");
            await Shot("settings");

            screen.GetNode<HSlider>("%Effects").Value = 0.4;
            Expect(Mathf.IsEqualApprox(UserSettings.EffectsVolume, 0.4f), "The effects volume is remembered");
            Expect(Mathf.IsEqualApprox(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Effects")), Mathf.LinearToDb(0.4f), 0.01f), "It reaches its bus");

            await ClickAndSettle(screen.GetNode<CheckBox>("%Mute"));
            Expect(Sound.IsMuted, "The settings mute too");
            await Frames();
            await ClickAndSettle(screen.GetNode<CheckBox>("%Mute"));

            if (i == 0)
                await ClickAndSettle(screen.GetNode<Button>("%Close"));
            else
                await EscapeAndSettle();
            Expect(!screen.Visible && pause.Visible, "Closing returns to the menu");
            await ClickAndSettle(pause.GetNode<Button>("%Action"));
            await CheckClicksWork(game, "closing the settings");
        }

        UserSettings.EffectsVolume = 0.8f;
        Sound.ApplySettings();
    }

    /// <summary>Every screen at the window size and language given: nothing leaves the window, and nothing important is covered.</summary>
    private async Task LayoutSweep()
    {
        UseSettings("user://uitest_settings.cfg");
        var data = GameData.Load(new GodotDataSource());
        _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        _main.IdleOpponents = true;
        AddChild(_main);
        await Frames();

        var start = Find<StartScreen>();
        await Shot("start");
        Press(start.GetNode<Button>("%Settings"));
        await Frames();
        await Shot("start-settings");
        Press(start.GetNode<Button>("%Host"));
        await Frames();
        await Shot("connect");
        _main.OpenHostLobby(new MemoryNetwork().Host, "Oğuz", null, Array.Empty<string>(), Handshake.DefaultPort);
        await Frames();
        await Shot("lobby");
        _main.QueueFree();
        await Frames();

        var (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.Villages);
        session.Send(new SendTroopsCommand(1, 2, MovementType.Raid, new Dictionary<UnitType, int> { [UnitType.Spearman] = 2 }));
        await RunUntilArrived(session);
        await SweepGame(game, session);

        (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.Villages, rivalAttacks: true);
        await RunUntilCapitalLost(session);
        await Frames();
        await Shot("capital-dialog");

        (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.LastStand, players: 3, rivalAttacks: true);
        await RunUntilArrived(session);
        await Frames();
        await Shot("eliminated");
        Press(game.GetNode<Overlay>("%EliminatedOverlay").GetNode<Button>("%Action"));
        await Settle(0.3);
        await Shot("watching");
        session.Tick(TestDuration);
        await Frames();
        await Shot("match-end");
    }

    private async Task SweepGame(GameScreen game, LocalGameSession session)
    {
        var bar = game.GetNode<ResourceBar>("%ResourceBar");
        var drawer = game.GetNode<Drawer>("%Drawer");
        var view = game.GetNode<VillageView>("%VillageView");
        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var window = GetViewportRect();
        var popups = new[] { "%BuildingPopup", "%TroopsPopup", "%SendPopup" }.Select(path => game.GetNode<PopupFrame>(path)).ToList();

        void CheckCovered(string name)
        {
            var barBottom = bar.GetGlobalRect().End.Y;
            Expect(bar.ContentWidth <= bar.GetNode<Control>("%Clip").Size.X + 1, $"{name}: the top bar fits");
            foreach (var popup in popups.Where(p => p.Visible))
            {
                var rect = popup.GetGlobalRect();
                Expect(window.Encloses(rect), $"{name}: {popup.Name} stays in the window, {rect}");
                Expect(rect.Position.Y >= barBottom, $"{name}: {popup.Name} leaves the top bar clear");
            }
            if (drawer.IsOpen)
            {
                Expect(drawer.GetNode<Control>("Tabs").GetGlobalRect().Position.Y >= barBottom, $"{name}: the drawer leaves the top bar clear");
                Expect(window.Encloses(drawer.SheetRect), $"{name}: the drawer stays in the window");
            }
        }

        async Task Step(string name)
        {
            await Frames();
            await Frames();
            CheckCovered(name);
            await Shot(name);
        }

        // Every item in the top bar at once, on the fullest screen.
        Valkaris.Core.Dev.DevScenario.AddThirdPartyActivity(session.State, 1, 2);
        Valkaris.Core.Dev.DevScenario.SetCapitalDeadline(session.State, 1, session.State.ElapsedTime + 45);
        await Frames();
        Press(game.GetNode<CapitalDialog>("%CapitalDialog").GetNode<Button>("%Later"));
        await Step("bar-full");

        foreach (var mainView in new[] { GameScreen.MainView.Village, GameScreen.MainView.World })
        {
            game.ShowView(mainView);
            await Step($"view-{mainView}");
            foreach (var fraction in new[] { Drawer.MinFraction, 0.2f, Drawer.MaxFraction })
            {
                drawer.SetHeightFraction(fraction);
                drawer.Open(Drawer.Tab.Overview);
                await Step($"overview-{mainView}-{fraction:0.00}");
                drawer.Open(Drawer.Tab.Reports);
                await Step($"reports-{mainView}-{fraction:0.00}");
                foreach (var panel in new[] { GameScreen.GamePanel.Building, GameScreen.GamePanel.Training, GameScreen.GamePanel.Send })
                {
                    game.ShowPanel(panel);
                    await Step($"{panel}-over-drawer-{mainView}-{fraction:0.00}");
                }
                popups[2].Close();
            }
            drawer.SetHeightFraction(0.2f);
            drawer.Close();
            await Settle(0.1);
        }

        game.ShowView(GameScreen.MainView.Village);
        foreach (var building in new[] { BuildingType.Barracks, BuildingType.Marketplace, BuildingType.Farm })
        {
            view.TestHoveredBuilding = building;
            await Settle(0.45);
            await Step($"card-{building}");
            Expect(new Rect2(Vector2.Zero, view.Size).Encloses(view.Card.GetRect()), $"The {building} card stays in the view");
            view.TestHoveredBuilding = null;
            await Settle(0.45);
        }

        // A popup beside a village in each corner of the view still fits.
        game.ShowView(GameScreen.MainView.World);
        var target = session.State.Villages[2];
        foreach (var corner in new[] { Vector2.Zero, new Vector2(1, 0), new Vector2(0, 1), Vector2.One })
        {
            var rect = new Rect2(window.Size * corner * 0.9f + window.Size * 0.02f, new Vector2(48, 48));
            popups[2].Open(rect);
            await Step($"send-corner-{corner.X}-{corner.Y}");
            Expect(!popups[2].GetGlobalRect().Intersects(rect) || popups[2].GetGlobalRect().GetCenter().DistanceTo(window.GetCenter()) < 2, $"The popup avoids its target at corner {corner}");
        }
        popups[2].Close();

        world.CentreOn(target);
        await Frames();
        world._GuiInput(new InputEventMouseMotion { Position = world.WorldToView(WorldMapView.VillageWorldPosition(target)) });
        await Settle(0.45);
        await Step("hover-card");
        Expect(new Rect2(Vector2.Zero, world.Size).Encloses(world.GetNode<VillagePopup>("Popup").GetRect()), "The hover card stays in the view");
        world._GuiInput(new InputEventMouseMotion { Position = new Vector2(world.Size.X - 2, world.Size.Y - 2) });
        await Settle(0.45);

        var dialog = game.GetNode<ReportDialog>("%ReportDialog");
        dialog.ShowReport(session, session.State.Reports[0]);
        await Step("report");
        dialog.Visible = false;

        var pause = game.GetNode<Overlay>("%PauseOverlay");
        Press(bar.GetNode<Button>("%Pause"));
        await Step("menu");
        Press(pause.GetNode<Button>("%Settings"));
        await Step("settings");
        game.GetNode<SettingsScreen>("%SettingsScreen").Close();
        await Frames();
        Press(pause.GetNode<Button>("%Secondary"));
        await Step("leave");
        Press(game.GetNode<Overlay>("%LeaveOverlay").GetNode<Button>("%Secondary"));
        await Frames();
        Press(pause.GetNode<Button>("%Tertiary"));
        await Step("surrender");
    }

    private async Task RealInputOnly()
    {
        UseSettings("user://uitest_settings.cfg");
        _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        _main.IdleOpponents = true;
        AddChild(_main);
        await Frames();
        await TestSettingsFromStart();
        await TestQuit();
        await TestRealInput(GameData.Load(new GodotDataSource()));
        await TestSounds(GameData.Load(new GodotDataSource()));
        await TestSettings(GameData.Load(new GodotDataSource()));
        await TestCursors(GameData.Load(new GodotDataSource()));
    }

    /// <summary>
    /// Real mouse and key events through the viewport, with the real slide: a control hidden or stacked wrongly only shows up this way.
    /// In a window, leave the mouse alone during the run. Moving the real cursor cancels the click being tested.
    /// </summary>
    private async Task TestRealInput(GameData data)
    {
        Drawer.Instant = false;
        try
        {
            var (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.Villages);
            session.Send(new SendTroopsCommand(1, 2, MovementType.Raid, new Dictionary<UnitType, int> { [UnitType.Spearman] = 2 }));
            await RunUntilArrived(session);
            Expect(session.State.Reports.Count > 0, "The raid gives a report to open");
            var (rounds, views) = (3, new[] { GameScreen.MainView.Village, GameScreen.MainView.World });

            // First with no view change at all, the way a player would meet it right after the match starts.
            await DrawerByMouse(game, 1, false);
            await CheckClicksWork(game, "the drawer, before any view change");

            foreach (var view in views)
            {
                game.ShowView(view);
                await Frames();
                await CheckClicksWork(game, $"switching to {view}");
                await DrawerByMouse(game, rounds, true);
                await ReportDialogByMouse(game, rounds);
                await OverlaysByMouse(game, rounds);
            }

            await ZoomByInput(game);
            await PopupsByMouse(game, rounds);
            await TestCapitalDialogByMouse(data);
            await TestEliminatedByMouse(data);
        }
        finally
        {
            Drawer.Instant = true;
        }
    }

    /// <summary>Wheel, the trackpad's pinch event, keys and buttons, all held between the whole map and twice as close.</summary>
    private async Task ZoomByInput(GameScreen game)
    {
        var world = game.GetNode<WorldMapView>("%WorldMapView");
        game.ShowView(GameScreen.MainView.World);
        await Settle(0.1);
        var centre = world.GetGlobalRect().GetCenter();

        async Task Wheel(MouseButton button)
        {
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = centre, GlobalPosition = centre, Factor = 1 }, true);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = centre, GlobalPosition = centre }, true);
            await Frames(1);
        }

        async Task Pinch(float factor)
        {
            GetViewport().PushInput(new InputEventMagnifyGesture { Position = centre, Factor = factor }, true);
            await Frames(1);
        }

        await MoveMouse(centre);
        async Task Check(string name, Func<Task> zoomIn, Func<Task> zoomOut)
        {
            var before = world.Zoom;
            await zoomOut();
            await zoomIn();
            await zoomIn();
            Expect(world.Zoom > Mathf.Min(before, world.FitZoom) && world.Zoom != before, $"{name} zooms in");
            for (var i = 0; i < 20; i++)
                await zoomIn();
            Expect(Mathf.IsEqualApprox(world.Zoom, world.MaxZoom), $"{name} stops at twice the fit, got {world.Zoom} of {world.MaxZoom}");
            await zoomOut();
            Expect(world.Zoom < world.MaxZoom, $"{name} zooms out");
            for (var i = 0; i < 20; i++)
                await zoomOut();
            Expect(Mathf.IsEqualApprox(world.Zoom, world.FitZoom), $"{name} stops at the whole map, got {world.Zoom} of {world.FitZoom}");
        }

        await Check("The wheel", () => Wheel(MouseButton.WheelUp), () => Wheel(MouseButton.WheelDown));
        await Check("A pinch", () => Pinch(1.15f), () => Pinch(1 / 1.15f));
        await Check("The keys", () => PressKeyEvent(Key.Equal), () => PressKeyEvent(Key.Minus));
        var buttons = world.GetNode<Control>("Zoom");
        await Check("The buttons", () => ClickAndSettle(buttons.GetNode<Button>("In")), () => ClickAndSettle(buttons.GetNode<Button>("Out")));
        Expect(GetViewportRect().Encloses(buttons.GetGlobalRect()) && buttons.GetGlobalRect().Position.Y >= game.GetNode<ResourceBar>("%ResourceBar").GetGlobalRect().End.Y, "The zoom buttons are in the window, below the top bar");

        await PressKeyEvent(Key.Equal);
        game.ShowView(GameScreen.MainView.Village);
        var zoom = world.Zoom;
        await PressKeyEvent(Key.Equal);
        Expect(world.Zoom == zoom, "The keys do nothing in the village");
    }

    private async Task DrawerByMouse(GameScreen game, int rounds, bool check)
    {
        var drawer = game.GetNode<Drawer>("%Drawer");
        var overview = drawer.GetNode<Button>("Tabs/OverviewTab");
        var reports = drawer.GetNode<Button>("Tabs/ReportsTab");
        foreach (var (tab, kind) in new[] { (overview, Drawer.Tab.Overview), (reports, Drawer.Tab.Reports) })
        {
            for (var i = 0; i < rounds; i++)
            {
                await ClickAndSettle(tab);
                Expect(drawer.IsOpen && drawer.ActiveTab == kind, $"Round {i}: a click on the tab opens {kind}");
                await ClickAndSettle(tab);
                Expect(!drawer.IsOpen, $"Round {i}: a click on the same tab closes {kind}");
                if (check)
                    await CheckClicksWork(game, $"closing {kind} with its tab");

                await ClickAndSettle(tab);
                await EscapeAndSettle();
                Expect(!drawer.IsOpen, $"Round {i}: Esc closes {kind}");
                if (check)
                    await CheckClicksWork(game, $"closing {kind} with Esc");
            }
        }

        // Moving between tabs leaves it open, and the filters inside respond.
        await ClickAndSettle(overview);
        await ClickAndSettle(reports);
        Expect(drawer.IsOpen && drawer.ActiveTab == Drawer.Tab.Reports, "A click on the other tab swaps the page");
        var panel = game.GetNode<ReportsPanel>("%Reports");
        foreach (var filter in new[] { "%News", "%Scouting", "%Mine" })
        {
            await ClickAndSettle(panel.GetNode<Button>(filter));
            Expect(panel.GetNode<Button>(filter).ButtonPressed, $"A click reaches the {filter} filter");
        }
        await ClickAndSettle(reports);
    }

    private async Task ReportDialogByMouse(GameScreen game, int rounds)
    {
        var drawer = game.GetNode<Drawer>("%Drawer");
        var dialog = game.GetNode<ReportDialog>("%ReportDialog");
        var reportsTab = drawer.GetNode<Button>("Tabs/ReportsTab");
        var rows = game.GetNode<ReportsPanel>("%Reports").GetNode<VBoxContainer>("%Rows");
        var view = game.CurrentView;
        for (var i = 0; i < rounds * 2; i++)
        {
            var byEscape = i % 2 == 1;
            await ClickAndSettle(reportsTab);
            var row = rows.GetChildren().OfType<Button>().First(b => b.Visible);
            await ClickAndSettle(row);
            Expect(dialog.Visible, $"Round {i}: a click on a row opens the report");

            await PressKeyEvent(Key.M);
            Expect(game.CurrentView == view && dialog.Visible, $"Round {i}: keys don't reach the game behind the report");
            await ClickAndSettle(new Vector2(4, 4));
            Expect(dialog.Visible, $"Round {i}: a click beside the report doesn't reach the game behind it");

            if (byEscape)
                await EscapeAndSettle();
            else
                await ClickAndSettle(dialog.GetNode<Button>("%Close"));
            Expect(!dialog.Visible, $"Round {i}: {(byEscape ? "Esc" : "Close")} hides the report");
            Expect(drawer.IsOpen, $"Round {i}: closing the report leaves the drawer open");

            await ClickAndSettle(reportsTab);
            Expect(!drawer.IsOpen, $"Round {i}: the tab still closes the drawer after the report");
            await CheckClicksWork(game, $"closing the report with {(byEscape ? "Esc" : "Close")}");
        }
    }

    private async Task PopupsByMouse(GameScreen game, int rounds)
    {
        var session = game.Session;
        var view = game.GetNode<VillageView>("%VillageView");
        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var building = game.GetNode<PopupFrame>("%BuildingPopup");
        var troops = game.GetNode<PopupFrame>("%TroopsPopup");
        var send = game.GetNode<PopupFrame>("%SendPopup");
        var rival = session.State.Villages[2];

        async Task CloseIt(PopupFrame popup, bool byEscape, string name)
        {
            if (byEscape)
                await EscapeAndSettle();
            else
                await ClickAndSettle(popup.GetNode<Button>("Frame/Header/Close"));
            Expect(!popup.Visible, $"{(byEscape ? "Esc" : "Its close button")} closes {name}");
            await MoveMouse(EmptySpot(game));
            await Settle(0.5);
            await CheckClicksWork(game, $"closing {name} with {(byEscape ? "Esc" : "its button")}");
        }

        async Task OnVillage(BuildingType type)
        {
            game.ShowView(GameScreen.MainView.Village);
            game.SwitchVillage(1);
            await Frames();
            await MoveMouse(view.BuildingScreenRect(type).GetCenter());
            await Settle(0.5);
            Expect(view.Card.Visible, $"Resting the mouse on the {type} opens its card");
        }

        for (var i = 0; i < rounds * 2; i++)
        {
            var byEscape = i % 2 == 1;

            await OnVillage(BuildingType.Farm);
            await ClickAndSettle(view.BuildingScreenRect(BuildingType.Farm).GetCenter());
            Expect(building.Visible, $"Round {i}: a click on a building opens its popup");
            await CloseIt(building, byEscape, "the building popup");

            await OnVillage(BuildingType.Barracks);
            await ClickAndSettle(view.Card.GetNode<Button>("Box/Action"));
            Expect(troops.Visible, $"Round {i}: the Barracks card opens training");
            await CloseIt(troops, byEscape, "the training popup");

            await OnVillage(BuildingType.Marketplace);
            await ClickAndSettle(view.Card.GetNode<Button>("Box/Action"));
            Expect(send.Visible, $"Round {i}: the Marketplace card opens the transfer");
            await CloseIt(send, byEscape, "the transfer popup");

            game.ShowView(GameScreen.MainView.World);
            world.CentreOn(rival);
            await Frames();
            var at = world.GetGlobalTransform() * world.WorldToView(WorldMapView.VillageWorldPosition(rival));
            await MoveMouse(at);
            await Settle(0.5);
            var card = world.GetNode<VillagePopup>("Popup");
            Expect(card.Visible, $"Round {i}: resting the mouse on a rival opens its card");
            await ClickAndSettle(card.GetNode<Button>("%Raid"));
            Expect(send.Visible, $"Round {i}: the card's Raid opens the send popup");
            await CloseIt(send, byEscape, "the send popup");
            world.ClearSelection();
        }
    }

    private async Task OverlaysByMouse(GameScreen game, int rounds)
    {
        var bar = game.GetNode<ResourceBar>("%ResourceBar");
        var menu = bar.GetNode<Button>("%Pause");
        var pause = game.GetNode<Overlay>("%PauseOverlay");
        var leave = game.GetNode<Overlay>("%LeaveOverlay");
        var surrender = game.GetNode<Overlay>("%SurrenderOverlay");

        for (var i = 0; i < rounds * 2; i++)
        {
            var byEscape = i % 2 == 1;
            var how = byEscape ? "Esc" : "its button";
            async Task Dismiss(Button button)
            {
                if (byEscape)
                    await EscapeAndSettle();
                else
                    await ClickAndSettle(button);
            }

            await ClickAndSettle(menu);
            Expect(pause.Visible, $"Round {i}: the top bar opens the menu");
            await Dismiss(pause.GetNode<Button>("%Action"));
            Expect(!pause.Visible, $"Round {i}: {how} closes the menu");
            await CheckClicksWork(game, $"closing the menu with {how}");

            await ClickAndSettle(menu);
            await ClickAndSettle(pause.GetNode<Button>("%Secondary"));
            Expect(leave.Visible && !pause.Visible, $"Round {i}: Leave asks first");
            await Dismiss(leave.GetNode<Button>("%Secondary"));
            Expect(!leave.Visible && pause.Visible, $"Round {i}: {how} backs out of leaving, to the menu");
            await ClickAndSettle(pause.GetNode<Button>("%Tertiary"));
            Expect(surrender.Visible && !pause.Visible, $"Round {i}: Surrender asks first");
            await Dismiss(surrender.GetNode<Button>("%Secondary"));
            Expect(!surrender.Visible && pause.Visible, $"Round {i}: {how} backs out of surrendering, to the menu");
            var confirmQuit = game.GetNode<Overlay>("%QuitOverlay");
            await ClickAndSettle(pause.GetNode<Button>("%Quit"));
            Expect(confirmQuit.Visible && !pause.Visible, $"Round {i}: Quit asks first during a match");
            await Dismiss(confirmQuit.GetNode<Button>("%Secondary"));
            Expect(!confirmQuit.Visible && pause.Visible, $"Round {i}: {how} backs out of quitting, to the menu");
            await ClickAndSettle(pause.GetNode<Button>("%Action"));
            Expect(!pause.Visible, $"Round {i}: the menu closes after backing out");
            await CheckClicksWork(game, $"backing out of the confirmations with {how}");
        }
    }

    private async Task TestCapitalDialogByMouse(GameData data)
    {
        var (game, _) = await LoseCapital(data);
        var dialog = game.GetNode<CapitalDialog>("%CapitalDialog");
        var warning = game.GetNode<ResourceBar>("%ResourceBar").GetNode<Button>("%CapitalWarning");
        for (var i = 0; i < 6; i++)
        {
            var byEscape = i % 2 == 1;
            Expect(dialog.Visible, $"Round {i}: the capital dialog is open");
            if (byEscape)
                await EscapeAndSettle();
            else
                await ClickAndSettle(dialog.GetNode<Button>("%Later"));
            Expect(!dialog.Visible, $"Round {i}: {(byEscape ? "Esc" : "Later")} sets the capital dialog aside");
            await CheckClicksWork(game, $"setting the capital dialog aside with {(byEscape ? "Esc" : "Later")}");
            await ClickAndSettle(warning);
        }
    }

    private async Task TestEliminatedByMouse(GameData data)
    {
        var (game, session) = await StartMatch(data, Valkaris.Core.Dev.DevScenario.LastStand, players: 3, rivalAttacks: true);
        await RunUntilArrived(session);
        await Frames();
        var overlay = game.GetNode<Overlay>("%EliminatedOverlay");
        Expect(overlay.Visible, "Elimination is announced");
        await EscapeAndSettle();
        Expect(overlay.Visible, "Esc can't skip the choice after elimination");
        await ClickAndSettle(overlay.GetNode<Button>("%Action"));
        Expect(!overlay.Visible, "Keep watching closes the message");

        var drawer = game.GetNode<Drawer>("%Drawer");
        await ClickAndSettle(drawer.GetNode<Button>("Tabs/OverviewTab"));
        Expect(drawer.IsOpen && drawer.ActiveTab == Drawer.Tab.Overview, "A watcher's tab still responds");
        await ClickAndSettle(drawer.GetNode<Button>("Tabs/OverviewTab"));
        Expect(!drawer.IsOpen, "A watcher can close the drawer, and it stays closed");
        var world = game.GetNode<WorldMapView>("%WorldMapView");
        var hits = 0;
        void Count(InputEvent e) => hits += e is InputEventMouseButton ? 1 : 0;
        world.GuiInput += Count;
        await ClickAndSettle(EmptySpot(game));
        world.GuiInput -= Count;
        Expect(hits > 0, "A watcher's map still takes clicks");
    }

    /// <summary>The map or village, a drawer tab and a top-bar button all answer a real click. Leaves everything as it found it.</summary>
    private async Task CheckClicksWork(GameScreen game, string after)
    {
        var drawer = game.GetNode<Drawer>("%Drawer");
        var view = game.CurrentView;
        Control shown = view == GameScreen.MainView.World ? game.GetNode<Control>("%WorldMapView") : game.GetNode<Control>("%VillageView");

        var hits = 0;
        void Count(InputEvent e) => hits += e is InputEventMouseButton ? 1 : 0;
        shown.GuiInput += Count;
        await ClickAndSettle(EmptySpot(game));
        shown.GuiInput -= Count;
        Expect(hits > 0, $"After {after}: a click reaches the {view} view");

        var tab = drawer.GetNode<Button>("Tabs/OverviewTab");
        await ClickAndSettle(tab);
        Expect(drawer.IsOpen && drawer.ActiveTab == Drawer.Tab.Overview, $"After {after}: a click on a drawer tab opens it");
        await ClickAndSettle(tab);
        Expect(!drawer.IsOpen, $"After {after}: a click on the tab closes it");

        var bar = game.GetNode<ResourceBar>("%ResourceBar");
        var other = view == GameScreen.MainView.World ? GameScreen.MainView.Village : GameScreen.MainView.World;
        await ClickAndSettle(bar.GetNode<Button>(other == GameScreen.MainView.World ? "%ViewWorld" : "%ViewVillage"));
        Expect(game.CurrentView == other, $"After {after}: a click on the top bar switches the view");
        await ClickAndSettle(bar.GetNode<Button>(view == GameScreen.MainView.World ? "%ViewWorld" : "%ViewVillage"));
        Expect(game.CurrentView == view, $"After {after}: a click on the top bar switches back");
    }

    /// <summary>Near the left edge, clear of the top bar, the village strip, the drawer and the buildings.</summary>
    private static Vector2 EmptySpot(GameScreen game)
    {
        var area = game.GetNode<Control>("%MainArea").GetGlobalRect();
        return new Vector2(area.Position.X + 3, area.Position.Y + 70);
    }

    private async Task MoveMouse(Vector2 at)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }, true);
        await Frames(2);
    }

    private async Task ClickAndSettle(Control control) => await ClickAndSettle(control.GetGlobalRect().GetCenter());

    private async Task ClickAndSettle(Vector2 at)
    {
        await MoveMouse(at);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left }, true);
        await Frames(1);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at }, true);
        await Settle(0.2);
    }

    private async Task PressKeyEvent(Key key)
    {
        GetViewport().PushInput(new InputEventKey { Pressed = true, Keycode = key, PhysicalKeycode = key });
        await Frames(1);
        GetViewport().PushInput(new InputEventKey { Pressed = false, Keycode = key, PhysicalKeycode = key });
        await Frames(2);
    }

    private async Task EscapeAndSettle()
    {
        await PressKeyEvent(Key.Escape);
        await Settle(0.2);
    }

    /// <summary>Longer than the drawer's slide.</summary>
    private async Task Settle(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

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

    /// <summary>Text that doesn't fit, on the screen shown right now: anything past the window's edge, or cut off in its own box.</summary>
    private void CheckLayout(string screen)
    {
        var window = GetViewportRect().Size;
        foreach (var control in FindAll<Control>(this))
        {
            if (!control.IsVisibleInTree() || control.Size.X <= 0 || Scrolls(control))
                continue;

            var rect = control.GetGlobalRect();
            if (control is Label or Button or OptionButton or LineEdit && (rect.End.X > window.X + 1 || rect.End.Y > window.Y + 1 || rect.Position.X < -1))
                Expect(false, $"{screen}: {control.GetPath()} is outside the window, {rect} in {window}");

            var text = control switch
            {
                Label { AutowrapMode: TextServer.AutowrapMode.Off } label => label.Text,
                Button button and not OptionButton => button.Text,
                _ => "",
            };
            if (text.Length == 0 || text.Contains('\n'))
                continue;

            var font = control.GetThemeFont("font");
            var width = font.GetStringSize(control.Tr(text), HorizontalAlignment.Left, -1, control.GetThemeFontSize("font_size")).X;
            if (width > control.Size.X + 1)
                Expect(false, $"{screen}: '{control.Tr(text)}' is cut off in {control.GetPath()}, {width} in {control.Size.X}");
        }
    }

    /// <summary>The map moves under its frame, and scroll areas are meant to hold more than they show.</summary>
    private static bool Scrolls(Control control)
    {
        for (var parent = control.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is WorldMapView)
                return true;
            if (parent is ScrollContainer scroll && control.GetGlobalRect().End.Y > scroll.GetGlobalRect().End.Y)
                return true;
        }
        return false;
    }

    private async Task Shot(string name)
    {
        CheckLayout(name);
        if (_shotsDir is null || DisplayServer.GetName() == "headless")
            return;

        // Let view fades finish, so the picture shows the settled screen.
        await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng($"{_shotsDir}/{name}.png");
    }
}
