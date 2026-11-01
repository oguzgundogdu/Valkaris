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
    public const string UnitsFile = "units.json";
    public const string CombatFile = "combat.json";
    public const string MapFile = "map.json";
    public const string VillagesFile = "villages.json";
    public const string ScoringFile = "scoring.json";
    public const string AiFile = "ai.json";
    public const string NationsFile = "nations.json";

    public static readonly IReadOnlyList<string> Files = new[] { GameFile, BuildingsFile, UnitsFile, CombatFile, MapFile, VillagesFile, ScoringFile, AiFile, NationsFile };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    internal GameData(GameConfig game, BuildingsConfig buildings, UnitsConfig units, CombatConfig combat, MapConfig map, VillagesConfig villages, ScoringConfig scoring, AiConfig ai, NationsConfig nations)
    {
        Game = game;
        Buildings = buildings;
        Units = units;
        Combat = combat;
        Map = map;
        Villages = villages;
        Scoring = scoring;
        Ai = ai;
        Nations = nations;
    }

    public GameConfig Game { get; }
    public BuildingsConfig Buildings { get; }
    public UnitsConfig Units { get; }
    public CombatConfig Combat { get; }
    public MapConfig Map { get; }
    public VillagesConfig Villages { get; }
    public ScoringConfig Scoring { get; }
    public AiConfig Ai { get; }
    public NationsConfig Nations { get; }

    public static GameData Load(IDataSource source)
    {
        var game = Parse<GameConfig>(source, GameFile);
        var buildings = Parse<BuildingsConfig>(source, BuildingsFile);
        var units = Parse<UnitsConfig>(source, UnitsFile);
        var combat = Parse<CombatConfig>(source, CombatFile);
        var map = Parse<MapConfig>(source, MapFile);
        var villages = Parse<VillagesConfig>(source, VillagesFile);
        var scoring = Parse<ScoringConfig>(source, ScoringFile);
        var ai = Parse<AiConfig>(source, AiFile);
        var nations = Parse<NationsConfig>(source, NationsFile);

        var errors = GameDataValidator.Validate(game, buildings, units, combat, map, villages, scoring, ai, nations);
        if (errors.Count > 0)
            throw new GameDataException("Invalid game data:\n" + string.Join("\n", errors));

        return new GameData(game, buildings, units, combat, map, villages, scoring, ai, nations);
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
