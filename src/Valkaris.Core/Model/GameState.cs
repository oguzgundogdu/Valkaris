using System.Collections.ObjectModel;

namespace Valkaris.Core.Model;

public sealed class GameState
{
    private readonly Dictionary<int, Player> _players = new();
    private readonly Dictionary<int, Village> _villages = new();

    internal GameState(GameSettings settings, GameMap map)
    {
        Settings = settings;
        Map = map;
        Players = new ReadOnlyDictionary<int, Player>(_players);
        Villages = new ReadOnlyDictionary<int, Village>(_villages);
    }

    public GameSettings Settings { get; }

    /// <summary>Real seconds since the match started. The speed multiplier does not scale it.</summary>
    public double ElapsedTime { get; internal set; }

    public IReadOnlyDictionary<int, Player> Players { get; }
    public IReadOnlyDictionary<int, Village> Villages { get; }
    public GameMap Map { get; }

    public bool IsMatchOver => ElapsedTime >= Settings.MatchDurationSeconds;

    internal void AddPlayer(Player player) => _players.Add(player.Id, player);

    internal void AddVillage(Village village) => _villages.Add(village.Id, village);
}
