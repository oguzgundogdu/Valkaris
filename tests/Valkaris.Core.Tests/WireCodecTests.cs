using System.Text;
using Valkaris.Core.Commands;
using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Network;
using Valkaris.Core.Snapshots;

namespace Valkaris.Core.Tests;

public class WireCodecTests
{
    private static readonly ICommand[] SampleCommands =
    {
        new BuildCommand(1, BuildingType.Farm),
        new TrainCommand(1, UnitType.Scout, 3),
        new SendTroopsCommand(1, 2, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Cavalry] = 4 }, BuildingType.Wall),
        new FoundVillageCommand(1, new TileCoord(3, 4)),
        new SetCapitalCommand(2),
        new SurrenderCommand(),
        new SendResourcesCommand(1, 2, 10, 20, 30),
        new RenameVillageCommand(1, "Şirin Köy"),
    };

    private static readonly GameEvent[] SampleEvents =
    {
        new BuildingCompleted(1.5, 1, BuildingType.Farm, 2),
        new TroopsTrained(1, 1, UnitType.Scout, 2),
        new TroopsReturned(1, 1, new Resources(1, 2, 3)),
        new ReinforcementsArrived(1, 1),
        new BattleResolved(1, 7),
        new ScoutResolved(1, 7),
        new ScoutNoticed(1, 7),
        new VillageCaptured(1, 1, 2, 3),
        new CapitalChoiceRequired(1, 1, 60),
        new CapitalChanged(1, 1, 2),
        new PlayerEliminated(1, 1),
        new PlayerSurrendered(1, 1),
        new MatchEnded(1),
        new ResourcesDelivered(1, 1, new Resources(1, 0, 0), Resources.Zero),
        new CarriersReturned(1, 1, new Resources(1, 0, 0), Resources.Zero),
        new VillageFounded(1, 3),
        new SettlingFailed(1, 1, CommandRejection.TileTaken),
    };

    private static IEnumerable<Type> Concrete<T>() => typeof(T).Assembly.GetTypes().Where(t => typeof(T).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void EveryCommandEventAndMessageIsRegistered()
    {
        Assert.Equal(Concrete<ICommand>().ToHashSet(), WireCodec.Commands.ToHashSet());
        Assert.Equal(Concrete<GameEvent>().ToHashSet(), WireCodec.Events.ToHashSet());
        Assert.Equal(Concrete<INetMessage>().ToHashSet(), WireCodec.Messages.ToHashSet());
    }

    [Fact]
    public void TheSamplesCoverEveryType()
    {
        Assert.Equal(WireCodec.Commands.ToHashSet(), SampleCommands.Select(c => c.GetType()).ToHashSet());
        Assert.Equal(WireCodec.Events.ToHashSet(), SampleEvents.Select(e => e.GetType()).ToHashSet());
    }

    [Fact]
    public void EveryCommandRoundTrips()
    {
        foreach (var command in SampleCommands)
        {
            var bytes = WireCodec.Encode(new CommandMessage(command));
            var back = Assert.IsType<CommandMessage>(WireCodec.DecodeFromClient(bytes));

            Assert.IsType(command.GetType(), back.Command);
            Assert.Equal(bytes, WireCodec.Encode(back));
        }
    }

    [Fact]
    public void AViewAndEveryEventRoundTrip()
    {
        var view = PlayerView.Of(TestMatch.Create(players: 3), TestData.Shipped, 1);
        var bytes = WireCodec.Encode(new UpdateMessage(view, SampleEvents));
        var back = Assert.IsType<UpdateMessage>(WireCodec.DecodeFromHost(bytes));

        Assert.Equal(SampleEvents, back.Events);
        Assert.Equal(bytes, WireCodec.Encode(back));
    }

    [Fact]
    public void AnUnregisteredTypeCannotBeSent()
    {
        Assert.Throws<InvalidOperationException>(() => WireCodec.Encode(new CommandMessage(new StrayCommand())));
    }

    private sealed record StrayCommand : ICommand;

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("[\"Nothing\",{}]")]
    [InlineData("[\"CommandMessage\"]")]
    [InlineData("[\"CommandMessage\",null]")]
    [InlineData("[\"CommandMessage\",{}]")]
    [InlineData("[\"CommandMessage\",{\"command\":null}]")]
    [InlineData("[\"CommandMessage\",{\"command\":[\"GameState\",{}]}]")]
    [InlineData("[\"CommandMessage\",{\"command\":[\"System.IO.File\",{}]}]")]
    [InlineData("[\"CommandMessage\",{\"command\":[\"SendTroopsCommand\",{}]}]")]
    [InlineData("[\"CommandMessage\",{\"command\":[\"RenameVillageCommand\",{\"villageId\":1}]}]")]
    [InlineData("[\"CommandMessage\",{\"command\":[\"BuildCommand\",{\"villageId\":1,\"building\":\"Palace\"}]}]")]
    [InlineData("[\"CommandMessage\",{\"command\":[\"BuildCommand\",{\"villageId\":\"x\"}]}]")]
    [InlineData("[\"CommandMessage\",{\"command\":[\"BuildCommand\",{}]},1]")]
    [InlineData("[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[")]
    public void BadInputIsRefused(string text)
    {
        Assert.Null(WireCodec.DecodeFromClient(Bytes(text)));
    }

    [Fact]
    public void AClientMayNotSendAnOversizedMessage()
    {
        var name = new string('x', WireCodec.MaxFromClientBytes);

        Assert.Null(WireCodec.DecodeFromClient(WireCodec.Encode(new CommandMessage(new RenameVillageCommand(1, name)))));
    }

    [Fact]
    public void AnyDamagedMessageIsHarmless()
    {
        var random = new Random(3);
        var state = TestMatch.Create(players: 2);
        var handler = new CommandHandler(state, TestData.Shipped);
        foreach (var command in SampleCommands.Where(c => c is not SurrenderCommand))
        {
            var good = WireCodec.Encode(new CommandMessage(command));
            for (var i = 0; i < 300; i++)
            {
                var damaged = (byte[])good.Clone();
                damaged[random.Next(damaged.Length)] = (byte)random.Next(32, 127);

                if (WireCodec.DecodeFromClient(damaged) is CommandMessage { Command: not SurrenderCommand } decoded)
                    handler.Handle(1, decoded.Command);
            }
        }
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    public void ExtremeCountsAreRefusedByTheRules(int count)
    {
        var state = TestMatch.Create(players: 2);
        var handler = new CommandHandler(state, TestData.Shipped);
        var other = state.Villages.Values.First(v => v.OwnerId == 2).Id;
        var before = SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(state));

        Assert.False(handler.Handle(1, new TrainCommand(1, UnitType.Spearman, count)).Success);
        Assert.False(handler.Handle(1, new SendTroopsCommand(1, other, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Spearman] = count })).Success);
        Assert.False(handler.Handle(1, new SendResourcesCommand(1, other, count, count, count)).Success);
        Assert.Equal(before, SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(state)));
    }
}

public class MemoryNetworkTests
{
    private static byte[] One(byte value) => new[] { value };

    [Fact]
    public void MessagesArriveInOrderDespiteDelay()
    {
        var network = new MemoryNetwork(delaySeed: 1, maxDelaySeconds: 1);
        var client = network.Connect();
        var got = new List<byte>();
        client.Received += bytes => got.Add(bytes[0]);

        for (byte i = 0; i < 50; i++)
        {
            network.Host.Send(client.Peer, One(i));
            network.Advance(0.01);
        }
        client.Poll();
        Assert.True(got.Count < 50);

        network.Advance(2);
        client.Poll();
        Assert.Equal(Enumerable.Range(0, 50).Select(i => (byte)i), got);
    }

    [Fact]
    public void BothEndsHearOfADisconnect()
    {
        var network = new MemoryNetwork();
        var (first, second) = (network.Connect(), network.Connect());
        var gone = new List<int>();
        var told = false;
        network.Host.PeerDisconnected += gone.Add;
        second.Disconnected += () => told = true;

        first.Close();
        network.Host.Disconnect(second.Peer);
        network.Host.Poll();
        second.Poll();

        Assert.Equal(new[] { first.Peer, second.Peer }, gone);
        Assert.True(told);
    }
}
