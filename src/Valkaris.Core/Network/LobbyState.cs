using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Network;

public enum SlotKind
{
    Host,
    Open,
    Ai,
    Closed,
}

/// <summary>Why someone can't be in the lobby, or no longer is.</summary>
public enum JoinRefusal
{
    VersionMismatch,
    DataMismatch,
    LobbyFull,
    MatchInProgress,
    PlayerNameInvalid,
    PlayerNameTaken,
    VillageNameRefused,
    RemovedByHost,
}

/// <summary>One seat. PlayerName is set when a person holds it.</summary>
public sealed record LobbySlot(SlotKind Kind, AiDifficulty Difficulty = AiDifficulty.Normal, string? PlayerName = null, string? VillageName = null, Nation? Nation = null);

/// <summary>Who plays which player once the match starts.</summary>
public sealed record LobbyPlan(IReadOnlyList<PlayerSlot> Players, IReadOnlyDictionary<int, int> PlayerOfPeer, IReadOnlyList<int> AiPlayers);

/// <summary>The seats and settings of a match about to start. The host's machine holds this, and clients get copies.</summary>
public sealed class LobbyState
{
    private readonly GameData _data;
    private readonly LobbySlot[] _slots;
    private readonly int?[] _peers;

    public LobbyState(GameData data, string hostName, string? hostVillage)
    {
        _data = data;
        _slots = Enumerable.Repeat(new LobbySlot(SlotKind.Open), data.Game.MaxPlayers).ToArray();
        _peers = new int?[data.Game.MaxPlayers];
        _slots[0] = new LobbySlot(SlotKind.Host, PlayerName: VillageNameRules.Clean(hostName), VillageName: CleanOrNull(hostVillage));
        Settings = new GameSettings(data.Game.MatchDurationsMinutes[0] * 60, data.Game.SpeedMultipliers[0], Enum.GetValues<MapSize>()[0]);
    }

    public IReadOnlyList<LobbySlot> Slots => _slots;
    public GameSettings Settings { get; private set; }

    public int PlayerCount => _slots.Count(s => s.Kind is SlotKind.Host or SlotKind.Ai || s.PlayerName is not null);

    /// <summary>Seats left open with nobody in them count as closed.</summary>
    public bool CanStart => PlayerCount >= _data.Game.MinPlayers;

    public IEnumerable<int> Peers => _peers.OfType<int>();

    public int? SlotOfPeer(int peer)
    {
        var index = Array.IndexOf(_peers, peer);
        return index < 0 ? null : index;
    }

    /// <summary>Puts a person in the first free seat. Null if they got one.</summary>
    public (JoinRefusal Reason, CommandRejection? Village)? Join(int peer, string playerName, string? villageName)
    {
        var name = VillageNameRules.Clean(playerName ?? "");
        if (VillageNameRules.FormatRejection(_data.Game.VillageNames, name) is not null)
            return (JoinRefusal.PlayerNameInvalid, null);
        if (_slots.Any(s => s.PlayerName is { } taken && VillageNameRules.Folded(taken) == VillageNameRules.Folded(name)))
            return (JoinRefusal.PlayerNameTaken, null);

        var village = CleanOrNull(villageName);
        if (village is not null)
        {
            var others = _slots.Select(s => s.VillageName).OfType<string>();
            if (VillageNameRules.Rejection(_data, others, village) is { } rejection)
                return (JoinRefusal.VillageNameRefused, rejection);
        }

        var free = Array.FindIndex(_slots, s => s.Kind == SlotKind.Open && s.PlayerName is null);
        if (free < 0)
            return (JoinRefusal.LobbyFull, null);

        _slots[free] = new LobbySlot(SlotKind.Open, PlayerName: name, VillageName: village);
        _peers[free] = peer;
        return null;
    }

    public void Leave(int peer)
    {
        if (SlotOfPeer(peer) is { } slot)
        {
            _slots[slot] = new LobbySlot(SlotKind.Open);
            _peers[slot] = null;
        }
    }

    /// <summary>The host changes a seat. Anyone in it is removed, and their connection is returned so they can be told.</summary>
    public int? SetSlot(int index, SlotKind kind, AiDifficulty difficulty = AiDifficulty.Normal)
    {
        if (index < 0 || index >= _slots.Length || _slots[index].Kind == SlotKind.Host || kind == SlotKind.Host)
            throw new ArgumentException("The host's own seat can't change, and nobody else can become the host.");

        // Opening a seat someone already holds changes nothing.
        if (kind == SlotKind.Open && _slots[index].PlayerName is not null)
            return null;

        var removed = _peers[index];
        _peers[index] = null;
        _slots[index] = new LobbySlot(kind, difficulty, Nation: kind == SlotKind.Ai && _slots[index].Kind == SlotKind.Ai ? _slots[index].Nation : null);
        return removed;
    }

    /// <summary>A seat's nation, or Random with null. Only a seat that plays can have one.</summary>
    public bool SetNation(int index, Nation? nation)
    {
        if (index < 0 || index >= _slots.Length || (nation is { } chosen && !Enum.IsDefined(chosen)))
            return false;
        if (_slots[index].Kind == SlotKind.Closed || (_slots[index].Kind == SlotKind.Open && _slots[index].PlayerName is null))
            return false;

        _slots[index] = _slots[index] with { Nation = nation };
        return true;
    }

    /// <summary>Only the choices the start screen offers are accepted.</summary>
    public bool SetSettings(double? durationSeconds, double speed, MapSize mapSize)
    {
        var durationOk = durationSeconds is { } seconds
            ? _data.Game.MatchDurationsMinutes.Any(minutes => minutes * 60 == seconds)
            : _data.Game.UnlimitedMatches;
        if (!durationOk || !_data.Game.SpeedMultipliers.Contains(speed) || !_data.Game.MapSizes.ContainsKey(mapSize))
            return false;

        Settings = new GameSettings(durationSeconds, speed, mapSize, SpectatorsSeeAll: Settings.SpectatorsSeeAll);
        return true;
    }

    public void SetSpectatorsSeeAll(bool value) => Settings = Settings with { SpectatorsSeeAll = value };

    /// <summary>Turns the seats into players, with the host as player 1 and no gaps.</summary>
    public LobbyPlan Plan(ulong seed = 0)
    {
        if (!CanStart)
            throw new InvalidOperationException("Not enough players to start.");

        var taken = _slots.Select(s => s.PlayerName).OfType<string>().Select(VillageNameRules.Folded).ToHashSet();
        var aiNames = new Queue<string>(_data.Ai.Names.Where(n => !taken.Contains(VillageNameRules.Folded(n))));
        var players = new List<PlayerSlot>();
        var peers = new Dictionary<int, int>();
        var ais = new List<int>();

        for (var i = 0; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            var nation = NationPicker.Resolve(slot.Nation, seed, players.Count);
            if (slot.Kind == SlotKind.Ai)
            {
                players.Add(new PlayerSlot(aiNames.Dequeue(), nation, IsAi: true, slot.Difficulty));
                ais.Add(players.Count);
            }
            else if (slot.PlayerName is { } name)
            {
                players.Add(new PlayerSlot(name, nation, IsAi: false, VillageName: slot.VillageName));
                if (_peers[i] is { } peer)
                    peers[peer] = players.Count;
            }
        }
        return new LobbyPlan(players, peers, ais);
    }

    private static string? CleanOrNull(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : VillageNameRules.Clean(name);
}
