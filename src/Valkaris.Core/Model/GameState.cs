using System.Collections.ObjectModel;

namespace Valkaris.Core.Model;

public sealed class GameState
{
    private readonly Dictionary<int, Player> _players = new();
    private readonly Dictionary<int, Village> _villages = new();
    private readonly List<Movement> _movements = new();
    private readonly List<BattleReport> _reports = new();
    private readonly List<ScoutReport> _scoutReports = new();
    private readonly List<ScoutNotice> _scoutNotices = new();
    private readonly List<BattleArrival> _battleArrivals = new();

    internal GameState(GameSettings settings, GameMap map)
    {
        Settings = settings;
        Map = map;
        Players = new ReadOnlyDictionary<int, Player>(_players);
        Villages = new ReadOnlyDictionary<int, Village>(_villages);
        Movements = _movements.AsReadOnly();
        Reports = _reports.AsReadOnly();
        ScoutReports = _scoutReports.AsReadOnly();
        ScoutNotices = _scoutNotices.AsReadOnly();
        BattleArrivals = _battleArrivals.AsReadOnly();
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

    /// <summary>Show one only to its sender. Ids are shared with battle reports.</summary>
    public IReadOnlyList<ScoutReport> ScoutReports { get; }

    /// <summary>Show one only to its defender. Ids are shared with battle reports.</summary>
    public IReadOnlyList<ScoutNotice> ScoutNotices { get; }

    /// <summary>Every Attack and Raid that reached its target, oldest first. Read through Visibility.</summary>
    public IReadOnlyList<BattleArrival> BattleArrivals { get; }

    public int NextMovementId { get; internal set; } = 1;
    public int NextReportId { get; internal set; } = 1;

    /// <summary>Set once, when the match ends.</summary>
    public MatchOutcome? Outcome { get; internal set; }

    /// <summary>The seeded generator's state, kept here so a restored snapshot continues identically.</summary>
    public ulong RngState { get; internal set; }

    public bool IsMatchOver => Outcome is not null;

#if VALKARIS_DEV
    /// <summary>Dev only: the player god mode applies to. Deliberately left out of snapshots.</summary>
    internal int? GodPlayerId { get; set; }

    /// <summary>Everything this player has under way is due now.</summary>
    internal void RushWork(int playerId)
    {
        foreach (var village in _villages.Values.Where(v => v.OwnerId == playerId))
            village.RushQueues(ElapsedTime);
        for (var i = 0; i < _movements.Count; i++)
        {
            if (_movements[i].OwnerId == playerId)
                _movements[i] = _movements[i] with { ArrivalTime = ElapsedTime };
        }
    }
#endif

    internal void AddPlayer(Player player) => _players.Add(player.Id, player);

    internal void AddVillage(Village village) => _villages.Add(village.Id, village);

    internal void AddMovement(Movement movement) => _movements.Add(movement);

    internal void RemoveMovement(Movement movement) => _movements.Remove(movement);

    internal void AddReport(BattleReport report) => _reports.Add(report);

    internal void AddScoutReport(ScoutReport report) => _scoutReports.Add(report);

    internal void AddScoutNotice(ScoutNotice notice) => _scoutNotices.Add(notice);

    internal void AddBattleArrival(BattleArrival arrival) => _battleArrivals.Add(arrival);

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

    /// <summary>From 0 up to, but not including, 1.</summary>
    internal double NextRandomShare() => (NextRandom() >> 11) * (1.0 / (1UL << 53));
}
