using Godot;

namespace Valkaris.Presentation;

/// <summary>What the game remembers between launches, next to the language.</summary>
public static class UserSettings
{
    private const string Player = "player";
    private const string Name = "name";
    private const string Display = "display";
    private const string FullscreenKey = "fullscreen";

    public static string PlayerName
    {
        get => Load().GetValue(Player, Name, "").AsString();
        set => Save(Player, Name, value);
    }

    /// <summary>On by default, so a first launch opens fullscreen.</summary>
    public static bool Fullscreen
    {
        get => Load().GetValue(Display, FullscreenKey, true).AsBool();
        set => Save(Display, FullscreenKey, value);
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
