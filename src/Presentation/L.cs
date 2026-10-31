using Godot;

namespace Valkaris.Presentation;

/// <summary>Player-facing text from the translation files. Numbers arrive already formatted by Format.</summary>
public static class L
{
    public static string T(string key, params (string Name, string Value)[] arguments) =>
        Fill(TranslationServer.Translate(key), arguments);

    /// <summary>Picks the plural form for the count, which fills {count}.</summary>
    public static string N(string key, int count, params (string Name, string Value)[] arguments) =>
        Fill(TranslationServer.TranslatePlural(key, key, count), arguments).Replace("{count}", Format.Whole(count));

    private static string Fill(string text, (string Name, string Value)[] arguments)
    {
        foreach (var (name, value) in arguments)
            text = text.Replace("{" + name + "}", value);
        return text;
    }
}
