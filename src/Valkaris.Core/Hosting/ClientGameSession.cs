using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Network;
using Valkaris.Core.Snapshots;

namespace Valkaris.Core.Hosting;

/// <summary>A player in someone else's match. It runs no rules: it shows the latest view from the host, and only moves the clock between views.</summary>
public sealed class ClientGameSession : IGameSession
{
    /// <summary>How far the clock may run on without news, so a stalled host doesn't leave timers racing ahead.</summary>
    private const double MaxLeadSeconds = 2;

    private readonly INetworkClient _network;
    private readonly ViewMerger _merger = new();
    private IReadOnlyList<Clash> _clashes = Array.Empty<Clash>();
    private double _lastViewTime;

    public ClientGameSession(INetworkClient network, GameData data, MatchStartMessage start)
    {
        _network = network;
        Data = data;
        LocalPlayerId = start.PlayerId;
        State = null!;
        Apply(start.First.View);
        network.Received += OnReceived;
        network.Disconnected += OnDisconnected;
    }

    public GameState State { get; private set; }
    public GameData Data { get; }
    public int LocalPlayerId { get; }
    public bool CanPause => false;
    public bool IsPaused => false;
    public IReadOnlyList<SightedMovement> SightedMovements { get; private set; } = Array.Empty<SightedMovement>();
    public IReadOnlyList<PublicNotice> PublicNotices { get; private set; } = Array.Empty<PublicNotice>();

    /// <summary>The latest whole view, as the host sent it.</summary>
    public PlayerViewSnapshot LastView { get; private set; } = null!;

    /// <summary>Set once the connection to the host is gone.</summary>
    public bool HostLost { get; private set; }

    public event Action<GameEvent>? EventRaised;
    public event Action<CommandRejection>? CommandRejected;
    public event Action? ConnectionLost;

    public IReadOnlyList<Clash> Clashes(double after) => _clashes.Where(c => c.Time > after).ToList();

    public void SetPaused(bool paused) => throw new InvalidOperationException("A multiplayer match cannot be paused.");

    public void Send(ICommand command) => _network.Send(WireCodec.Encode(new CommandMessage(command)));

    public void Tick(double deltaSeconds)
    {
        _network.Poll();
        if (!State.IsMatchOver)
            State.ElapsedTime = Math.Min(State.ElapsedTime + deltaSeconds, _lastViewTime + MaxLeadSeconds);
    }

    private void OnReceived(byte[] bytes)
    {
        switch (WireCodec.DecodeFromHost(bytes))
        {
            case UpdateMessage update:
                Apply(update.View);
                foreach (var gameEvent in update.Events)
                    EventRaised?.Invoke(gameEvent);
                break;
            case RejectionMessage rejected:
                CommandRejected?.Invoke(rejected.Rejection);
                break;
        }
    }

    private void Apply(PlayerViewSnapshot trimmed)
    {
        LastView = _merger.Merge(trimmed);
        State = SnapshotMapper.Restore(LastView.State);
        _lastViewTime = State.ElapsedTime;
        SightedMovements = LastView.Sightings;
        PublicNotices = LastView.Notices;
        _clashes = LastView.Clashes;
    }

    private void OnDisconnected()
    {
        HostLost = true;
        ConnectionLost?.Invoke();
    }
}
