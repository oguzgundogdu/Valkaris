using System.Text.Json;
using Valkaris.Core.Commands;
using Valkaris.Core.Events;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Network;
using Valkaris.Core.Snapshots;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class MultiplayerSessionTests
{
    private const int Host = 1;
    private const int Guest = 2;

    private readonly MemoryNetwork _network = new();
    private readonly HostGameSession _host;
    private readonly ClientGameSession _client;
    private readonly Dictionary<int, PlayerViewSnapshot> _lastSent = new();

    public MultiplayerSessionTests()
    {
        var state = TestMatch.Create(players: 2);
        _host = new HostGameSession(new LocalGameSession(state, TestData.Shipped, Host), _network.Host);
        _host.ViewSent += (player, view) => _lastSent[player] = view;
        var connection = _network.Connect();
        MatchStartMessage? start = null;
        void Listen(byte[] bytes) => start = (MatchStartMessage)WireCodec.DecodeFromHost(bytes)!;
        connection.Received += Listen;
        _host.AddClient(connection.Peer, Guest);
        connection.Poll();
        connection.Received -= Listen;
        _client = new ClientGameSession(connection, TestData.Shipped, start!);
    }

    private int GuestVillage => _client.State.Villages.Values.First(v => v.OwnerId == Guest).Id;

    private void Step(double seconds = 0.1)
    {
        _host.Tick(seconds);
        _client.Tick(0);
    }

    private static string Json(PlayerViewSnapshot view) => JsonSerializer.Serialize(view, SnapshotMapper.JsonOptions);

    [Fact]
    public void NeitherSessionCanPause()
    {
        Assert.False(_host.CanPause || _client.CanPause);
        Assert.Throws<InvalidOperationException>(() => _host.SetPaused(true));
        Assert.Throws<InvalidOperationException>(() => _client.SetPaused(true));
    }

    [Fact]
    public void TheClientStaysInStepWithTheHost()
    {
        _client.Send(new BuildCommand(GuestVillage, BuildingType.Farm));
        for (var i = 0; i < 2000; i++)
        {
            Step(0.25);
            Assert.Equal(Json(_lastSent[Guest]), Json(_client.LastView));
        }

        var level = _host.State.Villages[GuestVillage].BuildingLevel(BuildingType.Farm);
        Assert.True(level > 1);
        Assert.Equal(level, _client.State.Villages[GuestVillage].BuildingLevel(BuildingType.Farm));
    }

    [Fact]
    public void ACommandActsForTheConnectionsPlayerOnly()
    {
        var hostVillage = _host.State.Villages.Values.First(v => v.OwnerId == Host).Id;
        var rejections = new List<CommandRejection>();
        _client.CommandRejected += rejections.Add;

        _client.Send(new BuildCommand(hostVillage, BuildingType.Farm));
        Step();
        Step();

        Assert.Empty(_host.State.Villages[hostVillage].BuildQueue);
        Assert.Equal(new[] { CommandRejection.NotOwner }, rejections);
    }

    [Fact]
    public void ABadMessageIsDroppedAndTheMatchGoesOn()
    {
        var connection = _network.Connect();
        _host.AddClient(connection.Peer, Guest);
        connection.Send(new byte[] { 1, 2, 3 });
        connection.Send(WireCodec.Encode(new RejectionMessage(CommandRejection.NotOwner)));
        Step();

        Assert.True(_host.State.ElapsedTime > 0);
    }

    [Fact]
    public void AStateUpdateComesBeforeTheEventsThatNeedIt()
    {
        var found = 0;
        _client.EventRaised += e =>
        {
            if (e is BuildingCompleted completed && _client.State.Villages[completed.VillageId].BuildingLevel(completed.Building) == completed.NewLevel)
                found++;
        };

        _client.Send(new BuildCommand(GuestVillage, BuildingType.Farm));
        Step();
        Step(BuildingRules.BuildTimeSeconds(TestData.Shipped, BuildingType.Farm, 2) + 1);

        Assert.Equal(1, found);
    }

    [Fact]
    public void AClientHearsOnlyItsOwnEvents()
    {
        var state = _host.State;
        var hostVillage = state.Villages.Values.First(v => v.OwnerId == Host).Id;

        Assert.True(PlayerView.Hears(state, new BuildingCompleted(0, GuestVillage, BuildingType.Farm, 2), Guest));
        Assert.False(PlayerView.Hears(state, new BuildingCompleted(0, hostVillage, BuildingType.Farm, 2), Guest));
        Assert.False(PlayerView.Hears(state, new BattleResolved(0, 12345), Guest));
        Assert.True(PlayerView.Hears(state, new MatchEnded(0), Guest));
    }

    [Fact]
    public void AnEventTriggersAnUpdateAtOnce()
    {
        _client.Send(new BuildCommand(GuestVillage, BuildingType.Farm));
        Step();
        var heard = false;
        _client.EventRaised += e => heard |= e is BuildingCompleted;
        var time = BuildingRules.BuildTimeSeconds(TestData.Shipped, BuildingType.Farm, 2);

        while (!heard && _host.State.ElapsedTime < time + 1)
            Step(0.05);

        Assert.InRange(_host.State.ElapsedTime, time - 0.1, time + 0.2);
    }

    [Fact]
    public void TheClockRunsBetweenViewsAndStopsWithoutNews()
    {
        var before = _client.State.ElapsedTime;
        _client.Tick(1);
        Assert.Equal(before + 1, _client.State.ElapsedTime, 6);

        _client.Tick(100);
        Assert.InRange(_client.State.ElapsedTime, before, before + 5);
    }

    [Fact]
    public void ThePreviewsWorkOnAFilteredState()
    {
        var state = _client.State;
        var own = state.Villages[GuestVillage];
        var other = state.Villages.Values.First(v => v.OwnerId == Host);
        var free = state.Map.Tiles.Select((t, i) => (t, i)).First(p => p.t.VillageId is null && p.t.Terrain != Terrain.Water).i;
        var tile = new TileCoord(free % state.Map.Width, free / state.Map.Width);

        var send = new SendTroopsCommand(own.Id, other.Id, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Spearman] = 1 });
        foreach (var seen in new[] { state, _host.State })
        {
            Assert.Equal(BuildPreview.Evaluate(_host.State, TestData.Shipped, Guest, own.Id, BuildingType.Farm).Rejection, BuildPreview.Evaluate(seen, TestData.Shipped, Guest, own.Id, BuildingType.Farm).Rejection);
            Assert.Equal(TrainPreview.Evaluate(_host.State, TestData.Shipped, Guest, own.Id, UnitType.Spearman, 1).Rejection, TrainPreview.Evaluate(seen, TestData.Shipped, Guest, own.Id, UnitType.Spearman, 1).Rejection);
            Assert.Equal(SendPreview.Evaluate(_host.State, TestData.Shipped, Guest, send).Rejection, SendPreview.Evaluate(seen, TestData.Shipped, Guest, send).Rejection);
            Assert.Equal(FoundPreview.Evaluate(_host.State, TestData.Shipped, Guest, own.Id, tile).Rejection, FoundPreview.Evaluate(seen, TestData.Shipped, Guest, own.Id, tile).Rejection);
            Assert.Equal(SendResourcesPreview.Evaluate(_host.State, TestData.Shipped, Guest, new SendResourcesCommand(own.Id, other.Id, 1, 0, 0)).Rejection, SendResourcesPreview.Evaluate(seen, TestData.Shipped, Guest, new SendResourcesCommand(own.Id, other.Id, 1, 0, 0)).Rejection);
        }
    }
}
