using System;
using Godot;
using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>What the start screen asks for.</summary>
public sealed record MatchSetup(GameSettings Settings, int Opponents, AiDifficulty Difficulty);

/// <summary>What is selected on the start screen, kept when a language change rebuilds it.</summary>
public sealed record StartChoices(int Duration, int Speed, int MapSize, int Opponents, int Difficulty);

/// <summary>Match settings. The options come from data/game.json.</summary>
public partial class StartScreen : CenterContainer
{
    private GameData _data = null!;
    private StartChoices? _choices;

    public event Action<MatchSetup>? StartRequested;

    /// <summary>The language changed, so the screen needs rebuilding with these choices.</summary>
    public event Action<StartChoices>? LanguageChanged;

    public void Init(GameData data, StartChoices? choices = null)
    {
        _data = data;
        _choices = choices;
    }

    public override void _Ready()
    {
        var duration = GetNode<OptionButton>("%Duration");
        foreach (var minutes in _data.Game.MatchDurationsMinutes)
            duration.AddItem(L.N("common.minutes", (int)minutes));
        if (_data.Game.UnlimitedMatches)
            duration.AddItem("Unlimited");

        var speed = GetNode<OptionButton>("%Speed");
        foreach (var multiplier in _data.Game.SpeedMultipliers)
            speed.AddItem($"{Format.Trimmed(multiplier)}x");

        var mapSizes = Enum.GetValues<MapSize>();
        var mapSize = GetNode<OptionButton>("%MapSize");
        foreach (var size in mapSizes)
        {
            var definition = _data.Game.MapSizes[size];
            mapSize.AddItem(L.T("start.map_size", ("size", L.T(L.Key("map_size", size))), ("width", Format.Whole(definition.Width)), ("height", Format.Whole(definition.Height))));
        }

        var opponents = GetNode<OptionButton>("%Opponents");
        for (var count = _data.Game.MinPlayers - 1; count <= _data.Game.MaxPlayers - 1; count++)
            opponents.AddItem(Format.Whole(count));

        var difficulties = Enum.GetValues<AiDifficulty>();
        var difficulty = GetNode<OptionButton>("%Difficulty");
        foreach (var level in difficulties)
            difficulty.AddItem(L.T(L.Key("difficulty", level)));
        difficulty.Select(Array.IndexOf(difficulties, AiDifficulty.Normal));

        if (_choices is { } choices)
        {
            duration.Select(choices.Duration);
            speed.Select(choices.Speed);
            mapSize.Select(choices.MapSize);
            opponents.Select(choices.Opponents);
            difficulty.Select(choices.Difficulty);
        }

        var language = GetNode<OptionButton>("%Language");
        foreach (var code in Language.Codes)
            language.AddItem(L.T(L.Key("language", code)));
        language.Select(Array.IndexOf(Language.Codes, Language.Current));
        language.ItemSelected += index =>
        {
            Language.Choose(Language.Codes[index]);
            LanguageChanged?.Invoke(new StartChoices(duration.Selected, speed.Selected, mapSize.Selected, opponents.Selected, difficulty.Selected));
        };

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
