using Godot;

namespace Valkaris.Presentation;

/// <summary>The name someone last played under, remembered next to the language.</summary>
public static class PlayerSettings
{
    private const string Section = "player";
    private const string Key = "name";

    public static string LoadName()
    {
        var config = new ConfigFile();
        return config.Load(Language.SettingsPath) == Error.Ok ? config.GetValue(Section, Key, "").AsString() : "";
    }

    public static void SaveName(string name)
    {
        var config = new ConfigFile();
        config.Load(Language.SettingsPath);
        config.SetValue(Section, Key, name);
        config.Save(Language.SettingsPath);
    }
}
