using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Godot;
using Valkaris.Ai;
using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Network;
using Valkaris.Core.Snapshots;

namespace Valkaris.Presentation.Dev;

/// <summary>
/// One real process of a network test, run by tools/nettest.sh: -- --net-role=host|stay|leave --net-port=N.
/// A leaver goes after a few seconds, then the others surrender, and the AI that took over wins.
/// The host prints each player's final view, and clients print the one they hold, so the script can compare them.
/// </summary>
public partial class NetTest : Node
{
    private const double LeaveAt = 3;
    private const double ClientSurrenderAt = 6;
    private const double HostSurrenderAt = 8;
    private const double LingerAfterEnd = 2;
    private const double GiveUpAfter = 60;
    private const int Players = 3;

    private GameData _data = null!;
    private string _role = "";
    private double _clock;
    private double _sinceEnd;
    private bool _surrendered;
    private bool _printed;
    private LobbyHost? _lobby;
    private HostGameSession? _host;
    private LobbyClient? _joining;
    private ClientGameSession? _client;
    private double _matchStart;
    private bool _takeoverSeen;

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        CommonCulture();
        var arguments = OS.GetCmdlineUserArgs();
        string Value(string name) => arguments.First(a => a.StartsWith(name + "=", StringComparison.Ordinal))[(name.Length + 1)..];
        _role = Value("--net-role");
        var port = int.Parse(Value("--net-port"));

        var source = new GodotDataSource();
        _data = GameData.Load(source);
        var hash = Handshake.DataHash(source);

        if (_role == "host")
        {
            var network = EnetHost.Listen(port);
            if (network is null)
            {
                Finish("cannot listen", 1);
                return;
            }
            _lobby = new LobbyHost(network, _data, hash, "Test Host", null);
            GD.Print("[nettest] ready");
        }
        else
        {
            var network = EnetClient.Connect("127.0.0.1", port)!;
            _joining = new LobbyClient(network, _data, hash, "Test " + _role, null);
            _joining.Refused += refused => Finish($"refused {refused.Reason}", 1);
            _joining.ConnectionLost += () => Finish("no connection", 1);
            _joining.MatchStarted += session =>
            {
                _client = session;
                _matchStart = _clock;
                session.ConnectionLost += OnHostLeft;
                session.EventRaised += e => GD.Print($"[nettest] event {e.GetType().Name}");
            };
        }
    }

    private static void CommonCulture() => System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_clock > GiveUpAfter)
        {
            Finish("gave up waiting", 1);
            return;
        }

        if (_lobby is not null && _host is null)
            RunLobby();
        else if (_host is not null)
            RunHost(delta);
        else if (_client is null)
            _joining?.Poll();
        else
            RunClient(delta);
    }

    private void RunLobby()
    {
        _lobby!.Poll();
        if (_lobby.State.PlayerCount < Players)
            return;

        _host = _lobby.Start(77, (session, id) => new AiPlayer(new AiView(session.State, session.Data, id, command => session.SendAs(id, command))));
        _matchStart = _clock;
    }

    private void RunHost(double delta)
    {
        var host = _host!;
        host.Tick(delta);
        if (!_takeoverSeen && host.State.Players.Values.Any(p => p.Id != host.LocalPlayerId && p.IsAi))
        {
            _takeoverSeen = true;
            GD.Print("[nettest] takeover");
        }

        if (!_surrendered && _clock - _matchStart >= HostSurrenderAt)
        {
            _surrendered = true;
            host.Send(new SurrenderCommand());
        }

        if (!host.State.IsMatchOver)
            return;

        _sinceEnd += delta;
        if (_sinceEnd < LingerAfterEnd)
            return;

        foreach (var player in host.State.Players.Keys.Where(id => id != host.LocalPlayerId))
            GD.Print($"[nettest] view {player} {Digest(PlayerView.Of(host.State, _data, player))}");
        host.Stop();
        Finish("host done", 0);
    }

    private void RunClient(double delta)
    {
        var client = _client!;
        client.Tick(delta);
        var elapsed = _clock - _matchStart;

        var own = client.State.Villages.Values.FirstOrDefault(v => v.OwnerId == client.LocalPlayerId);
        if (_role == "leave" && elapsed >= LeaveAt)
        {
            client.Leave();
            Finish("left", 0);
            return;
        }
        if (_role == "stay" && !_surrendered && elapsed >= ClientSurrenderAt)
        {
            _surrendered = true;
            client.Send(new SurrenderCommand());
        }
        if (own is not null && !_printed && elapsed < 1)
        {
            _printed = true;
            client.Send(new BuildCommand(own.Id, BuildingType.Farm));
        }
    }

    private void OnHostLeft()
    {
        var client = _client!;
        GD.Print($"[nettest] view {client.LocalPlayerId} {Digest(client.LastView)}");
        Finish(client.State.IsMatchOver ? "host left after the end" : "host left early", client.State.IsMatchOver ? 0 : 1);
    }

    private static string Digest(PlayerViewSnapshot view) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SnapshotMapper.ToJson(view.State))));

    private void Finish(string message, int code)
    {
        GD.Print($"[nettest] {message}");
        SetProcess(false);
        GetTree().Quit(code);
    }
}
