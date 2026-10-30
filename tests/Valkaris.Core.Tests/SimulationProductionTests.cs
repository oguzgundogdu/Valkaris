using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class SimulationProductionTests
{
    private static readonly GameData Data = TestData.Shipped;

    private static (GameState State, Simulation Simulation, Village Village) Setup(double speed = 1, double duration = TestMatch.OneHour)
    {
        var state = TestMatch.Create(speed: speed, duration: duration);
        var village = state.Villages[1];
        village.Resources = Resources.Zero;
        return (state, new Simulation(state, Data), village);
    }

    [Fact]
    public void ProducesRateTimesTime()
    {
        var (state, simulation, village) = Setup();
        var rate = BuildingRules.VillageProduction(Data, village);

        simulation.Advance(10);

        Assert.Equal(rate * 10, village.Resources);
        Assert.Equal(10, state.ElapsedTime);
    }

    [Fact]
    public void HigherLevelsProduceMore()
    {
        var (_, simulation, village) = Setup();
        village.SetBuildingLevel(BuildingType.Woodcutter, 3);

        simulation.Advance(10);

        Assert.Equal(BuildingRules.Production(Data, BuildingType.Woodcutter, 3).Wood * 10, village.Resources.Wood, precision: 10);
        Assert.True(village.Resources.Wood > village.Resources.Stone);
    }

    [Fact]
    public void LevelZeroProducerProducesNothing()
    {
        var (_, simulation, village) = Setup();
        village.SetBuildingLevel(BuildingType.Quarry, 0);

        simulation.Advance(10);

        Assert.Equal(0, village.Resources.Stone);
        Assert.True(village.Resources.Wood > 0);
    }

    [Fact]
    public void EveryVillageProduces()
    {
        var (state, simulation, _) = Setup();
        state.Villages[2].Resources = Resources.Zero;

        simulation.Advance(10);

        Assert.Equal(state.Villages[1].Resources, state.Villages[2].Resources);
    }

    [Fact]
    public void StopsExactlyAtWarehouseCapacity()
    {
        var (_, simulation, village) = Setup();
        var cap = BuildingRules.VillageCapacity(Data, village);
        var rate = BuildingRules.VillageProduction(Data, village).Wood;
        village.Resources = new Resources(cap - rate * 5, 0, 0);

        simulation.Advance(5);

        Assert.Equal(cap, village.Resources.Wood);
    }

    [Fact]
    public void NeverExceedsCapacity()
    {
        var (_, simulation, village) = Setup();
        var cap = BuildingRules.VillageCapacity(Data, village);

        simulation.Advance(TestMatch.OneHour);

        Assert.Equal(new Resources(cap, cap, cap), village.Resources);
    }

    [Fact]
    public void CapacityRisesWithWarehouseLevel()
    {
        var (_, simulation, village) = Setup();
        village.SetBuildingLevel(BuildingType.Warehouse, 3);

        simulation.Advance(TestMatch.OneHour);

        Assert.Equal(BuildingRules.WarehouseCapacity(Data, 3), village.Resources.Wood);
    }

    [Fact]
    public void CapIsPerResource()
    {
        var (_, simulation, village) = Setup();
        var cap = BuildingRules.VillageCapacity(Data, village);
        village.Resources = new Resources(cap, 0, 0);

        simulation.Advance(1);

        Assert.Equal(cap, village.Resources.Wood);
        Assert.True(village.Resources.Stone > 0);
    }

    [Fact]
    public void SpeedMultiplierScalesProductionButNotElapsedTime()
    {
        var (state, simulation, village) = Setup(speed: 3);
        var rate = BuildingRules.VillageProduction(Data, village);

        simulation.Advance(10);

        Assert.Equal(rate * 30, village.Resources);
        Assert.Equal(10, state.ElapsedTime);
    }

    [Fact]
    public void TimeAndProductionStopAtTheEndOfTheMatch()
    {
        var (state, simulation, village) = Setup(duration: 10);
        var rate = BuildingRules.VillageProduction(Data, village);

        simulation.Advance(25);
        simulation.Advance(25);

        Assert.Equal(10, state.ElapsedTime);
        Assert.True(state.IsMatchOver);
        Assert.Equal(rate * 10, village.Resources);
    }

    [Fact]
    public void ZeroDeltaChangesNothing()
    {
        var (state, simulation, village) = Setup();

        simulation.Advance(0);

        Assert.Equal(0, state.ElapsedTime);
        Assert.Equal(Resources.Zero, village.Resources);
    }

    [Fact]
    public void NegativeDeltaIsRejected()
    {
        var (_, simulation, _) = Setup();

        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Advance(-1));
    }
}
