using System;
using System.Text;
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

    /// <summary>A key made from an enum value: "building" and MainBuilding give "building.main_building". Tests check that every value has one.</summary>
    public static string Key(string prefix, Enum value) => Key(prefix, value.ToString());

    /// <summary>A key made from an identifier in data, such as a tier name.</summary>
    public static string Key(string prefix, string identifier)
    {
        var key = new StringBuilder(prefix).Append('.');
        for (var i = 0; i < identifier.Length; i++)
        {
            if (i > 0 && char.IsUpper(identifier[i]))
                key.Append('_');
            key.Append(char.ToLowerInvariant(identifier[i]));
        }
        return key.ToString();
    }

    private static string Fill(string text, (string Name, string Value)[] arguments)
    {
        foreach (var (name, value) in arguments)
            text = text.Replace("{" + name + "}", value);
        return text;
    }
}
