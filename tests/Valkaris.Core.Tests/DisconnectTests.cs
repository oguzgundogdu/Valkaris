using Valkaris.Ai;
using Valkaris.Core.Commands;
using Valkaris.Core.Events;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Network;

namespace Valkaris.Core.Tests;

public class DisconnectTests
{
    private const int FirstGuest = 2;
    private const int SecondGuest = 3;

    private readonly MemoryNetwork _network = new();
    private readonly HostGameSession _host;
    private readonly List<ClientGameSession> _clients = new();

    public DisconnectTests()
    {
        var hash = Handshake.DataHash(new ShippedDataSource());
        var lobby = new LobbyHost(_network.Host, TestData.Shipped, hash, "Oguz", null);
        lobby.SetSettings(30 * 60, 3, MapSize.Small);
        var joining = new[] { "Ayla", "Deniz" }.Select(name => new LobbyClient(_network.Connect(), TestData.Shipped, hash, name, null)).ToList();
        foreach (var client in joining)
        {
            client.MatchStarted += _clients.Add;
            client.Poll();
        }
        lobby.Poll();
        _host = lobby.Start(11, (session, id) => new AiPlayer(new AiView(session.State, session.Data, id, c => session.SendAs(id, c))));
        foreach (var client in joining)
            client.Poll();
    }

    private void Step(double seconds = 0.5)
    {
        _host.Tick(seconds);
        foreach (var client in _clients)
            client.Tick(0);
    }

    [Fact]
    public void AnAiTakesOverAndEveryoneElseHears()
    {
        var heard = new List<GameEvent>();
        _clients[1].EventRaised += heard.Add;
        _clients[0].Leave();
        Step();
        Step();

        var player = _host.State.Players[FirstGuest];
        Assert.True(player.IsAi);
        Assert.Equal(AiDifficulty.Normal, player.Difficulty);
        Assert.Equal(FirstGuest, heard.OfType<PlayerDisconnected>().Single().PlayerId);
    }

    [Fact]
    public void TheTakeoverPlaysOnFromMidMatchUntilTheMatchEnds()
    {
        var village = _host.State.Villages.Values.First(v => v.OwnerId == FirstGuest);
        for (var i = 0; i < 240; i++)
            Step();
        var levels = village.Buildings.Values.Sum();

        _clients[0].Leave();
        while (!_host.State.IsMatchOver)
            Step(5);

        Assert.True(village.Buildings.Values.Sum() > levels || village.OwnerId != FirstGuest, "The AI should have built something.");
        Assert.NotNull(_clients[1].State.Outcome);
        Assert.Equal(_host.State.Outcome!.WinnerIds, _clients[1].State.Outcome!.WinnerIds);
    }

    [Fact]
    public void SomeoneAlreadyOutGetsNoAi()
    {
        _clients[0].Send(new SurrenderCommand());
        Step();
        _clients[0].Leave();
        Step();
        Step();

        Assert.False(_host.State.Players[FirstGuest].IsAi);
        Assert.False(_host.State.IsMatchOver);
    }

    [Fact]
    public void ABadSenderDisconnectingTwiceChangesNothing()
    {
        _clients[0].Leave();
        _clients[0].Leave();
        Step();
        Step();

        Assert.True(_host.State.Players[FirstGuest].IsAi);
        Assert.False(_host.State.Players[SecondGuest].IsAi);
    }

    [Fact]
    public void TheHostLeavingTellsEveryClient()
    {
        var lost = 0;
        foreach (var client in _clients)
            client.ConnectionLost += () => lost++;

        _host.Stop();
        Step();

        Assert.Equal(2, lost);
        Assert.All(_clients, c => Assert.True(c.HostLost));
    }

    [Fact]
    public void AfterTheHostLeavesTheClientsClockStops()
    {
        _host.Stop();
        Step();
        var time = _clients[0].State.ElapsedTime;
        for (var i = 0; i < 50; i++)
            _clients[0].Tick(1);

        Assert.InRange(_clients[0].State.ElapsedTime, time, time + 5);
    }
}
