using Valkaris.Core.Commands;
using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class UnlimitedTests
{
    private static GameState Unlimited() =>
        GameFactory.CreateMatch(new GameSettings(null, 1, MapSize.Small, 5), TestMatch.Slots(2), TestData.Shipped);

    [Fact]
    public void TimeNeverEndsTheMatch()
    {
        var state = Unlimited();
        var simulation = new Simulation(state, TestData.Shipped);

        simulation.AdvanceTo(1000000);

        Assert.False(state.IsMatchOver);
        Assert.Equal(1000000, state.ElapsedTime);
    }

    [Fact]
    public void ALimitedMatchStillEndsOnTime()
    {
        var state = GameFactory.CreateMatch(new GameSettings(100, 1, MapSize.Small, 5), TestMatch.Slots(2), TestData.Shipped);

        new Simulation(state, TestData.Shipped).AdvanceTo(1000);

        Assert.Equal(MatchEndReason.TimeUp, state.Outcome!.Reason);
        Assert.Equal(100, state.ElapsedTime);
    }

    [Fact]
    public void TheLastPlayerStandingEndsIt()
    {
        var state = Unlimited();
        var handler = new CommandHandler(state, TestData.Shipped);
        var simulation = new Simulation(state, TestData.Shipped);
        var target = state.Villages[2];
        foreach (var (unit, count) in target.Troops.ToList())
            target.RemoveTroops(unit, count);
        target.Loyalty = 1;
        var home = state.Villages[1];
        home.AddTroops(UnitType.Swordsman, 10);
        home.AddTroops(UnitType.Lord, 1);
        Assert.True(handler.Handle(1, new SendTroopsCommand(1, 2, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 10, [UnitType.Lord] = 1 })).Success);

        simulation.AdvanceTo(state.Movements.Single().ArrivalTime);

        Assert.Equal(MatchEndReason.LastPlayerStanding, state.Outcome!.Reason);
        Assert.Equal(new[] { 1 }, state.Outcome.WinnerIds);
    }

    [Fact]
    public void ItSurvivesASnapshot()
    {
        var json = SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(Unlimited()));

        Assert.Null(SnapshotMapper.Restore(SnapshotMapper.FromJson(json)).Settings.MatchDurationSeconds);
    }

    [Fact]
    public void TheDataOffersIt() => Assert.True(TestData.Shipped.Game.UnlimitedMatches);
}
