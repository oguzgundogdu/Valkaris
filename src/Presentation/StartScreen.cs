using System;
using Godot;
using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>Match settings. The options come from data/game.json.</summary>
public partial class StartScreen : CenterContainer
{
    private GameData _data = null!;

    public event Action<GameSettings>? StartRequested;

    public void Init(GameData data) => _data = data;

    public override void _Ready()
    {
        var duration = GetNode<OptionButton>("%Duration");
        foreach (var minutes in _data.Game.MatchDurationsMinutes)
            duration.AddItem($"{minutes:0} minutes");

        var speed = GetNode<OptionButton>("%Speed");
        foreach (var multiplier in _data.Game.SpeedMultipliers)
            speed.AddItem($"{multiplier:0.##}x");

        var mapSizes = Enum.GetValues<MapSize>();
        var mapSize = GetNode<OptionButton>("%MapSize");
        foreach (var size in mapSizes)
        {
            var definition = _data.Game.MapSizes[size];
            mapSize.AddItem($"{size} ({definition.Width} × {definition.Height})");
        }

        GetNode<Button>("%Start").Pressed += () =>
        {
            var minutes = _data.Game.MatchDurationsMinutes[duration.Selected];
            var multiplier = _data.Game.SpeedMultipliers[speed.Selected];
            StartRequested?.Invoke(new GameSettings(minutes * 60, multiplier, mapSizes[mapSize.Selected]));
        };
    }
}
