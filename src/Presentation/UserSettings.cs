using System;
using Godot;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>What the game remembers between launches, next to the language.</summary>
public static class UserSettings
{
    private const string Player = "player";
    private const string Name = "name";
    private const string NationKey = "nation";
    private const string Display = "display";
    private const string FullscreenKey = "fullscreen";
    private const string DrawerKey = "drawer_height";
    private const float DefaultDrawerHeight = 0.2f;
    private const string Audio = "audio";
    private const string MasterKey = "master";
    private const string MusicKey = "music";
    private const string EffectsKey = "effects";
    private const string MutedKey = "muted";
    private const float DefaultMaster = 1f;
    private const float DefaultMusic = 0.35f;
    private const float DefaultEffects = 0.8f;

    public static string PlayerName
    {
        get => Load().GetValue(Player, Name, "").AsString();
        set => Save(Player, Name, value);
    }

    /// <summary>Null is Random.</summary>
    public static Nation? Nation
    {
        get => Enum.TryParse<Nation>(Load().GetValue(Player, NationKey, "").AsString(), out var nation) ? nation : null;
        set => Save(Player, NationKey, value?.ToString() ?? "");
    }

    /// <summary>On by default, so a first launch opens fullscreen.</summary>
    public static bool Fullscreen
    {
        get => Load().GetValue(Display, FullscreenKey, true).AsBool();
        set => Save(Display, FullscreenKey, value);
    }

    public static float DrawerHeight
    {
        get => (float)Load().GetValue(Display, DrawerKey, DefaultDrawerHeight).AsDouble();
        set => Save(Display, DrawerKey, value);
    }

    public static float MasterVolume
    {
        get => (float)Load().GetValue(Audio, MasterKey, DefaultMaster).AsDouble();
        set => Save(Audio, MasterKey, value);
    }

    public static float MusicVolume
    {
        get => (float)Load().GetValue(Audio, MusicKey, DefaultMusic).AsDouble();
        set => Save(Audio, MusicKey, value);
    }

    public static float EffectsVolume
    {
        get => (float)Load().GetValue(Audio, EffectsKey, DefaultEffects).AsDouble();
        set => Save(Audio, EffectsKey, value);
    }

    public static bool Muted
    {
        get => Load().GetValue(Audio, MutedKey, false).AsBool();
        set => Save(Audio, MutedKey, value);
    }

    private static ConfigFile Load()
    {
        var config = new ConfigFile();
        config.Load(Language.SettingsPath);
        return config;
    }

    private static void Save(string section, string key, Variant value)
    {
        var config = Load();
        config.SetValue(section, key, value);
        config.Save(Language.SettingsPath);
    }
}
