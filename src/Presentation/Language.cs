using System;
using Godot;

namespace Valkaris.Presentation;

/// <summary>The display language: the player's choice if there is one, otherwise the system's if the game has it.</summary>
public static class Language
{
    private const string Section = "language";
    private const string Key = "code";
    private const string Default = "en";

    public static readonly string[] Codes = { "en", "tr" };

    /// <summary>The UI test points this elsewhere, so it can't change the language saved for real play.</summary>
    public static string SettingsPath { get; set; } = "user://settings.cfg";

    public static string Current { get; private set; } = Default;

    /// <summary>A first launch uses the system language if the game has it, and English otherwise.</summary>
    public static string Pick(string? saved, string systemLanguage)
    {
        if (saved is not null && Array.IndexOf(Codes, saved) >= 0)
            return saved;
        return Array.IndexOf(Codes, systemLanguage) >= 0 ? systemLanguage : Default;
    }

    public static string Load()
    {
        var config = new ConfigFile();
        var saved = config.Load(SettingsPath) == Error.Ok ? config.GetValue(Section, Key, "").AsString() : null;
        return Pick(saved, OS.GetLocaleLanguage());
    }

    public static void Apply(string code)
    {
        Current = code;
        TranslationServer.SetLocale(code);
    }

    /// <summary>Applies the player's choice and remembers it.</summary>
    public static void Choose(string code)
    {
        Apply(code);

        var config = new ConfigFile();
        config.Load(SettingsPath);
        config.SetValue(Section, Key, code);
        config.Save(SettingsPath);
    }
}
