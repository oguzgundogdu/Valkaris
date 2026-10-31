using System;
using Godot;
using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>What the start screen asks for.</summary>
public sealed record MatchSetup(GameSettings Settings, int Opponents, AiDifficulty Difficulty);

/// <summary>Match settings. The options come from data/game.json.</summary>
public partial class StartScreen : CenterContainer
{
    private GameData _data = null!;

    public event Action<MatchSetup>? StartRequested;

    public void Init(GameData data) => _data = data;

    public override void _Ready()
    {
        var duration = GetNode<OptionButton>("%Duration");
        foreach (var minutes in _data.Game.MatchDurationsMinutes)
            duration.AddItem($"{minutes:0} minutes");
        if (_data.Game.UnlimitedMatches)
            duration.AddItem("Unlimited");

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

        var opponents = GetNode<OptionButton>("%Opponents");
        for (var count = _data.Game.MinPlayers - 1; count <= _data.Game.MaxPlayers - 1; count++)
            opponents.AddItem(count.ToString());

        var difficulties = Enum.GetValues<AiDifficulty>();
        var difficulty = GetNode<OptionButton>("%Difficulty");
        foreach (var level in difficulties)
            difficulty.AddItem(level.ToString());
        difficulty.Select(Array.IndexOf(difficulties, AiDifficulty.Normal));

        GetNode<Button>("%Start").Pressed += () =>
        {
            // Unlimited comes after the durations.
            var durations = _data.Game.MatchDurationsMinutes;
            double? seconds = duration.Selected < durations.Count ? durations[duration.Selected] * 60 : null;
            var multiplier = _data.Game.SpeedMultipliers[speed.Selected];
            var settings = new GameSettings(seconds, multiplier, mapSizes[mapSize.Selected]);
            StartRequested?.Invoke(new MatchSetup(settings, _data.Game.MinPlayers - 1 + opponents.Selected, difficulties[difficulty.Selected]));
        };
    }
}
