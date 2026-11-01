using System.Text.Json;
using Valkaris.Ai;
using Valkaris.Core.Commands;
using Valkaris.Core.Events;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Network;
using Valkaris.Core.Snapshots;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

/// <summary>A whole match over a delayed network: a host, three clients, and someone leaving halfway.</summary>
public class FullMatchTests
{
    private static string Json(PlayerViewSnapshot view) => JsonSerializer.Serialize(view, SnapshotMapper.JsonOptions);

    /// <summary>Plays as a person would, and only from what the client knows.</summary>
    private sealed class ClientBot
    {
        private readonly ClientGameSession _session;
        private readonly Random _random;
        private double _next;

        public ClientBot(ClientGameSession session, int seed)
        {
            _session = session;
            _random = new Random(seed);
        }

        public int Sent { get; private set; }

        public void Act()
        {
            var state = _session.State;
            if (state.ElapsedTime < _next || state.IsMatchOver || state.Players[_session.LocalPlayerId].IsEliminated)
                return;
            _next = state.ElapsedTime + 3;

            var me = _session.LocalPlayerId;
            var own = state.Villages.Values.Where(v => v.OwnerId == me).ToList();
            if (own.Count == 0)
                return;
            var village = own[_random.Next(own.Count)];

            if (state.Players[me].CapitalVillageId is null)
                Issue(new SetCapitalCommand(village.Id));

            var building = Enum.GetValues<BuildingType>()[_random.Next(Enum.GetValues<BuildingType>().Length)];
            if (BuildPreview.Evaluate(state, _session.Data, me, village.Id, building).Rejection is null)
                Issue(new BuildCommand(village.Id, building));
            if (TrainPreview.Evaluate(state, _session.Data, me, village.Id, UnitType.Spearman, 3).Rejection is null)
                Issue(new TrainCommand(village.Id, UnitType.Spearman, 3));

            var spears = village.TroopCount(UnitType.Spearman);
            if (spears >= 8)
            {
                var targets = state.Villages.Values.Where(v => v.OwnerId != me).ToList();
                var type = _random.Next(2) == 0 ? MovementType.Attack : MovementType.Raid;
                Issue(new SendTroopsCommand(village.Id, targets[_random.Next(targets.Count)].Id, type, new Dictionary<UnitType, int> { [UnitType.Spearman] = spears }));
            }

            // Sometimes a move the host must refuse, as a stale screen would send.
            if (_random.Next(10) == 0)
                Issue(new BuildCommand(state.Villages.Values.First(v => v.OwnerId != me).Id, BuildingType.Farm));
        }

        private void Issue(ICommand command)
        {
            Sent++;
            _session.Send(command);
        }
    }

    [Theory]
    [InlineData(21, 0.15, false)]
    [InlineData(22, 0.6, false)]
    [InlineData(23, 0.3, true)]
    public void EveryClientMatchesItsViewOfTheHostThroughAWholeMatch(int seed, double delay, bool spectators)
    {
        var network = new MemoryNetwork(seed, delay);
        var hash = Handshake.DataHash(new ShippedDataSource());
        var lobby = new LobbyHost(network.Host, TestData.Shipped, hash, "Oguz", null);
        lobby.SetSettings(30 * 60, 3, MapSize.Medium);
        lobby.SetSpectatorsSeeAll(spectators);

        var joining = new[] { "Ayla", "Deniz", "Mert" }.Select(name => new LobbyClient(network.Connect(), TestData.Shipped, hash, name, null)).ToList();
        var clients = new List<ClientGameSession>();
        foreach (var client in joining)
            client.MatchStarted += clients.Add;
        network.Advance(delay + 1);
        foreach (var client in joining)
            client.Poll();
        network.Advance(delay + 1);
        lobby.Poll();
        network.Advance(delay + 1);
        foreach (var client in joining)
            client.Poll();

        var host = lobby.Start((ulong)seed, (session, id) => new AiPlayer(new AiView(session.State, session.Data, id, c => session.SendAs(id, c))));
        var sent = new Dictionary<int, Queue<string>>();
        host.ViewSent += (player, view) =>
        {
            if (!sent.TryGetValue(player, out var queue))
                sent[player] = queue = new Queue<string>();
            queue.Enqueue(Json(view));
        };

        network.Advance(delay + 1);
        foreach (var client in joining)
            client.Poll();
        Assert.Equal(3, clients.Count);

        var compared = 0;
        var reportsHeard = 0;
        foreach (var client in clients)
        {
            var player = client.LocalPlayerId;
            client.ViewApplied += view =>
            {
                Assert.Equal(sent[player].Dequeue(), Json(view));
                compared++;
            };
            client.EventRaised += gameEvent =>
            {
                // Whatever an event names must already be there, or the screens would fail on it.
                if (gameEvent is BattleResolved battle)
                {
                    Assert.Contains(client.State.Reports, r => r.Id == battle.ReportId);
                    reportsHeard++;
                }
                if (gameEvent is ScoutResolved scout)
                    Assert.Contains(client.State.ScoutReports, r => r.Id == scout.ReportId);
                if (gameEvent is ScoutNoticed notice)
                    Assert.Contains(client.State.ScoutNotices, n => n.Id == notice.NoticeId);
            };
        }

        var bots = clients.Select((c, i) => new ClientBot(c, seed * 10 + i)).ToList();
        var leaver = clients[2];
        var left = false;
        var surrendered = false;
        const double step = 0.25;
        var hostLocal = new ClientlessHost(host, seed);

        while (!host.State.IsMatchOver)
        {
            host.Tick(step);
            hostLocal.Act();
            network.Advance(step);

            foreach (var client in clients)
            {
                if (ReferenceEquals(client, leaver) && left)
                    continue;
                client.Tick(step);
            }
            foreach (var (bot, client) in bots.Zip(clients))
            {
                if (!(ReferenceEquals(client, leaver) && left))
                    bot.Act();
            }

            // Someone giving up mid-match becomes a spectator, and older reports then appear for them.
            if (spectators && !surrendered && host.State.ElapsedTime > 10 * 60)
            {
                surrendered = true;
                clients[0].Send(new SurrenderCommand());
            }

            if (!left && host.State.ElapsedTime > 15 * 60)
            {
                leaver.Leave();
                left = true;
            }
        }

        // Let what's still on its way arrive.
        for (var i = 0; i < 10; i++)
        {
            network.Advance(delay + step);
            foreach (var client in clients.Where(c => !ReferenceEquals(c, leaver)))
                client.Tick(0);
        }

        Assert.True(compared > 1000, $"Only {compared} views compared.");
        Assert.True(reportsHeard > 0, "The match should have had battles.");
        Assert.All(bots, bot => Assert.True(bot.Sent > 0));
        Assert.True(host.State.Players[leaver.LocalPlayerId].IsAi);

        var outcome = JsonSerializer.Serialize(host.State.Outcome, SnapshotMapper.JsonOptions);
        foreach (var client in clients.Where(c => !ReferenceEquals(c, leaver)))
            Assert.Equal(outcome, JsonSerializer.Serialize(client.State.Outcome, SnapshotMapper.JsonOptions));
    }

    /// <summary>The host plays too, through the same session a window would use.</summary>
    private sealed class ClientlessHost
    {
        private readonly HostGameSession _host;
        private readonly Random _random;
        private double _next;

        public ClientlessHost(HostGameSession host, int seed)
        {
            _host = host;
            _random = new Random(seed);
        }

        public void Act()
        {
            if (_host.State.ElapsedTime < _next || _host.State.IsMatchOver)
                return;
            _next = _host.State.ElapsedTime + 5;

            var village = _host.State.Villages.Values.FirstOrDefault(v => v.OwnerId == _host.LocalPlayerId);
            if (village is not null)
                _host.Send(new BuildCommand(village.Id, Enum.GetValues<BuildingType>()[_random.Next(Enum.GetValues<BuildingType>().Length)]));
        }
    }
}
