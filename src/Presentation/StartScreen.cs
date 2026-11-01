using System;
using Godot;
using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>What the start screen asks for.</summary>
public sealed record MatchSetup(GameSettings Settings, int Opponents, AiDifficulty Difficulty, string? VillageName = null);

/// <summary>What is selected on the start screen, kept when a language change rebuilds it.</summary>
public sealed record StartChoices(int Duration, int Speed, int MapSize, int Opponents, int Difficulty, string VillageName);

/// <summary>Match settings. The options come from data/game.json.</summary>
public partial class StartScreen : CenterContainer
{
    private GameData _data = null!;
    private StartChoices? _choices;

    public event Action<MatchSetup>? StartRequested;
    public event Action? HostRequested;
    public event Action? JoinRequested;

    /// <summary>The language changed, so the screen needs rebuilding with these choices.</summary>
    public event Action<StartChoices>? LanguageChanged;

    public void Init(GameData data, StartChoices? choices = null)
    {
        _data = data;
        _choices = choices;
    }

    /// <summary>The same choices here and in the lobby.</summary>
    public static void FillMatchOptions(GameData data, OptionButton duration, OptionButton speed, OptionButton mapSize)
    {
        foreach (var minutes in data.Game.MatchDurationsMinutes)
            duration.AddItem(L.N("common.minutes", (int)minutes));
        if (data.Game.UnlimitedMatches)
            duration.AddItem(L.T("start.unlimited"));

        foreach (var multiplier in data.Game.SpeedMultipliers)
            speed.AddItem(Format.Multiplier(multiplier));

        foreach (var size in Enum.GetValues<MapSize>())
        {
            var definition = data.Game.MapSizes[size];
            mapSize.AddItem(L.T("start.map_size", ("size", L.T(L.Key("map_size", size))), ("width", Format.Whole(definition.Width)), ("height", Format.Whole(definition.Height))));
        }
    }

    /// <summary>Seconds, or null for Unlimited, which comes after the durations.</summary>
    public static double? ChosenDuration(GameData data, OptionButton duration)
    {
        var durations = data.Game.MatchDurationsMinutes;
        return duration.Selected < durations.Count ? durations[duration.Selected] * 60 : null;
    }

    public override void _Ready()
    {
        var duration = GetNode<OptionButton>("%Duration");
        var speed = GetNode<OptionButton>("%Speed");
        var mapSizes = Enum.GetValues<MapSize>();
        var mapSize = GetNode<OptionButton>("%MapSize");
        FillMatchOptions(_data, duration, speed, mapSize);

        var opponents = GetNode<OptionButton>("%Opponents");
        for (var count = _data.Game.MinPlayers - 1; count <= _data.Game.MaxPlayers - 1; count++)
            opponents.AddItem(Format.Whole(count));

        var difficulties = Enum.GetValues<AiDifficulty>();
        var difficulty = GetNode<OptionButton>("%Difficulty");
        foreach (var level in difficulties)
            difficulty.AddItem(L.T(L.Key("difficulty", level)));
        difficulty.Select(Array.IndexOf(difficulties, AiDifficulty.Normal));

        var villageName = GetNode<LineEdit>("%VillageName");
        villageName.MaxLength = _data.Game.VillageNames.MaxLength;
        villageName.TextChanged += _ => CheckVillageName();

        if (_choices is { } choices)
        {
            villageName.Text = choices.VillageName;
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
            LanguageChanged?.Invoke(new StartChoices(duration.Selected, speed.Selected, mapSize.Selected, opponents.Selected, difficulty.Selected, villageName.Text));
        };
        CheckVillageName();

        var settings = GetNode<SettingsScreen>("%SettingsScreen");
        settings.LanguageChosen += code =>
        {
            Language.Choose(code);
            LanguageChanged?.Invoke(new StartChoices(duration.Selected, speed.Selected, mapSize.Selected, opponents.Selected, difficulty.Selected, villageName.Text));
        };
        GetNode<Button>("%Settings").Pressed += settings.Open;
        GetNode<Button>("%Host").Pressed += () => HostRequested?.Invoke();
        GetNode<Button>("%Join").Pressed += () => JoinRequested?.Invoke();
        GetNode<Button>("%Start").Pressed += () =>
        {
            var seconds = ChosenDuration(_data, duration);
            var multiplier = _data.Game.SpeedMultipliers[speed.Selected];
            var settings = new GameSettings(seconds, multiplier, mapSizes[mapSize.Selected]);
            StartRequested?.Invoke(new MatchSetup(settings, _data.Game.MinPlayers - 1 + opponents.Selected, difficulties[difficulty.Selected], villageName.Text));
        };
    }

    /// <summary>Empty is fine, and means a random name from data.</summary>
    private void CheckVillageName()
    {
        var name = VillageNameRules.Clean(GetNode<LineEdit>("%VillageName").Text);
        var rejection = name.Length == 0 ? null : VillageNameRules.Rejection(_data, Array.Empty<string>(), name);

        var reason = GetNode<Label>("%VillageNameReason");
        reason.Visible = rejection is not null;
        reason.Text = rejection is { } value ? UiContent.RejectionText(value) : "";
        GetNode<Button>("%Start").Disabled = rejection is not null;
    }
}
