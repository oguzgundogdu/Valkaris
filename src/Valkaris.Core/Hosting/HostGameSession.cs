using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Network;
using Valkaris.Core.Snapshots;

namespace Valkaris.Core.Hosting;

/// <summary>The host of a multiplayer match: the real match runs here, and each client gets its own view of it.</summary>
public sealed class HostGameSession : IGameSession
{
    private readonly INetworkHost _network;
    private readonly Dictionary<int, Client> _clients = new();
    private readonly double _snapshotInterval;
    private const AiDifficulty TakeoverDifficulty = AiDifficulty.Normal;

    private double _sinceSnapshot;

    private readonly Func<LocalGameSession, int, IMatchAgent> _makeAgent;

    public HostGameSession(LocalGameSession inner, INetworkHost network, Func<LocalGameSession, int, IMatchAgent> makeAgent)
    {
        Inner = inner;
        _network = network;
        _makeAgent = makeAgent;
        _snapshotInterval = 1 / inner.Data.Game.SnapshotsPerSecond;
        inner.EventRaised += OnEvent;
        inner.CommandRejected += rejection => CommandRejected?.Invoke(rejection);
        network.Received += OnReceived;
        network.PeerDisconnected += OnPeerDisconnected;
    }

    public LocalGameSession Inner { get; }
    public GameState State => Inner.State;
    public GameData Data => Inner.Data;
    public int LocalPlayerId => Inner.LocalPlayerId;
    public bool CanPause => false;
    public bool IsPaused => false;

    public IReadOnlyList<SightedMovement> SightedMovements => Inner.SightedMovements;
    public IReadOnlyList<PublicNotice> PublicNotices => Inner.PublicNotices;
    public IReadOnlyList<Clash> Clashes(double after) => Inner.Clashes(after);

    public event Action<GameEvent>? EventRaised;
    public event Action<CommandRejection>? CommandRejected;

    /// <summary>The whole view behind each packet, as the client should rebuild it. Lets tests compare the two.</summary>
    internal event Action<int, PlayerViewSnapshot>? ViewSent;

    public void SetPaused(bool paused) => throw new InvalidOperationException("A multiplayer match cannot be paused.");

    public void Send(ICommand command) => Inner.Send(command);

    /// <summary>Connects a peer to a player, and sends them the match so far.</summary>
    public void AddClient(int peer, int playerId)
    {
        var client = new Client(playerId);
        _clients[peer] = client;
        _network.Send(peer, WireCodec.Encode(new MatchStartMessage(playerId, Update(client))));
    }

    /// <summary>The host leaves, which ends the match for everyone.</summary>
    public void Stop() => _network.Stop();

    public void Tick(double deltaSeconds)
    {
        _network.Poll();
        Inner.Tick(deltaSeconds);

        _sinceSnapshot += deltaSeconds;
        var due = _sinceSnapshot >= _snapshotInterval;
        if (due)
            _sinceSnapshot -= _snapshotInterval * Math.Floor(_sinceSnapshot / _snapshotInterval);

        foreach (var (peer, client) in _clients)
        {
            if (due || client.Dirty)
                _network.Send(peer, WireCodec.Encode(Update(client)));
        }
    }

    private UpdateMessage Update(Client client)
    {
        var view = PlayerView.Of(State, Data, client.PlayerId);
        var events = client.Events.ToList();
        client.Events.Clear();
        client.Dirty = false;
        ViewSent?.Invoke(client.PlayerId, view);
        return new UpdateMessage(client.Trimmer.Trim(view), events);
    }

    private void OnEvent(GameEvent gameEvent)
    {
        foreach (var client in _clients.Values)
        {
            if (PlayerView.Hears(State, gameEvent, client.PlayerId))
            {
                client.Events.Add(gameEvent);
                client.Dirty = true;
            }
        }
        EventRaised?.Invoke(gameEvent);
    }

    private void OnReceived(int peer, byte[] bytes)
    {
        var decoded = WireCodec.DecodeFromClient(bytes);
        if (!_clients.TryGetValue(peer, out var client))
        {
            // Nobody can join a match under way.
            if (decoded is HelloMessage)
            {
                _network.Send(peer, WireCodec.Encode(new RefusedMessage(JoinRefusal.MatchInProgress)));
                _network.Disconnect(peer);
            }
            return;
        }
        if (decoded is not CommandMessage message)
            return;

        // The sender comes from the connection. A command can't claim to be from someone else.
        var result = Inner.SendAs(client.PlayerId, message.Command);
        if (result.Rejection is { } rejection)
            _network.Send(peer, WireCodec.Encode(new RejectionMessage(rejection)));
        else
            client.Dirty = true;
    }

    private void OnPeerDisconnected(int peer)
    {
        if (!_clients.Remove(peer, out var client) || State.IsMatchOver)
            return;

        var player = State.Players[client.PlayerId];
        if (player.IsEliminated)
            return;

        player.TakeOverByAi(TakeoverDifficulty);
        Inner.AddAgent(_makeAgent(Inner, player.Id));
        OnEvent(new PlayerDisconnected(State.ElapsedTime, player.Id));
    }

    private sealed class Client
    {
        public Client(int playerId) => PlayerId = playerId;

        public int PlayerId { get; }
        public ViewTrimmer Trimmer { get; } = new();
        public List<GameEvent> Events { get; } = new();
        public bool Dirty { get; set; }
    }
}
