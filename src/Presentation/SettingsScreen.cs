using System;
using Godot;

namespace Valkaris.Presentation;

/// <summary>Audio, fullscreen and language, from the start screen or the in-game menu. Every change applies and saves at once.</summary>
public partial class SettingsScreen : ColorRect
{
    private HSlider _master = null!;
    private HSlider _music = null!;
    private HSlider _effects = null!;
    private CheckBox _mute = null!;
    private CheckBox _fullscreen = null!;
    private OptionButton _language = null!;

    /// <summary>Most text is set once when a screen is built, so a match keeps the language it started in.</summary>
    public bool LanguageLocked { get; set; }

    public event Action<string>? LanguageChosen;

    public override void _Ready()
    {
        _master = GetNode<HSlider>("%Master");
        _music = GetNode<HSlider>("%Music");
        _effects = GetNode<HSlider>("%Effects");
        _mute = GetNode<CheckBox>("%Mute");
        _fullscreen = GetNode<CheckBox>("%Fullscreen");
        _language = GetNode<OptionButton>("%Language");

        foreach (var code in Language.Codes)
            _language.AddItem(L.T($"language.{code}"));

        _master.ValueChanged += value => SetVolume(() => UserSettings.MasterVolume = (float)value);
        _music.ValueChanged += value => SetVolume(() => UserSettings.MusicVolume = (float)value);
        _effects.ValueChanged += value => SetVolume(() => UserSettings.EffectsVolume = (float)value);
        _effects.DragEnded += _ => Sound.Play(SoundEffect.Click);
        _mute.Toggled += Sound.SetMuted;
        _fullscreen.Toggled += Main.SetFullscreen;
        _language.ItemSelected += index => LanguageChosen?.Invoke(Language.Codes[index]);
        GetNode<Button>("%Close").Pressed += Close;
    }

    public void Open()
    {
        _master.SetValueNoSignal(UserSettings.MasterVolume);
        _music.SetValueNoSignal(UserSettings.MusicVolume);
        _effects.SetValueNoSignal(UserSettings.EffectsVolume);
        _mute.SetPressedNoSignal(Sound.IsMuted);
        _fullscreen.SetPressedNoSignal(UserSettings.Fullscreen);
        _language.Select(Array.IndexOf(Language.Codes, Language.Current));
        _language.Disabled = LanguageLocked;
        GetNode<Control>("%LanguageNote").Visible = LanguageLocked;
        Visible = true;
    }

    public void Close() => Visible = false;

    private static void SetVolume(Action save)
    {
        save();
        Sound.ApplySettings();
    }
}
