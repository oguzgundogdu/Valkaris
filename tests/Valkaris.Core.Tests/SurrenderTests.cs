using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class SurrenderTests
{
    private const int Other = 1;
    private const int Quitter = 2;
    private const int Third = 3;

    private static readonly GameData Data = TestData.Shipped;

    private readonly GameState _state = TestMatch.Create(players: 3, duration: 36000);
    private readonly LocalGameSession _session;
    private readonly List<GameEvent> _events = new();
    private readonly Village _village;

    public SurrenderTests()
    {
        _session = new LocalGameSession(_state, Data, Other);
        _session.EventRaised += _events.Add;
        _village = _state.Villages[Quitter];
    }

    private CommandResult Surrender(int player = Quitter) => _session.SendAs(player, new SurrenderCommand());

    [Fact]
    public void BuildingsTroopsAndResourcesStay()
    {
        _village.SetBuildingLevel(BuildingType.Farm, 4);
        _village.AddTroops(UnitType.Swordsman, 7);
        _village.Resources = new Resources(123, 456, 78);
        var troops = _village.Troops.ToDictionary(t => t.Key, t => t.Value);

        Assert.True(Surrender().Success);

        Assert.Equal(Village.BarbarianOwnerId, _village.OwnerId);
        Assert.Equal(4, _village.BuildingLevel(BuildingType.Farm));
        Assert.Equal(troops, _village.Troops.ToDictionary(t => t.Key, t => t.Value));
        Assert.Equal(new Resources(123, 456, 78), _village.Resources);
    }

    [Fact]
    public void LoyaltyResetsQueuesClearAndNoTierIsSet()
    {
        _village.Loyalty = 30;
        _village.Resources = new Resources(800, 800, 800);
        Assert.True(_session.SendAs(Quitter, new BuildCommand(Quitter, BuildingType.Farm)).Success);

        Surrender();

        Assert.Equal(Village.MaxLoyalty, _village.Loyalty);
        Assert.Empty(_village.BuildQueue);
        Assert.Null(_village.BarbarianTier);
        Assert.True(_village.IsAbandoned);
    }

    [Fact]
    public void EveryVillageOfThePlayerGoes()
    {
        var camp = _state.Villages.Values.First(v => v.IsBarbarian);
        camp.OwnerId = Quitter;
        camp.BarbarianTier = null;

        Surrender();

        Assert.True(camp.IsAbandoned);
        Assert.DoesNotContain(_state.Villages.Values, v => v.OwnerId == Quitter);
    }

    [Fact]
    public void ThePlayersMovementsAreRemovedAndOthersStay()
    {
        _village.AddTroops(UnitType.Cavalry, 3);
        _state.Villages[Other].AddTroops(UnitType.Cavalry, 3);
        var cavalry = new Dictionary<UnitType, int> { [UnitType.Cavalry] = 1 };
        Assert.True(_session.SendAs(Quitter, new SendTroopsCommand(Quitter, Other, MovementType.Attack, cavalry)).Success);
        Assert.True(_session.SendAs(Other, new SendTroopsCommand(Other, Quitter, MovementType.Attack, cavalry)).Success);

        Surrender();

        var left = Assert.Single(_state.Movements);
        Assert.Equal(Other, left.OwnerId);
    }

    [Fact]
    public void AnAttackAlreadyOnTheWayFightsTheAbandonedVillage()
    {
        _state.Villages[Other].AddTroops(UnitType.Cavalry, 100);
        Assert.True(_session.SendAs(Other, new SendTroopsCommand(Other, Quitter, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Cavalry] = 100 })).Success);
        Surrender();

        _session.Tick(_state.Movements.Single().ArrivalTime);

        var report = _state.Reports.Single();
        Assert.Equal(Village.BarbarianOwnerId, report.DefenderId);
        Assert.True(report.AttackerWon);
    }

    [Fact]
    public void AnAbandonedVillageDoesNotRegenerateOrProduce()
    {
        _village.Resources = new Resources(10, 10, 10);
        foreach (var (unit, count) in _village.Troops.ToList())
            _village.RemoveTroops(unit, count);
        Surrender();

        _session.Tick(5000);

        Assert.Equal(new Resources(10, 10, 10), _village.Resources);
        Assert.Empty(_village.Troops.Where(t => t.Value > 0));
    }

    [Fact]
    public void BeatingItsGarrisonDoesNotStartRegrowth()
    {
        _state.Villages[Other].AddTroops(UnitType.Cavalry, 100);
        Surrender();
        Assert.True(_session.SendAs(Other, new SendTroopsCommand(Other, Quitter, MovementType.Raid, new Dictionary<UnitType, int> { [UnitType.Cavalry] = 100 })).Success);

        _session.Tick(_state.Movements.Single().ArrivalTime);

        Assert.Null(_village.NextTroopRegenTime);
    }

    [Fact]
    public void ItCanBeScouted()
    {
        _state.Villages[Other].AddTroops(UnitType.Cavalry, 100);
        Surrender();
        Assert.True(_session.SendAs(Other, new SendTroopsCommand(Other, Quitter, MovementType.Scout, new Dictionary<UnitType, int> { [UnitType.Cavalry] = 100 })).Success);

        _session.Tick(_state.Movements.Single().ArrivalTime);

        var report = _state.ScoutReports.Single();
        Assert.True(report.Succeeded);
        Assert.Equal(Village.BarbarianOwnerId, report.DefenderId);
    }

    [Fact]
    public void CapturingItCountsAsACapture()
    {
        var home = _state.Villages[Other];
        home.AddTroops(UnitType.Cavalry, 200);
        home.AddTroops(UnitType.Lord, 1);
        Surrender();
        _village.Loyalty = 1;
        Assert.True(_session.SendAs(Other, new SendTroopsCommand(Other, Quitter, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Cavalry] = 200, [UnitType.Lord] = 1 })).Success);

        _session.Tick(_state.Movements.Single().ArrivalTime);

        Assert.Equal(Other, _village.OwnerId);
        Assert.Contains(_village.Id, _state.Players[Other].CapturedVillageIds);
        Assert.False(_village.IsAbandoned);
    }

    [Fact]
    public void ThePlayerIsOutAndTheMatchGoesOnWithTwoLeft()
    {
        Surrender();

        var player = _state.Players[Quitter];
        Assert.True(player.IsEliminated);
        Assert.True(player.HasSurrendered);
        Assert.Null(player.CapitalVillageId);
        Assert.False(_state.IsMatchOver);
        Assert.Contains(_events, e => e is PlayerSurrendered { PlayerId: Quitter });
        Assert.DoesNotContain(_events, e => e is PlayerEliminated);
    }

    [Fact]
    public void WithOnePlayerLeftTheMatchEndsAtOnce()
    {
        Surrender();
        _events.Clear();

        Surrender(Third);

        Assert.Equal(MatchEndReason.LastPlayerStanding, _state.Outcome!.Reason);
        Assert.Equal(new[] { Other }, _state.Outcome.WinnerIds);
        Assert.Contains(_events, e => e is MatchEnded);
    }

    [Fact]
    public void ALostCapitalChoiceEndsWithTheSurrender()
    {
        _state.Players[Quitter].CapitalChoiceDeadline = 500;

        Surrender();
        _session.Tick(1000);

        Assert.Null(_state.Players[Quitter].CapitalChoiceDeadline);
        Assert.False(_state.IsMatchOver);
    }

    [Fact]
    public void AnUnlimitedMatchEndsTheSameWay()
    {
        var state = GameFactory.CreateMatch(new GameSettings(null, 1, MapSize.Small, 5), TestMatch.Slots(2), Data);
        var session = new LocalGameSession(state, Data, Other);

        session.SendAs(Quitter, new SurrenderCommand());

        Assert.Equal(new[] { Other }, state.Outcome!.WinnerIds);
    }

    [Fact]
    public void SurrenderingTwiceIsRejected()
    {
        Surrender();

        Assert.Equal(CommandRejection.AlreadyOutOfTheMatch, Surrender().Rejection);
    }

    [Fact]
    public void ThePauseStopsIt()
    {
        _session.SetPaused(true);

        Assert.Equal(CommandRejection.GamePaused, Surrender().Rejection);
        Assert.False(_state.Players[Quitter].IsEliminated);
    }

    [Fact]
    public void TheScoreTableStillListsThePlayerLast()
    {
        Surrender();
        Surrender(Third);

        Assert.Equal(Other, _state.Outcome!.Scores[0].PlayerId);
        Assert.Equal(3, _state.Outcome.Scores.Count);
    }

    [Fact]
    public void ItSurvivesASnapshot()
    {
        Surrender();

        var json = SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(_state));
        var restored = SnapshotMapper.Restore(SnapshotMapper.FromJson(json));

        Assert.Equal(json, SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(restored)));
        Assert.True(restored.Players[Quitter].HasSurrendered);
        Assert.True(restored.Villages[Quitter].IsAbandoned);
    }

    [Theory]
    [InlineData(600)]
    [InlineData(1)]
    [InlineData(0.25)]
    public void AnAbandonedVillageIsTheSameAtAnyStepSize(double step)
    {
        var state = TestMatch.Create(players: 3, duration: 36000);
        var session = new LocalGameSession(state, Data, Other);
        session.SendAs(Quitter, new SurrenderCommand());
        var before = state.Villages[Quitter].Resources;

        for (var time = step; time <= 600; time += step)
            session.Tick(step);

        Assert.Equal(before, state.Villages[Quitter].Resources);
    }
}
