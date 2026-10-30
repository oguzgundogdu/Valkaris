using Valkaris.Core.Commands;
using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class OutcomeTests
{
    private readonly GameState _state;
    private readonly CommandHandler _handler;
    private readonly Simulation _simulation;
    private readonly List<GameEvent> _events = new();

    public OutcomeTests() : this(TestMatch.Create(players: 3))
    {
    }

    private OutcomeTests(GameState state)
    {
        _state = state;
        _handler = new CommandHandler(state, TestData.Shipped);
        _simulation = new Simulation(state, TestData.Shipped);
    }

    private void Capture(int attacker, int from, int target)
    {
        var village = _state.Villages[target];
        foreach (var (unit, count) in village.Troops.ToList())
            village.RemoveTroops(unit, count);
        village.Loyalty = 1;

        var troops = new Dictionary<UnitType, int> { [UnitType.Swordsman] = 50, [UnitType.Lord] = 1 };
        foreach (var (unit, count) in troops)
            _state.Villages[from].AddTroops(unit, count);
        var result = _handler.Handle(attacker, new SendTroopsCommand(from, target, MovementType.Attack, troops));
        Assert.True(result.Success, result.Rejection.ToString());
        _events.AddRange(_simulation.Advance(_state.Movements[^1].ArrivalTime - _state.ElapsedTime));
        Assert.Equal(attacker, village.OwnerId);
    }

    [Fact]
    public void LosingTheLastVillageEliminates()
    {
        Capture(1, 1, 3);

        Assert.True(_state.Players[3].IsEliminated);
        Assert.Null(_state.Players[3].CapitalVillageId);
        Assert.Equal(3, _events.OfType<PlayerEliminated>().Single().PlayerId);
        Assert.False(_state.IsMatchOver);
    }

    [Fact]
    public void AnEliminatedPlayersMovementsAreRemoved()
    {
        var attack = new Dictionary<UnitType, int> { [UnitType.Spearman] = 5 };
        var result = _handler.Handle(3, new SendTroopsCommand(3, 2, MovementType.Attack, attack));
        Assert.True(result.Success);

        Capture(1, 1, 3);

        Assert.DoesNotContain(_state.Movements, m => m.OwnerId == 3);
    }

    [Fact]
    public void AnEliminatedPlayerCanNoLongerAct() =>
        AfterElimination(() => Assert.Equal(CommandRejection.NotOwner, _handler.Handle(3, new BuildCommand(3, BuildingType.Farm)).Rejection));

    private void AfterElimination(Action assert)
    {
        Capture(1, 1, 3);
        assert();
    }

    [Fact]
    public void LosingAVillageDuringACapitalChoiceCanEliminate()
    {
        var second = _state.Villages.Values.First(v => v.IsBarbarian);
        second.OwnerId = 1;
        second.BarbarianTier = null;
        Capture(2, 2, 1);
        Assert.NotNull(_state.Players[1].CapitalChoiceDeadline);

        Capture(2, 2, second.Id);

        Assert.True(_state.Players[1].IsEliminated);
        Assert.Null(_state.Players[1].CapitalChoiceDeadline);
    }

    [Fact]
    public void LastPlayerStandingEndsTheMatchAtThatMoment()
    {
        Capture(1, 1, 3);
        Capture(1, 1, 2);
        var end = _state.ElapsedTime;

        Assert.True(_state.IsMatchOver);
        var outcome = _state.Outcome!;
        Assert.Equal(MatchEndReason.LastPlayerStanding, outcome.Reason);
        Assert.Equal(new[] { 1 }, outcome.WinnerIds);
        Assert.Equal(end, outcome.Time);
        Assert.IsType<MatchEnded>(_events[^1]);

        var later = _simulation.Advance(100);

        Assert.Empty(later);
        Assert.Equal(end, _state.ElapsedTime);
    }

    [Fact]
    public void CommandsAreRejectedAfterTheEnd()
    {
        Capture(1, 1, 3);
        Capture(1, 1, 2);

        Assert.Equal(CommandRejection.MatchOver, _handler.Handle(1, new BuildCommand(1, BuildingType.Farm)).Rejection);
    }

    [Fact]
    public void BarbariansDoNotKeepAMatchGoing()
    {
        Capture(1, 1, 3);
        Capture(1, 1, 2);

        Assert.Contains(_state.Villages.Values, v => v.IsBarbarian);
        Assert.True(_state.IsMatchOver);
    }

    // Time-out.

    [Fact]
    public void TimeOutPicksTheHighestScore()
    {
        var test = new OutcomeTests(TestMatch.Create(players: 2, duration: 100));
        test._state.Villages[2].SetBuildingLevel(BuildingType.Barracks, 3);

        var events = test._simulation.Advance(100);

        Assert.Equal(MatchEndReason.TimeUp, test._state.Outcome!.Reason);
        Assert.Equal(new[] { 2 }, test._state.Outcome.WinnerIds);
        Assert.Equal(2, test._state.Outcome.Scores[0].PlayerId);
        Assert.IsType<MatchEnded>(events[^1]);
    }

    [Fact]
    public void ATieIsADrawBetweenTheLeaders()
    {
        var test = new OutcomeTests(TestMatch.Create(players: 3, duration: 100));
        test._state.Villages[3].SetBuildingLevel(BuildingType.Farm, 0);

        test._simulation.Advance(100);

        Assert.Equal(new[] { 1, 2 }, test._state.Outcome!.WinnerIds);
    }

    [Fact]
    public void AnEliminatedPlayerCannotWinOnScore()
    {
        var state = TestMatch.Create(players: 3, duration: 5000);
        var test = new OutcomeTests(state);
        test.Capture(1, 1, 3);
        for (var id = 100; id < 110; id++)
            state.Players[3].RecordCapture(id);

        test._simulation.Advance(5000);

        Assert.DoesNotContain(3, state.Outcome!.WinnerIds);
        Assert.Equal(3, state.Outcome.Scores[^1].PlayerId);
    }

    [Fact]
    public void AnEventAtTheFinalMomentCountsBeforeTheScore()
    {
        var state = TestMatch.Create(players: 2, duration: 30);
        state.Villages[1].EnqueueBuild(new BuildQueueItem(BuildingType.Barracks, 1, 0, 30));

        new Simulation(state, TestData.Shipped).Advance(30);

        Assert.Equal(new[] { 1 }, state.Outcome!.WinnerIds);
    }

    // The score.

    [Fact]
    public void ScoreUsesTheWeightsFromData()
    {
        var scoring = TestData.Shipped.Scoring;
        var village = _state.Villages[1];
        var population = village.Buildings.Sum(b => b.Value * scoring.PopulationPerLevel[b.Key]);
        _state.Players[1].RecordCapture(50);

        var score = ScoreRules.For(_state, TestData.Shipped, _state.Players[1]);

        Assert.Equal(1, score.Villages);
        Assert.Equal(population, score.Population);
        Assert.Equal(1, score.Captures);
        Assert.Equal(scoring.VillageWeight + population * scoring.PopulationWeight + scoring.CaptureWeight, score.Score);
    }

    [Fact]
    public void ChangedWeightsChangeTheScore()
    {
        var data = TestData.Mutated("scoring.json", r => r["villageWeight"] = 1000);

        Assert.True(ScoreRules.For(_state, data, _state.Players[1]).Score > 1000);
    }

    [Fact]
    public void RecapturingTheSameVillageCountsOnce()
    {
        var camp = _state.Villages.Values.First(v => v.IsBarbarian);
        Capture(1, 1, camp.Id);
        Capture(2, 2, camp.Id);
        Capture(1, 1, camp.Id);
        Capture(2, 2, camp.Id);
        Capture(1, 1, camp.Id);

        Assert.Equal(1, ScoreRules.For(_state, TestData.Shipped, _state.Players[1]).Captures);
        Assert.Equal(1, ScoreRules.For(_state, TestData.Shipped, _state.Players[2]).Captures);
    }

    [Fact]
    public void DifferentVillagesEachCount()
    {
        var camps = _state.Villages.Values.Where(v => v.IsBarbarian).Take(2).ToList();
        Capture(1, 1, camps[0].Id);
        Capture(1, 1, camps[1].Id);

        Assert.Equal(2, ScoreRules.For(_state, TestData.Shipped, _state.Players[1]).Captures);
    }

    [Fact]
    public void CapturesAreKeptWhenTheVillageIsLost()
    {
        var camp = _state.Villages.Values.First(v => v.IsBarbarian);
        Capture(1, 1, camp.Id);
        Capture(2, 2, camp.Id);

        Assert.Equal(1, ScoreRules.For(_state, TestData.Shipped, _state.Players[1]).Captures);
    }

    // Snapshots.

    [Fact]
    public void ARestoredMatchContinuesIdentically()
    {
        Capture(1, 1, 3);

        var restored = SnapshotAssert.RoundTrips(_state);
        new Simulation(restored, TestData.Shipped).Advance(TestMatch.OneHour);
        _simulation.Advance(TestMatch.OneHour);

        SnapshotAssert.RoundTrips(restored);
        Assert.Equal(_state.Outcome, restored.Outcome, new OutcomeComparer());
    }

    [Fact]
    public void ARestoredFinishedMatchStaysOver()
    {
        Capture(1, 1, 3);
        Capture(1, 1, 2);

        var restored = SnapshotAssert.RoundTrips(_state);

        Assert.True(restored.IsMatchOver);
        Assert.Equal(new[] { 1 }, restored.Outcome!.WinnerIds);
    }

    private sealed class OutcomeComparer : IEqualityComparer<MatchOutcome?>
    {
        public bool Equals(MatchOutcome? a, MatchOutcome? b) =>
            a!.Reason == b!.Reason && a.Time == b.Time && a.WinnerIds.SequenceEqual(b.WinnerIds) && a.Scores.SequenceEqual(b.Scores);

        public int GetHashCode(MatchOutcome? obj) => 0;
    }
}
