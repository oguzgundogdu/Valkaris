using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Godot;
using Valkaris.Ai;
using Valkaris.Core.Data;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Network;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>Loads the game data and switches between the top-level screens.</summary>
public partial class Main : Control
{
    private const int LocalPlayerId = 1;

    private static readonly Vector2I MinWindowSize = new(1024, 640);
    private static readonly PackedScene StartScene = GD.Load<PackedScene>("res://scenes/StartScreen.tscn");
    private static readonly PackedScene IntroScene = GD.Load<PackedScene>("res://scenes/IntroScreen.tscn");
    private static readonly PackedScene GameScene = GD.Load<PackedScene>("res://scenes/Game.tscn");
    private static readonly PackedScene ConnectScene = GD.Load<PackedScene>("res://scenes/ConnectScreen.tscn");
    private static readonly PackedScene LobbyScene = GD.Load<PackedScene>("res://scenes/LobbyScreen.tscn");

    /// <summary>Dev only: tests go straight to the start screen.</summary>
    public static bool SkipIntro { get; set; }

    /// <summary>Dev only: opponents stand still, for scripted tests that need a predictable match.</summary>
    public bool IdleOpponents { get; set; }

    private GameData _data = null!;
    private string _lastVillage = "";
    private string _lastAddress = "";
    private Control? _screen;
    private Action? _leaveMatch;

    /// <summary>Dev only: lets tests see a quit without ending the run.</summary>
    public static Action? QuitForTest { get; set; }

    public override void _Ready()
    {
        // Code states its culture wherever it matters. This is a safety net for players, and the tests turn it off to prove that.
        var options = DevOptions.Parse(OS.GetCmdlineUserArgs(), OS.IsDebugBuild());
        var culture = options.Culture is { } name
            ? CultureInfo.GetCultureInfo(name)
            : CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;
        Language.Apply(options.Language ?? Language.Load());

        DisplayServer.WindowSetMinSize(MinWindowSize);
        ApplyWindowMode();
        GetTree().AutoAcceptQuit = false;
        AddChild(new Sound { Name = "Sound" });
        AddChild(new Cursors { Name = "Cursors" });

        try
        {
            _data = GameData.Load(new GodotDataSource());
        }
        catch (GameDataException ex)
        {
            GD.PrintErr(ex.Message);
            ShowErrorScreen(ex.Message);
            return;
        }

        if (SkipIntro)
        {
            ShowStartScreen();
            return;
        }

        var intro = IntroScene.Instantiate<IntroScreen>();
        intro.Finished += ShowStartScreen;
        SwitchTo(intro);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F11 })
        {
            ToggleFullscreen();
            GetViewport().SetInputAsHandled();
        }
    }

    public void ToggleFullscreen() => SetFullscreen(!UserSettings.Fullscreen);

    public static void SetFullscreen(bool fullscreen)
    {
        UserSettings.Fullscreen = fullscreen;
        ApplyWindowMode();
    }

    private static void ApplyWindowMode() =>
        DisplayServer.WindowSetMode(UserSettings.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            Quit();
    }

    /// <summary>Every way out comes here: the buttons, Cmd+Q and Alt+F4. Whoever is connected gets told at once.</summary>
    public async void Quit()
    {
        _leaveMatch?.Invoke();
        _leaveMatch = null;
        if (QuitForTest is { } quitForTest)
        {
            quitForTest();
            return;
        }

        // Quitting with live scenes can abort inside Godot's shutdown (mutex lock failed), so free them first.
        _screen?.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit();
    }

    private void ShowStartScreen() => ShowStartScreen(null);

    private void ShowStartScreen(StartChoices? choices)
    {
        var screen = StartScene.Instantiate<StartScreen>();
        screen.Init(_data, choices);
        screen.StartRequested += StartMatch;
        screen.LanguageChanged += ShowStartScreen;
        screen.QuitRequested += Quit;
        screen.HostRequested += () => ShowConnect(hosting: true);
        screen.JoinRequested += () => ShowConnect(hosting: false);
        SwitchTo(screen);
    }

    private void ShowConnect(bool hosting, string? error = null)
    {
        var screen = ConnectScene.Instantiate<ConnectScreen>();
        screen.Init(_data, hosting, error, _lastVillage, _lastAddress);
        screen.BackRequested += ShowStartScreen;
        screen.ContinueRequested += (name, village, address) =>
        {
            _lastVillage = village ?? "";
            _lastAddress = address;
            if (hosting)
                Host(name, village);
            else
                Join(name, village, address);
        };
        SwitchTo(screen);
    }

    private void Host(string name, string? village)
    {
        if (EnetHost.Listen(Handshake.DefaultPort) is { } network)
            OpenHostLobby(network, name, village, NetAddress.Shareable(IP.GetLocalAddresses()), Handshake.DefaultPort);
        else
            ShowConnect(hosting: true, L.T("connect.port_busy", ("port", Handshake.PortText(Handshake.DefaultPort))));
    }

    private void Join(string name, string? village, string address)
    {
        if (NetAddress.Parse(address) is { } target && EnetClient.Connect(target.Host, target.Port) is { } network)
            OpenClientLobby(network, name, village);
        else
            ShowConnect(hosting: false, L.T("connect.bad_address"));
    }

    /// <summary>Takes any network, so the UI test can use one in memory.</summary>
    public void OpenHostLobby(INetworkHost network, string name, string? village, IReadOnlyList<string> addresses, int port)
    {
        var lobby = new LobbyHost(network, _data, Handshake.DataHash(new GodotDataSource()), name, village);
        var screen = LobbyScene.Instantiate<LobbyScreen>();
        screen.InitHost(_data, lobby, addresses, port);
        screen.LeaveRequested += () =>
        {
            network.Stop();
            ShowStartScreen();
        };
        screen.HostStarted += session => PlayNetworkMatch(session, session.Stop);
        SwitchTo(screen);
    }

    /// <summary>Takes any network, so the UI test can use one in memory.</summary>
    public void OpenClientLobby(INetworkClient network, string name, string? village)
    {
        var client = new LobbyClient(network, _data, Handshake.DataHash(new GodotDataSource()), name, village);
        var screen = LobbyScene.Instantiate<LobbyScreen>();
        screen.InitClient(_data, client);
        screen.LeaveRequested += () =>
        {
            client.Leave();
            ShowStartScreen();
        };
        screen.Failed += reason =>
        {
            network.Close();
            ShowConnect(hosting: false, reason);
        };
        screen.ClientStarted += session => PlayNetworkMatch(session, session.Leave);
        SwitchTo(screen);
    }

    /// <summary>Never looks at DevOptions: dev settings can't reach a multiplayer match.</summary>
    public void PlayNetworkMatch(IGameSession session, Action leave)
    {
        var screen = GameScene.Instantiate<GameScreen>();
        screen.Init(session);
        _leaveMatch = leave;
        screen.QuitRequested += Quit;
        screen.ExitRequested += () =>
        {
            _leaveMatch = null;
            leave();
            ShowStartScreen();
        };
        SwitchTo(screen);
    }

    private void StartMatch(MatchSetup setup)
    {
        var devOptions = DevOptions.Parse(OS.GetCmdlineUserArgs(), OS.IsDebugBuild());
        var seeded = devOptions.Apply(setup.Settings) with { Seed = (ulong)Random.Shared.NextInt64(), SpectatorsSeeAll = true };

        var slots = new List<PlayerSlot> { new("You", NationPicker.Resolve(setup.Nation, seeded.Seed, 0), IsAi: false, VillageName: setup.VillageName) };
        for (var i = 0; i < setup.Opponents; i++)
            slots.Add(new PlayerSlot(_data.Ai.Names[i], NationPicker.Resolve(setup.OpponentNation, seeded.Seed, i + 1), IsAi: true, setup.Difficulty));

        var state = GameFactory.CreateMatch(seeded, slots, _data);
#if VALKARIS_DEV
        if (devOptions.Scenario is { } scenario && !Valkaris.Core.Dev.DevScenario.Apply(state, _data, scenario))
            GD.PrintErr($"Unknown dev scenario: {scenario}");
#endif

        var session = new LocalGameSession(state, _data, LocalPlayerId);
        if (!IdleOpponents && !devOptions.IdleAi)
        {
            foreach (var slot in state.Players.Values.Where(p => p.IsAi))
            {
                var id = slot.Id;
                session.AddAgent(new AiPlayer(new AiView(state, _data, id, command => session.SendAs(id, command))));
            }
        }

        var screen = GameScene.Instantiate<GameScreen>();
        screen.Init(session);
        screen.QuitRequested += Quit;
        screen.ExitRequested += ShowStartScreen;
#if VALKARIS_DEV
        if (devOptions.God)
        {
            session.SetGodMode(true);
            var godMode = new GodModeControl();
            godMode.Init(session);
            screen.AddChild(godMode);
        }
#endif
        SwitchTo(screen);
    }

    private void SwitchTo(Control screen)
    {
        _screen?.QueueFree();
        _screen = screen;
        AddChild(screen);
    }

    private void ShowErrorScreen(string message)
    {
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);

        var card = new PanelContainer { ThemeTypeVariation = "CardPanel", CustomMinimumSize = new Vector2(720, 0) };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        box.AddChild(new Label { Text = L.T("error.data"), ThemeTypeVariation = "HeadingLabel" });
        box.AddChild(new Label { Text = message, AutowrapMode = TextServer.AutowrapMode.WordSmart });

        card.AddChild(box);
        center.AddChild(card);
        SwitchTo(center);
    }
}
