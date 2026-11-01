using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Valkaris.Presentation;

public enum SoundEffect
{
    Hover,
    Click,
    BuildingComplete,
    TrainingComplete,
    AttackWarning,
    BattleClash,
    VillageCaptured,
    Victory,
    Defeat,
}

/// <summary>Plays the music and effects, and gives every button its hover and click without any screen asking for it.</summary>
public partial class Sound : Node
{
    private const string MasterBus = "Master";
    private const string MusicBus = "Music";
    private const string EffectsBus = "Effects";
    private const string MusicPath = "res://assets/audio/music.ogg";
    private const int Voices = 8;
    private const ulong RepeatLimitMs = 70;

    private static readonly Dictionary<SoundEffect, (string Path, float VolumeDb)> Effects = new()
    {
        [SoundEffect.Hover] = ("res://assets/audio/ui_hover.ogg", -16),
        [SoundEffect.Click] = ("res://assets/audio/ui_click.ogg", -8),
        [SoundEffect.BuildingComplete] = ("res://assets/audio/building_complete.ogg", -6),
        [SoundEffect.TrainingComplete] = ("res://assets/audio/training_complete.ogg", -6),
        [SoundEffect.AttackWarning] = ("res://assets/audio/attack_warning.ogg", -3),
        [SoundEffect.BattleClash] = ("res://assets/audio/battle_clash.ogg", -4),
        [SoundEffect.VillageCaptured] = ("res://assets/audio/village_captured.ogg", -3),
        [SoundEffect.Victory] = ("res://assets/audio/victory.ogg", 0),
        [SoundEffect.Defeat] = ("res://assets/audio/defeat.ogg", 0),
    };

    private static Sound? _instance;

    private readonly Dictionary<SoundEffect, AudioStream> _streams = new();
    private readonly Dictionary<SoundEffect, ulong> _lastPlayed = new();
    private readonly List<AudioStreamPlayer> _voices = new();

    /// <summary>Lets the UI test see what played, since a headless run has no audio to listen to.</summary>
    public static List<SoundEffect>? Recorded { get; set; }

    public static void Play(SoundEffect effect) => _instance?.PlayEffect(effect);

    public static bool IsMuted => AudioServer.IsBusMute(AudioServer.GetBusIndex(MasterBus));

    public static void SetMuted(bool muted)
    {
        UserSettings.Muted = muted;
        ApplySettings();
    }

    /// <summary>Puts the saved volumes and mute on the buses.</summary>
    public static void ApplySettings()
    {
        SetBus(MasterBus, UserSettings.MasterVolume);
        SetBus(MusicBus, UserSettings.MusicVolume);
        SetBus(EffectsBus, UserSettings.EffectsVolume);
        AudioServer.SetBusMute(AudioServer.GetBusIndex(MasterBus), UserSettings.Muted);
    }

    private static void SetBus(string bus, float volume) =>
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(bus), Mathf.LinearToDb(volume));

    public override void _Ready()
    {
        _instance = this;
        foreach (var (effect, (path, _)) in Effects)
            _streams[effect] = GD.Load<AudioStream>(path);

        for (var i = 0; i < Voices; i++)
        {
            var voice = new AudioStreamPlayer { Bus = EffectsBus };
            AddChild(voice);
            _voices.Add(voice);
        }

        ApplySettings();
        GetTree().NodeAdded += OnNodeAdded;
        StartMusic();
    }

    public override void _ExitTree()
    {
        GetTree().NodeAdded -= OnNodeAdded;
        if (_instance == this)
            _instance = null;

        // Held streams outlive the tree otherwise, and Godot complains at exit.
        foreach (var player in GetChildren().OfType<AudioStreamPlayer>())
            player.Stream = null;
        _streams.Clear();
    }

    private void StartMusic()
    {
        var music = GD.Load<AudioStreamOggVorbis>(MusicPath);
        music.Loop = true;
        var player = new AudioStreamPlayer { Stream = music, Bus = MusicBus, Autoplay = true };
        AddChild(player);
    }

    private static void OnNodeAdded(Node node)
    {
        if (node is not BaseButton button)
            return;

        button.MouseEntered += () =>
        {
            if (!button.Disabled)
                Play(SoundEffect.Hover);
        };
        button.Pressed += () => Play(SoundEffect.Click);
    }

    private void PlayEffect(SoundEffect effect)
    {
        var now = Time.GetTicksMsec();
        if (_lastPlayed.TryGetValue(effect, out var last) && now - last < RepeatLimitMs)
            return;

        _lastPlayed[effect] = now;
        Recorded?.Add(effect);

        var voice = _voices.Find(v => !v.Playing) ?? _voices[0];
        voice.Stream = _streams[effect];
        voice.VolumeDb = Effects[effect].VolumeDb;
        voice.Play();
    }
}
