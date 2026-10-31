using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Network;

/// <summary>Runs the lobby on the host's machine until the match starts.</summary>
public sealed class LobbyHost
{
    private readonly INetworkHost _network;
    private readonly GameData _data;
    private readonly string _dataHash;

    public LobbyHost(INetworkHost network, GameData data, string dataHash, string hostName, string? hostVillage)
    {
        _network = network;
        _data = data;
        _dataHash = dataHash;
        State = new LobbyState(data, hostName, hostVillage);
        _network.Received += OnReceived;
        _network.PeerDisconnected += OnDisconnected;
    }

    public LobbyState State { get; }

    public event Action? Changed;

    public void Poll() => _network.Poll();

    public void SetSlot(int index, SlotKind kind, AiDifficulty difficulty = AiDifficulty.Normal)
    {
        if (State.SetSlot(index, kind, difficulty) is { } removed)
            Refuse(removed, new RefusedMessage(JoinRefusal.RemovedByHost));
        Announce();
    }

    public void SetSettings(double? durationSeconds, double speed, MapSize mapSize)
    {
        if (State.SetSettings(durationSeconds, speed, mapSize))
            Announce();
    }

    /// <summary>Starts the match, and from here the session handles the connections.</summary>
    public HostGameSession Start(ulong seed, Func<LocalGameSession, int, IMatchAgent> makeAgent)
    {
        var plan = State.Plan();
        _network.Received -= OnReceived;
        _network.PeerDisconnected -= OnDisconnected;

        var game = GameFactory.CreateMatch(State.Settings with { Seed = seed }, plan.Players, _data);
        var inner = new LocalGameSession(game, _data, 1);
        foreach (var player in plan.AiPlayers)
            inner.AddAgent(makeAgent(inner, player));

        var session = new HostGameSession(inner, _network, makeAgent);
        foreach (var (peer, player) in plan.PlayerOfPeer)
            session.AddClient(peer, player);
        return session;
    }

    /// <summary>Tells someone why they can't stay, and lets them go.</summary>
    private void Refuse(int peer, RefusedMessage message)
    {
        _network.Send(peer, WireCodec.Encode(message));
        _network.Disconnect(peer);
    }

    private void OnReceived(int peer, byte[] bytes)
    {
        if (State.SlotOfPeer(peer) is not null || WireCodec.DecodeFromClient(bytes) is not HelloMessage hello)
            return;

        if (hello.Version != Handshake.Version)
            Refuse(peer, new RefusedMessage(JoinRefusal.VersionMismatch));
        else if (hello.DataHash != _dataHash)
            Refuse(peer, new RefusedMessage(JoinRefusal.DataMismatch));
        else if (State.Join(peer, hello.PlayerName, hello.VillageName) is { } refused)
            Refuse(peer, new RefusedMessage(refused.Reason, refused.Village));
        else
            Announce();
    }

    private void OnDisconnected(int peer)
    {
        if (State.SlotOfPeer(peer) is not null)
        {
            State.Leave(peer);
            Announce();
        }
    }

    private void Announce()
    {
        var bytes = WireCodec.Encode(new LobbyMessage(State.Slots, State.Settings));
        foreach (var peer in State.Peers)
            _network.Send(peer, bytes);
        Changed?.Invoke();
    }
}

/// <summary>A player's way into a lobby, up to the point where the match starts.</summary>
public sealed class LobbyClient
{
    private readonly INetworkClient _network;
    private readonly GameData _data;
    private bool _finished;

    public LobbyClient(INetworkClient network, GameData data, string dataHash, string playerName, string? villageName, string version = Handshake.Version)
    {
        _network = network;
        _data = data;
        var hello = WireCodec.Encode(new HelloMessage(version, dataHash, playerName, villageName));
        network.Connected += () => network.Send(hello);
        network.Received += OnReceived;
        network.Disconnected += OnDisconnected;
    }

    /// <summary>The latest copy of the seats and settings. Null until accepted.</summary>
    public LobbyMessage? Lobby { get; private set; }

    public event Action? LobbyChanged;
    public event Action<RefusedMessage>? Refused;
    public event Action? ConnectionLost;
    public event Action<ClientGameSession>? MatchStarted;

    public void Poll() => _network.Poll();

    public void Leave()
    {
        _finished = true;
        _network.Close();
    }

    private void OnReceived(byte[] bytes)
    {
        if (_finished)
            return;

        switch (WireCodec.DecodeFromHost(bytes))
        {
            case LobbyMessage lobby:
                Lobby = lobby;
                LobbyChanged?.Invoke();
                break;
            case RefusedMessage refused:
                _finished = true;
                Refused?.Invoke(refused);
                break;
            case MatchStartMessage start:
                _finished = true;
                _network.Received -= OnReceived;
                _network.Disconnected -= OnDisconnected;
                MatchStarted?.Invoke(new ClientGameSession(_network, _data, start));
                break;
        }
    }

    private void OnDisconnected()
    {
        if (_finished)
            return;

        _finished = true;
        ConnectionLost?.Invoke();
    }
}
