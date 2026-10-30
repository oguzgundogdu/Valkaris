using System.Collections.ObjectModel;

namespace Valkaris.Core.Model;

public sealed class GameState
{
    private readonly Dictionary<int, Player> _players = new();
    private readonly Dictionary<int, Village> _villages = new();
    private readonly List<Movement> _movements = new();
    private readonly List<BattleReport> _reports = new();

    internal GameState(GameSettings settings, GameMap map)
    {
        Settings = settings;
        Map = map;
        Players = new ReadOnlyDictionary<int, Player>(_players);
        Villages = new ReadOnlyDictionary<int, Village>(_villages);
        Movements = _movements.AsReadOnly();
        Reports = _reports.AsReadOnly();
        RngState = settings.Seed;
    }

    public GameSettings Settings { get; }

    /// <summary>Real seconds since the match started. The speed multiplier does not scale it.</summary>
    public double ElapsedTime { get; internal set; }

    public IReadOnlyDictionary<int, Player> Players { get; }
    public IReadOnlyDictionary<int, Village> Villages { get; }
    public GameMap Map { get; }

    /// <summary>Troops on the way, in the order they were sent.</summary>
    public IReadOnlyList<Movement> Movements { get; }

    public IReadOnlyList<BattleReport> Reports { get; }

    public int NextMovementId { get; internal set; } = 1;
    public int NextReportId { get; internal set; } = 1;

    /// <summary>Set once, when the match ends.</summary>
    public MatchOutcome? Outcome { get; internal set; }

    /// <summary>The seeded generator's state, kept here so a restored snapshot continues identically.</summary>
    public ulong RngState { get; internal set; }

    public bool IsMatchOver => ElapsedTime >= Settings.MatchDurationSeconds;

    internal void AddPlayer(Player player) => _players.Add(player.Id, player);

    internal void AddVillage(Village village) => _villages.Add(village.Id, village);

    internal void AddMovement(Movement movement) => _movements.Add(movement);

    internal void RemoveMovement(Movement movement) => _movements.Remove(movement);

    internal void AddReport(BattleReport report) => _reports.Add(report);

    /// <summary>SplitMix64. System.Random cannot be saved and restored.</summary>
    internal ulong NextRandom()
    {
        RngState += 0x9E3779B97F4A7C15UL;
        var z = RngState;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A random integer from 0 up to, but not including, the limit.</summary>
    internal int NextRandom(int exclusiveLimit) => (int)(NextRandom() % (ulong)exclusiveLimit);
}
