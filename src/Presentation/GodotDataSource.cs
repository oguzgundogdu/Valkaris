using Godot;
using Valkaris.Core.Data;

namespace Valkaris.Presentation;

/// <summary>Reads data files from res://data through Godot, so it also works in exported builds.</summary>
public sealed class GodotDataSource : IDataSource
{
    public string ReadText(string fileName)
    {
        var path = $"res://data/{fileName}";
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null)
            throw new GameDataException($"{fileName}: cannot open {path} ({FileAccess.GetOpenError()}).");
        return file.GetAsText();
    }
}
