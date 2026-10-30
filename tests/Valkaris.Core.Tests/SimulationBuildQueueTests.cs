using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class SimulationBuildQueueTests
{
    private const int Owner = 1;

    private static readonly GameData Data = TestData.Shipped;

    private readonly GameState _state;
    private readonly CommandHandler _handler;
    private readonly Simulation _simulation;
    private readonly Village _village;

    public SimulationBuildQueueTests()
    {
        _state = TestMatch.Create();
        _handler = new CommandHandler(_state, Data);
        _simulation = new Simulation(_state, Data);
        _village = _state.Villages[1];
    }

    private void Build(BuildingType building, int owner = Owner, int villageId = 1) =>
        Assert.True(_handler.Handle(owner, new BuildCommand(villageId, building)).Success);

    private static double BuildTime(BuildingType building, int level) =>
        BuildingRules.BuildTimeSeconds(Data, building, level);

    [Fact]
    public void DoesNotCompleteBeforeFinishTime()
    {
        Build(BuildingType.Woodcutter);

        var events = _simulation.Advance(BuildTime(BuildingType.Woodcutter, 2) - 0.001);

        Assert.Empty(events);
        Assert.Equal(1, _village.BuildingLevel(BuildingType.Woodcutter));
        Assert.Single(_village.BuildQueue);
    }

    [Fact]
    public void CompletesExactlyOnFinishTime()
    {
        Build(BuildingType.Woodcutter);
        var time = BuildTime(BuildingType.Woodcutter, 2);

        var events = _simulation.Advance(time);

        var completed = Assert.IsType<BuildingCompleted>(Assert.Single(events));
        Assert.Equal(new BuildingCompleted(time, 1, BuildingType.Woodcutter, 2), completed);
        Assert.Equal(2, _village.BuildingLevel(BuildingType.Woodcutter));
        Assert.Empty(_village.BuildQueue);
    }

    [Fact]
    public void EventIsReportedOnlyOnce()
    {
        Build(BuildingType.Woodcutter);
        _simulation.Advance(BuildTime(BuildingType.Woodcutter, 2));

        Assert.Empty(_simulation.Advance(100));
    }

    [Fact]
    public void QueuedItemsRunInSequence()
    {
        Build(BuildingType.Woodcutter);
        Build(BuildingType.Quarry);
        var first = BuildTime(BuildingType.Woodcutter, 2);
        var second = BuildTime(BuildingType.Quarry, 2);

        _simulation.Advance(first);
        Assert.Equal(2, _village.BuildingLevel(BuildingType.Woodcutter));
        Assert.Equal(1, _village.BuildingLevel(BuildingType.Quarry));

        _simulation.Advance(second - 0.001);
        Assert.Equal(1, _village.BuildingLevel(BuildingType.Quarry));

        _simulation.Advance(0.001);
        Assert.Equal(2, _village.BuildingLevel(BuildingType.Quarry));
    }

    [Fact]
    public void TwoUpgradesOfTheSameBuildingReachLevelPlusTwo()
    {
        Build(BuildingType.Woodcutter);
        Build(BuildingType.Woodcutter);

        var events = _simulation.Advance(TestMatch.OneHour);

        Assert.Equal(3, _village.BuildingLevel(BuildingType.Woodcutter));
        Assert.Equal(new[] { 2, 3 }, events.Cast<BuildingCompleted>().Select(e => e.NewLevel));
    }

    [Fact]
    public void OneLargeStepCompletesEverythingAndReportsActualFinishTimes()
    {
        Build(BuildingType.Woodcutter);
        Build(BuildingType.Quarry);
        var first = BuildTime(BuildingType.Woodcutter, 2);
        var second = first + BuildTime(BuildingType.Quarry, 2);

        var events = _simulation.Advance(TestMatch.OneHour);

        Assert.Equal(new[] { first, second }, events.Select(e => e.Time));
    }

    [Fact]
    public void EventsFromDifferentVillagesAreInTimeOrder()
    {
        Build(BuildingType.Barracks);                                // Slow, in village 1.
        Build(BuildingType.Woodcutter, owner: 2, villageId: 2);      // Fast, in village 2.

        var events = _simulation.Advance(TestMatch.OneHour).Cast<BuildingCompleted>().ToList();

        Assert.Equal(new[] { 2, 1 }, events.Select(e => e.VillageId));
    }

    [Fact]
    public void SpeedMultiplierShortensQueueTime()
    {
        var fast = TestMatch.Create(speed: 2);
        new CommandHandler(fast, Data).Handle(Owner, new BuildCommand(1, BuildingType.Woodcutter));

        new Simulation(fast, Data).Advance(BuildTime(BuildingType.Woodcutter, 2) / 2);

        Assert.Equal(2, fast.Villages[1].BuildingLevel(BuildingType.Woodcutter));
    }

    [Fact]
    public void ItemsFinishingAfterTheMatchEndDoNotComplete()
    {
        var shortMatch = TestMatch.Create(duration: 5);
        new CommandHandler(shortMatch, Data).Handle(Owner, new BuildCommand(1, BuildingType.Woodcutter));

        var events = new Simulation(shortMatch, Data).Advance(TestMatch.OneHour);

        Assert.Empty(events);
        Assert.Equal(1, shortMatch.Villages[1].BuildingLevel(BuildingType.Woodcutter));
    }

    [Fact]
    public void FreedQueueSlotCanBeUsedAgain()
    {
        Build(BuildingType.Woodcutter);
        Build(BuildingType.Quarry);
        _simulation.Advance(BuildTime(BuildingType.Woodcutter, 2));

        Build(BuildingType.Farm);

        Assert.Equal(2, _village.BuildQueue.Count);
    }

    [Fact]
    public void StateRoundTripsWithAQueueInProgress()
    {
        Build(BuildingType.Woodcutter);
        Build(BuildingType.Quarry);
        _simulation.Advance(1);

        var restored = SnapshotAssert.RoundTrips(_state);

        Assert.Equal(_village.BuildQueue, restored.Villages[1].BuildQueue);
    }

    [Fact]
    public void RestoredStateContinuesLikeTheOriginal()
    {
        Build(BuildingType.Woodcutter);
        Build(BuildingType.Quarry);
        _simulation.Advance(1);
        var restored = SnapshotAssert.RoundTrips(_state);

        _simulation.Advance(500);
        new Simulation(restored, Data).Advance(500);

        Assert.Equal(_village.Resources, restored.Villages[1].Resources);
        Assert.Equal(_village.BuildingLevel(BuildingType.Quarry), restored.Villages[1].BuildingLevel(BuildingType.Quarry));
    }
}
