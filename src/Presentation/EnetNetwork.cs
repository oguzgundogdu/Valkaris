using System;
using System.Collections.Generic;
using Godot;
using Valkaris.Core.Network;

namespace Valkaris.Presentation;

/// <summary>What the host and client share: compression, a quick timeout and reliable sends.</summary>
internal static class Enet
{
    public const int MaxClients = 8;
    public const int Channel = 0;
    private const int TimeoutLimit = 32;
    private const int TimeoutMilliseconds = 8000;

    public static void Configure(ENetPacketPeer peer) => peer.SetTimeout(TimeoutLimit, TimeoutMilliseconds, TimeoutMilliseconds);

    public static void Send(ENetPacketPeer peer, byte[] message) => peer.Send(Channel, message, (int)ENetPacketPeer.FlagReliable);

    /// <summary>The next thing that happened on this connection, if any.</summary>
    public static bool Next(ENetConnection connection, out ENetConnection.EventType type, out ENetPacketPeer peer)
    {
        var result = connection.Service(0);
        type = result[0].As<ENetConnection.EventType>();
        peer = result[1].As<ENetPacketPeer>();
        return type != ENetConnection.EventType.None && type != ENetConnection.EventType.Error;
    }
}

/// <summary>The host's end, over ENet.</summary>
public sealed class EnetHost : INetworkHost
{
    private readonly ENetConnection _connection = new();
    private readonly Dictionary<int, ENetPacketPeer> _peers = new();
    private readonly Dictionary<ulong, int> _numbers = new();
    private int _nextPeer = 1;
    private bool _stopped;

    private EnetHost()
    {
    }

    public event Action<int>? PeerConnected;
    public event Action<int>? PeerDisconnected;
    public event Action<int, byte[]>? Received;

    /// <summary>Null if the port can't be used, for example because something else holds it.</summary>
    public static EnetHost? Listen(int port)
    {
        var host = new EnetHost();
        if (host._connection.CreateHostBound(Handshake.AnyAddress, port, Enet.MaxClients) != Error.Ok)
            return null;

        host._connection.Compress(ENetConnection.CompressionMode.Zstd);
        return host;
    }

    /// <summary>Bytes sent since last asked, after compression, for measuring.</summary>
    public double TakeBytesSent() => _connection.PopStatistic(ENetConnection.HostStatistic.SentData);

    public void Send(int peer, byte[] message)
    {
        if (_stopped || !_peers.TryGetValue(peer, out var target))
            return;

        Enet.Send(target, message);
        _connection.Flush();
    }

    public void Disconnect(int peer)
    {
        if (!_stopped && _peers.TryGetValue(peer, out var target))
        {
            target.PeerDisconnectLater();
            _connection.Flush();
        }
    }

    public void Poll()
    {
        while (!_stopped && Enet.Next(_connection, out var type, out var peer))
        {
            var id = peer.GetInstanceId();
            switch (type)
            {
                case ENetConnection.EventType.Connect:
                    var number = _nextPeer++;
                    _numbers[id] = number;
                    _peers[number] = peer;
                    Enet.Configure(peer);
                    PeerConnected?.Invoke(number);
                    break;
                case ENetConnection.EventType.Disconnect:
                    if (_numbers.Remove(id, out var gone))
                    {
                        _peers.Remove(gone);
                        PeerDisconnected?.Invoke(gone);
                    }
                    break;
                case ENetConnection.EventType.Receive:
                    var bytes = peer.GetPacket();
                    if (_numbers.TryGetValue(id, out var sender))
                        Received?.Invoke(sender, bytes);
                    break;
            }
        }
    }

    public void Stop()
    {
        if (_stopped)
            return;

        _stopped = true;
        foreach (var peer in _peers.Values)
            peer.PeerDisconnect();
        _connection.Flush();
        _connection.Destroy();
    }
}

/// <summary>A client's end, over ENet.</summary>
public sealed class EnetClient : INetworkClient
{
    private readonly ENetConnection _connection = new();
    private ENetPacketPeer _peer = null!;
    private bool _closed;

    private EnetClient()
    {
    }

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<byte[]>? Received;

    /// <summary>Starts connecting, and Connected or Disconnected follows during Poll. Null if the address makes no sense.</summary>
    public static EnetClient? Connect(string address, int port)
    {
        var client = new EnetClient();
        if (client._connection.CreateHost(1) != Error.Ok)
            return null;

        client._connection.Compress(ENetConnection.CompressionMode.Zstd);
        var peer = client._connection.ConnectToHost(address, port);
        if (peer is null)
            return null;

        client._peer = peer;
        Enet.Configure(peer);
        return client;
    }

    public void Send(byte[] message)
    {
        if (_closed)
            return;

        Enet.Send(_peer, message);
        _connection.Flush();
    }

    public void Poll()
    {
        while (!_closed && Enet.Next(_connection, out var type, out var peer))
        {
            switch (type)
            {
                case ENetConnection.EventType.Connect:
                    Connected?.Invoke();
                    break;
                case ENetConnection.EventType.Disconnect:
                    Disconnected?.Invoke();
                    break;
                case ENetConnection.EventType.Receive:
                    Received?.Invoke(peer.GetPacket());
                    break;
            }
        }
    }

    public void Close()
    {
        if (_closed)
            return;

        _closed = true;
        _peer.PeerDisconnect();
        _connection.Flush();
        _connection.Destroy();
    }
}
