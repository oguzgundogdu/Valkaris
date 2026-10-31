namespace Valkaris.Core.Network;

/// <summary>The host's end of the connection: reliable, ordered bytes to and from numbered peers. Events fire only during Poll.</summary>
public interface INetworkHost
{
    event Action<int>? PeerConnected;
    event Action<int>? PeerDisconnected;
    event Action<int, byte[]>? Received;

    void Send(int peer, byte[] message);
    void Disconnect(int peer);
    void Poll();
    void Stop();
}

/// <summary>A client's end of the connection. Events fire only during Poll.</summary>
public interface INetworkClient
{
    event Action? Connected;
    event Action? Disconnected;
    event Action<byte[]>? Received;

    void Send(byte[] message);
    void Poll();
    void Close();
}
