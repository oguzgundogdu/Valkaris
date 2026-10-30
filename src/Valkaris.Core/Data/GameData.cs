using System.Text.Json;
using System.Text.Json.Serialization;

namespace Valkaris.Core.Data;

/// <summary>Reads a data file by name. Hides where files live, since Core cannot read res:// paths.</summary>
public interface IDataSource
{
    string ReadText(string fileName);
}

public sealed class GameDataException : Exception
{
    public GameDataException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public sealed class GameData
{
    public const string GameFile = "game.json";
    public const string BuildingsFile = "buildings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    internal GameData(GameConfig game, BuildingsConfig buildings)
    {
        Game = game;
        Buildings = buildings;
    }

    public GameConfig Game { get; }
    public BuildingsConfig Buildings { get; }

    public static GameData Load(IDataSource source)
    {
        var game = Parse<GameConfig>(source, GameFile);
        var buildings = Parse<BuildingsConfig>(source, BuildingsFile);
        return new GameData(game, buildings);
    }

    private static T Parse<T>(IDataSource source, string fileName) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(source.ReadText(fileName), JsonOptions)
                ?? throw new GameDataException($"{fileName}: file is empty or null.");
        }
        catch (JsonException ex)
        {
            throw new GameDataException($"{fileName}: {ex.Message}", ex);
        }
    }
}
