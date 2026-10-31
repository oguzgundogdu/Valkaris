using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Godot;
using Valkaris.Ai;
using Valkaris.Core.Data;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>Loads the game data and switches between the top-level screens.</summary>
public partial class Main : Control
{
    private const int LocalPlayerId = 1;

    private static readonly Vector2I MinWindowSize = new(1024, 640);
    private static readonly PackedScene StartScene = GD.Load<PackedScene>("res://scenes/StartScreen.tscn");
    private static readonly PackedScene GameScene = GD.Load<PackedScene>("res://scenes/Game.tscn");

    /// <summary>Dev only: opponents stand still, for scripted tests that need a predictable match.</summary>
    public bool IdleOpponents { get; set; }

    private GameData _data = null!;
    private Control? _screen;

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
        GetTree().AutoAcceptQuit = false;

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

        ShowStartScreen();
    }

    public override async void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest)
            return;

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
        SwitchTo(screen);
    }

    private void StartMatch(MatchSetup setup)
    {
        var nations = new[] { Nation.Karuk, Nation.Solari, Nation.Valdren };
        var slots = new List<PlayerSlot> { new("You", Nation.Valdren, IsAi: false, VillageName: setup.VillageName) };
        for (var i = 0; i < setup.Opponents; i++)
            slots.Add(new PlayerSlot(_data.Ai.Names[i], nations[i % nations.Length], IsAi: true, setup.Difficulty));

        var devOptions = DevOptions.Parse(OS.GetCmdlineUserArgs(), OS.IsDebugBuild());
        var seeded = devOptions.Apply(setup.Settings) with { Seed = (ulong)Random.Shared.NextInt64() };
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
