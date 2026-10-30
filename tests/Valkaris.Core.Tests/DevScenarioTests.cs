using Valkaris.Core.Commands;
using Valkaris.Core.Dev;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class DevScenarioTests
{
    [Fact]
    public void UnknownNameChangesNothing()
    {
        var state = TestMatch.Create();
        var before = SnapshotAssert.RoundTrips(state);

        Assert.False(DevScenario.Apply(state, TestData.Shipped, "nope"));

        Assert.Equal(before.Villages[1].Loyalty, state.Villages[1].Loyalty);
    }

    [Theory]
    [InlineData(DevScenario.Villages)]
    [InlineData(DevScenario.Founding)]
    [InlineData(DevScenario.Endgame)]
    [InlineData(DevScenario.LastStand)]
    [InlineData(DevScenario.Empire)]
    public void EveryScenarioGivesAValidState(string name)
    {
        var state = TestMatch.Create();

        Assert.True(DevScenario.Apply(state, TestData.Shipped, name));

        SnapshotAssert.RoundTrips(state);
    }

    [Theory]
    [InlineData(MapSize.Small)]
    [InlineData(MapSize.Medium)]
    public void TheVillagesRivalCanTakeYourCapital(MapSize size)
    {
        var state = GameFactory.CreateMatch(new GameSettings(TestMatch.OneHour, 1, size), TestMatch.Slots(2), TestData.Shipped);
        DevScenario.Apply(state, TestData.Shipped, DevScenario.Villages);
        var force = state.Villages.Values.Where(v => v.OwnerId == 2).MaxBy(v => v.Troops.Values.Sum())!;
        var troops = force.Troops.Where(t => t.Value > 0).ToDictionary(t => t.Key, t => t.Value);

        Assert.True(new CommandHandler(state, TestData.Shipped).Handle(2, new SendTroopsCommand(force.Id, 1, MovementType.Attack, troops)).Success);
        new Simulation(state, TestData.Shipped).Advance(state.Movements[0].ArrivalTime);

        Assert.Equal(2, state.Villages[1].OwnerId);
        Assert.NotNull(state.Players[1].CapitalChoiceDeadline);
    }
}
