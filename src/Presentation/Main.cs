using System.Globalization;
using Godot;
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

    private GameData _data = null!;
    private Control? _screen;

    public override void _Ready()
    {
        // The UI is English, so numbers must not follow the player's system locale.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        DisplayServer.WindowSetMinSize(MinWindowSize);

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

    private void ShowStartScreen()
    {
        var screen = StartScene.Instantiate<StartScreen>();
        screen.Init(_data);
        screen.StartRequested += StartMatch;
        SwitchTo(screen);
    }

    private void StartMatch(GameSettings settings)
    {
        // Two slots are the minimum. The rival stays idle until AI arrives in milestone 6.
        var slots = new[]
        {
            new PlayerSlot("You", Nation.Valdren, IsAi: false),
            new PlayerSlot("Rival", Nation.Karuk, IsAi: true),
        };
        var state = GameFactory.CreateMatch(settings, slots, _data);

        var screen = GameScene.Instantiate<GameScreen>();
        screen.Init(new LocalGameSession(state, _data, LocalPlayerId));
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
        box.AddChild(new Label { Text = "The game data could not be loaded", ThemeTypeVariation = "HeadingLabel" });
        box.AddChild(new Label { Text = message, AutowrapMode = TextServer.AutowrapMode.WordSmart });

        card.AddChild(box);
        center.AddChild(card);
        SwitchTo(center);
    }
}
