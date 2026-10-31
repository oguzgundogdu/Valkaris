namespace Valkaris.Core.Network;

/// <summary>Host and clients in one process, for tests. Delay keeps messages in order, as ENet's reliable channel does.</summary>
public sealed class MemoryNetwork
{
    private readonly Random? _random;
    private readonly double _maxDelay;
    private double _now;

    public MemoryNetwork(int? delaySeed = null, double maxDelaySeconds = 0)
    {
        _random = delaySeed is { } seed ? new Random(seed) : null;
        _maxDelay = maxDelaySeconds;
        Host = new MemoryHost(this);
    }

    public MemoryHost Host { get; }

    /// <summary>Lets time pass, so delayed messages come due.</summary>
    public void Advance(double seconds) => _now += seconds;

    public MemoryClient Connect()
    {
        var client = new MemoryClient(this, Host.NextPeer());
        Host.Attach(client);
        return client;
    }

    private double DueTime() => _now + (_random?.NextDouble() ?? 0) * _maxDelay;

    /// <summary>One direction of one connection.</summary>
    private sealed class Link
    {
        private readonly MemoryNetwork _network;
        private readonly Queue<(double Due, Action Deliver)> _queue = new();
        private double _last;

        public Link(MemoryNetwork network) => _network = network;

        public void Add(Action deliver)
        {
            _last = Math.Max(_last, _network.DueTime());
            _queue.Enqueue((_last, deliver));
        }

        public void Run()
        {
            while (_queue.TryPeek(out var next) && next.Due <= _network._now)
            {
                _queue.Dequeue();
                next.Deliver();
            }
        }
    }

    public sealed class MemoryHost : INetworkHost
    {
        private readonly MemoryNetwork _network;
        private readonly Dictionary<int, MemoryClient> _clients = new();
        private readonly Dictionary<int, Link> _incoming = new();
        private int _nextPeer = 1;

        internal MemoryHost(MemoryNetwork network) => _network = network;

        public event Action<int>? PeerConnected;
        public event Action<int>? PeerDisconnected;
        public event Action<int, byte[]>? Received;

        internal int NextPeer() => _nextPeer++;

        internal void Attach(MemoryClient client)
        {
            _clients[client.Peer] = client;
            var link = new Link(_network);
            _incoming[client.Peer] = link;
            link.Add(() => PeerConnected?.Invoke(client.Peer));
            client.Deliver(client.RaiseConnected);
        }

        internal void FromClient(int peer, byte[] message)
        {
            if (_incoming.TryGetValue(peer, out var link))
                link.Add(() => Received?.Invoke(peer, message));
        }

        internal void ClientLeft(int peer)
        {
            if (_clients.Remove(peer) && _incoming.TryGetValue(peer, out var link))
                link.Add(() => PeerDisconnected?.Invoke(peer));
        }

        public void Send(int peer, byte[] message)
        {
            if (_clients.TryGetValue(peer, out var client))
                client.Deliver(() => client.RaiseReceived(message));
        }

        public void Disconnect(int peer)
        {
            if (_clients.Remove(peer, out var client))
            {
                client.Deliver(client.RaiseDisconnected);
                _incoming[peer].Add(() => PeerDisconnected?.Invoke(peer));
            }
        }

        public void Poll()
        {
            foreach (var link in _incoming.Values.ToList())
                link.Run();
        }

        public void Stop()
        {
            foreach (var peer in _clients.Keys.ToList())
                Disconnect(peer);
        }
    }

    public sealed class MemoryClient : INetworkClient
    {
        private readonly MemoryNetwork _network;
        private readonly Link _incoming;
        private bool _closed;

        internal MemoryClient(MemoryNetwork network, int peer)
        {
            _network = network;
            Peer = peer;
            _incoming = new Link(network);
        }

        public int Peer { get; }

        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<byte[]>? Received;

        internal void Deliver(Action action) => _incoming.Add(action);
        internal void RaiseConnected() => Connected?.Invoke();
        internal void RaiseDisconnected() => Disconnected?.Invoke();
        internal void RaiseReceived(byte[] message) => Received?.Invoke(message);

        public void Send(byte[] message)
        {
            if (!_closed)
                _network.Host.FromClient(Peer, message);
        }

        public void Poll()
        {
            if (!_closed)
                _incoming.Run();
        }

        public void Close()
        {
            if (_closed)
                return;

            _closed = true;
            _network.Host.ClientLeft(Peer);
        }
    }
}
