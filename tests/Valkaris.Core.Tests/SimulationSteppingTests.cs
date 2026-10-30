using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class SimulationSteppingTests
{
    private const int Owner = 1;
    private const int Precision = 6;

    private static readonly GameData Data = TestData.Shipped;

    private static double BuildTime(BuildingType building, int level) =>
        BuildingRules.BuildTimeSeconds(Data, building, level);

    private static double Rate(BuildingType building, int level, ResourceType resource) =>
        BuildingRules.Production(Data, building, level).Get(resource);

    private static (GameState State, IReadOnlyList<GameEvent> Events) Run(double speed, double step, double total, params BuildingType[] builds)
    {
        var state = TestMatch.Create(speed: speed);
        var handler = new CommandHandler(state, Data);
        foreach (var building in builds)
            Assert.True(handler.Handle(Owner, new BuildCommand(1, building)).Success);

        var simulation = new Simulation(state, Data);
        var events = new List<GameEvent>();
        var steps = (int)Math.Round(total / step);
        for (var i = 0; i < steps; i++)
            events.AddRange(simulation.Advance(step));
        return (state, events);
    }

    private static void AssertEquivalent(GameState expected, GameState actual)
    {
        Assert.Equal(expected.ElapsedTime, actual.ElapsedTime, Precision);
        foreach (var (id, village) in expected.Villages)
        {
            var other = actual.Villages[id];
            Assert.Equal(village.Resources.Wood, other.Resources.Wood, Precision);
            Assert.Equal(village.Resources.Stone, other.Resources.Stone, Precision);
            Assert.Equal(village.Resources.Food, other.Resources.Food, Precision);
            Assert.Equal(village.Buildings, other.Buildings);
            Assert.Equal(village.BuildQueue, other.BuildQueue);
        }
    }

    public static IEnumerable<object[]> Scenarios() => new[]
    {
        new object[] { 1.0, Array.Empty<BuildingType>() },
        new object[] { 1.0, new[] { BuildingType.Woodcutter } },
        new object[] { 1.0, new[] { BuildingType.Woodcutter, BuildingType.Woodcutter } },
        new object[] { 1.0, new[] { BuildingType.Warehouse, BuildingType.Farm } },
        new object[] { 3.0, new[] { BuildingType.Quarry, BuildingType.Warehouse } },
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void ResultDoesNotDependOnStepSize(double speed, BuildingType[] builds)
    {
        var (oneBigStep, bigEvents) = Run(speed, step: 600, total: 600, builds);
        var (oneSecond, secondEvents) = Run(speed, step: 1, total: 600, builds);
        var (quarterSecond, quarterEvents) = Run(speed, step: 0.25, total: 600, builds);

        AssertEquivalent(oneBigStep, oneSecond);
        AssertEquivalent(oneBigStep, quarterSecond);
        Assert.Equal(bigEvents, secondEvents);
        Assert.Equal(bigEvents, quarterEvents);
    }

    [Fact]
    public void ProductionUpgradeTakesEffectAtTheMomentItCompletes()
    {
        var state = TestMatch.Create();
        new CommandHandler(state, Data).Handle(Owner, new BuildCommand(1, BuildingType.Woodcutter));
        var woodAfterPaying = state.Villages[1].Resources.Wood;
        var finish = BuildTime(BuildingType.Woodcutter, 2);

        new Simulation(state, Data).Advance(100);

        var expected = woodAfterPaying
            + Rate(BuildingType.Woodcutter, 1, ResourceType.Wood) * finish
            + Rate(BuildingType.Woodcutter, 2, ResourceType.Wood) * (100 - finish);
        Assert.Equal(expected, state.Villages[1].Resources.Wood, Precision);
    }

    [Fact]
    public void WarehouseUpgradeRaisesTheCapAtTheMomentItCompletes()
    {
        var state = TestMatch.Create();
        new CommandHandler(state, Data).Handle(Owner, new BuildCommand(1, BuildingType.Warehouse));
        var village = state.Villages[1];
        var oldCap = BuildingRules.VillageCapacity(Data, village);
        village.Resources = new Resources(oldCap, oldCap, oldCap);
        var finish = BuildTime(BuildingType.Warehouse, 2);

        new Simulation(state, Data).Advance(finish + 10);

        // Held at the old cap until the upgrade finishes, then grows for 10 seconds.
        Assert.Equal(oldCap + Rate(BuildingType.Woodcutter, 1, ResourceType.Wood) * 10, village.Resources.Wood, Precision);
    }

    [Fact]
    public void ElapsedTimeIsCorrectAfterACompletionMidStep()
    {
        var state = TestMatch.Create();
        new CommandHandler(state, Data).Handle(Owner, new BuildCommand(1, BuildingType.Woodcutter));

        new Simulation(state, Data).Advance(100);

        Assert.Equal(100, state.ElapsedTime);
    }

    [Fact]
    public void SimultaneousCompletionsAreOrderedByVillageId()
    {
        var state = TestMatch.Create();
        var handler = new CommandHandler(state, Data);
        handler.Handle(2, new BuildCommand(2, BuildingType.Woodcutter));
        handler.Handle(1, new BuildCommand(1, BuildingType.Woodcutter));

        var events = new Simulation(state, Data).Advance(100).Cast<BuildingCompleted>().ToList();

        Assert.Equal(new[] { 1, 2 }, events.Select(e => e.VillageId));
    }

    [Fact]
    public void FullEconomyScenario()
    {
        var state = TestMatch.Create();
        var handler = new CommandHandler(state, Data);
        var simulation = new Simulation(state, Data);
        var village = state.Villages[1];

        // Spend the starting resources on two upgrades; a third has to wait for the queue.
        Assert.True(handler.Handle(Owner, new BuildCommand(1, BuildingType.Woodcutter)).Success);
        Assert.True(handler.Handle(Owner, new BuildCommand(1, BuildingType.MainBuilding)).Success);
        Assert.Equal(
            CommandRejection.QueueFull,
            handler.Handle(Owner, new BuildCommand(1, BuildingType.Farm)).Rejection);

        // Both finish, and the economy has grown.
        var events = simulation.Advance(BuildTime(BuildingType.Woodcutter, 2) + BuildTime(BuildingType.MainBuilding, 2));
        Assert.Equal(2, events.Count);
        Assert.Equal(2, village.BuildingLevel(BuildingType.Woodcutter));
        Assert.Equal(2, village.BuildingLevel(BuildingType.MainBuilding));
        Assert.Empty(village.BuildQueue);

        // The higher Main Building allows a level the old one did not.
        var oldLimit = BuildingRules.MaxOtherBuildingLevel(Data, 1);
        Assert.True(BuildingRules.MaxOtherBuildingLevel(Data, 2) > oldLimit);
        village.SetBuildingLevel(BuildingType.Woodcutter, oldLimit);

        // Earn enough for the next upgrade, buy it, and wait for it.
        var cost = BuildingRules.UpgradeCost(Data, BuildingType.Woodcutter, oldLimit + 1);
        while (!village.Resources.Covers(cost))
            simulation.Advance(1);
        Assert.True(handler.Handle(Owner, new BuildCommand(1, BuildingType.Woodcutter)).Success);
        simulation.Advance(BuildTime(BuildingType.Woodcutter, oldLimit + 1));
        Assert.Equal(oldLimit + 1, village.BuildingLevel(BuildingType.Woodcutter));

        SnapshotAssert.RoundTrips(state);

        // Time runs out: no more commands, no more production.
        simulation.Advance(state.Settings.MatchDurationSeconds);
        Assert.True(state.IsMatchOver);
        var finalResources = village.Resources;
        Assert.Equal(
            CommandRejection.MatchOver,
            handler.Handle(Owner, new BuildCommand(1, BuildingType.Farm)).Rejection);
        simulation.Advance(100);
        Assert.Equal(finalResources, village.Resources);
    }
}
