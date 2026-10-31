using Valkaris.Ai;
using Valkaris.Core.Commands;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Network;

namespace Valkaris.Core.Tests;

public class LobbyStateTests
{
    private readonly LobbyState _lobby = new(TestData.Shipped, "Oguz", "Evim");

    [Fact]
    public void TheHostHoldsTheFirstSeatAndTheRestStartOpen()
    {
        Assert.Equal(SlotKind.Host, _lobby.Slots[0].Kind);
        Assert.All(_lobby.Slots.Skip(1), s => Assert.Equal(SlotKind.Open, s.Kind));
        Assert.Equal(TestData.Shipped.Game.MaxPlayers, _lobby.Slots.Count);
    }

    [Fact]
    public void StartingNeedsTwoPlayersAndEmptySeatsDoNotCount()
    {
        Assert.False(_lobby.CanStart);
        Assert.Null(_lobby.Join(7, "Ayla", null));
        Assert.True(_lobby.CanStart);
        _lobby.Leave(7);
        Assert.False(_lobby.CanStart);
        _lobby.SetSlot(3, SlotKind.Ai, AiDifficulty.Hard);
        Assert.True(_lobby.CanStart);
    }

    [Fact]
    public void AFullLobbyRefuses()
    {
        _lobby.SetSlot(1, SlotKind.Closed);
        _lobby.SetSlot(2, SlotKind.Ai);
        Assert.Null(_lobby.Join(7, "Ayla", null));

        Assert.Equal(JoinRefusal.LobbyFull, _lobby.Join(8, "Deniz", null)!.Value.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    [InlineData("<script>")]
    public void ABadPlayerNameIsRefused(string name)
    {
        Assert.Equal(JoinRefusal.PlayerNameInvalid, _lobby.Join(7, name, null)!.Value.Reason);
    }

    [Fact]
    public void TheLongestVillageNameIsAlsoTheLimitForPlayerNames()
    {
        var limit = TestData.Shipped.Game.VillageNames.MaxLength;

        Assert.Null(_lobby.Join(7, new string('a', limit), null));
        Assert.Equal(JoinRefusal.PlayerNameInvalid, _lobby.Join(8, new string('b', limit + 1), null)!.Value.Reason);
    }

    [Theory]
    [InlineData("OGUZ")]
    [InlineData(" oguz ")]
    public void APlayerNameIsTakenWhateverItsCase(string name)
    {
        Assert.Equal(JoinRefusal.PlayerNameTaken, _lobby.Join(7, name, null)!.Value.Reason);
    }

    [Fact]
    public void VillageNamesFollowTheUsualRules()
    {
        Assert.Equal((JoinRefusal.VillageNameRefused, CommandRejection.VillageNameTaken), _lobby.Join(7, "Ayla", "EVIM")!.Value);
        Assert.Equal((JoinRefusal.VillageNameRefused, CommandRejection.VillageNameTooShort), _lobby.Join(7, "Ayla", "ab")!.Value);
        Assert.Null(_lobby.Join(7, "Ayla", null));
        Assert.Null(_lobby.Join(8, "Deniz", "Deniz Köy"));
    }

    [Fact]
    public void ChangingAnOccupiedSeatRemovesThePerson()
    {
        _lobby.Join(7, "Ayla", null);

        Assert.Null(_lobby.SetSlot(1, SlotKind.Open));
        Assert.Equal("Ayla", _lobby.Slots[1].PlayerName);
        Assert.Equal(7, _lobby.SetSlot(1, SlotKind.Closed));
        Assert.Null(_lobby.SlotOfPeer(7));
        Assert.Null(_lobby.Slots[1].PlayerName);
    }

    [Fact]
    public void TheHostsSeatCannotChange()
    {
        Assert.Throws<ArgumentException>(() => _lobby.SetSlot(0, SlotKind.Closed));
        Assert.Throws<ArgumentException>(() => _lobby.SetSlot(1, SlotKind.Host));
    }

    [Fact]
    public void OnlyOfferedSettingsAreAccepted()
    {
        Assert.True(_lobby.SetSettings(45 * 60, 2, MapSize.Medium));
        Assert.True(_lobby.SetSettings(null, 3, MapSize.Small));
        Assert.False(_lobby.SetSettings(7, 2, MapSize.Medium));
        Assert.False(_lobby.SetSettings(45 * 60, 99, MapSize.Medium));
        Assert.Equal(3, _lobby.Settings.SpeedMultiplier);
    }

    [Fact]
    public void ThePlanLeavesNoGapsAndKeepsEachPlayersOwnDifficulty()
    {
        _lobby.SetSlot(1, SlotKind.Ai, AiDifficulty.Hard);
        _lobby.SetSlot(2, SlotKind.Closed);
        _lobby.Join(7, "Ayla", "Köyüm");

        var plan = _lobby.Plan();

        Assert.Equal(3, plan.Players.Count);
        Assert.Equal(AiDifficulty.Hard, plan.Players[1].Difficulty);
        Assert.Equal("Köyüm", plan.Players[2].VillageName);
        Assert.Equal(3, plan.PlayerOfPeer[7]);
        Assert.Equal(new[] { 2 }, plan.AiPlayers);
    }

    [Fact]
    public void AnAiNeverSharesAPersonsName()
    {
        var ai = TestData.Shipped.Ai.Names[0];
        _lobby.Join(7, ai, null);
        _lobby.SetSlot(2, SlotKind.Ai);

        Assert.DoesNotContain(_lobby.Plan().Players.Where(p => p.IsAi), p => p.Name == ai);
    }
}

public class HandshakeTests
{
    private static readonly string Hash = Handshake.DataHash(new ShippedDataSource());

    private readonly MemoryNetwork _network = new();
    private readonly LobbyHost _host;

    public HandshakeTests() => _host = new LobbyHost(_network.Host, TestData.Shipped, Hash, "Oguz", null);

    private (LobbyClient Client, List<RefusedMessage> Refusals) Join(string name = "Ayla", string? version = null, string? hash = null, string? village = null)
    {
        var connection = _network.Connect();
        var client = new LobbyClient(connection, TestData.Shipped, hash ?? Hash, name, village, version ?? Handshake.Version);
        var refusals = new List<RefusedMessage>();
        client.Refused += refusals.Add;
        Pump(client);
        return (client, refusals);
    }

    private void Pump(params LobbyClient[] clients)
    {
        for (var i = 0; i < 3; i++)
        {
            foreach (var client in clients)
                client.Poll();
            _host.Poll();
        }
        foreach (var client in clients)
            client.Poll();
    }

    [Fact]
    public void TheHashChangesWithTheDataOnly()
    {
        Assert.Equal(Hash, Handshake.DataHash(new ShippedDataSource()));
        var mutated = new MutatedDataSource("map.json", root => root["sightRadius"] = 7);
        Assert.NotEqual(Hash, Handshake.DataHash(mutated));
    }

    [Fact]
    public void AMatchingClientJoinsAndEveryoneSeesIt()
    {
        var (first, _) = Join();
        var (second, _) = Join("Deniz");
        Pump(first, second);

        Assert.Equal("Deniz", first.Lobby!.Slots[2].PlayerName);
        Assert.Equal("Ayla", second.Lobby!.Slots[1].PlayerName);
    }

    [Fact]
    public void ADifferentVersionIsRefused()
    {
        Assert.Equal(JoinRefusal.VersionMismatch, Join(version: "0.0.1").Refusals.Single().Reason);
    }

    [Fact]
    public void DifferentDataIsRefused()
    {
        Assert.Equal(JoinRefusal.DataMismatch, Join(hash: "ABC").Refusals.Single().Reason);
    }

    [Fact]
    public void ARefusedVillageNameSaysWhy()
    {
        var refusal = Join(village: "ab").Refusals.Single();

        Assert.Equal((JoinRefusal.VillageNameRefused, CommandRejection.VillageNameTooShort), (refusal.Reason, refusal.Village));
    }

    [Fact]
    public void TheHostSeesLeaversAndCanRemovePlayers()
    {
        var (first, firstRefusals) = Join();
        var (second, _) = Join("Deniz");
        _host.SetSlot(1, SlotKind.Closed);
        Pump(first, second);

        Assert.Equal(JoinRefusal.RemovedByHost, firstRefusals.Single().Reason);
        Assert.Equal(SlotKind.Closed, second.Lobby!.Slots[1].Kind);

        second.Leave();
        Pump();
        Assert.Null(_host.State.Slots[2].PlayerName);
    }

    [Fact]
    public void SettingsReachEveryone()
    {
        var (client, _) = Join();
        _host.SetSettings(45 * 60, 2, MapSize.Medium);
        Pump(client);

        Assert.Equal(MapSize.Medium, client.Lobby!.Settings.MapSize);
    }

    [Fact]
    public void TheMatchStartsForEveryoneWithSameMapAndJoiningThenIsRefused()
    {
        var (client, _) = Join();
        ClientGameSession? session = null;
        client.MatchStarted += started => session = started;
        _host.SetSlot(2, SlotKind.Ai, AiDifficulty.Easy);

        var host = _host.Start(99, (local, id) => new AiPlayer(new AiView(local.State, local.Data, id, c => local.SendAs(id, c))));
        Pump(client);

        Assert.NotNull(session);
        Assert.Equal(2, session!.LocalPlayerId);
        Assert.Equal(host.State.Map.Tiles.Select(t => t.Terrain), session.State.Map.Tiles.Select(t => t.Terrain));
        Assert.Equal(3, host.State.Players.Count);

        var late = new LobbyClient(_network.Connect(), TestData.Shipped, Hash, "Geç Kalan", null);
        var refusals = new List<RefusedMessage>();
        late.Refused += refusals.Add;
        late.Poll();
        host.Tick(0.1);
        late.Poll();
        Assert.Equal(JoinRefusal.MatchInProgress, refusals.Single().Reason);
    }
}
