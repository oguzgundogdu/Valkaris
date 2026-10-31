using Valkaris.Core.Commands;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class WatchtowerTests
{
    [Fact]
    public void NoVillageStartsWithOne()
    {
        Assert.Equal(0, TestData.Shipped.Game.StartingBuildings[BuildingType.Watchtower]);
        Assert.Equal(0, TestData.Shipped.Game.FoundedVillage.Buildings[BuildingType.Watchtower]);
    }

    [Fact]
    public void ItCanBeBuiltAndAddsToTheScore()
    {
        var state = TestMatch.Create(duration: 36000);
        var village = state.Villages[1];
        var population = ScoreRules.Population(TestData.Shipped, village);

        Assert.True(new CommandHandler(state, TestData.Shipped).Handle(1, new BuildCommand(1, BuildingType.Watchtower)).Success);
        new Simulation(state, TestData.Shipped).AdvanceTo(village.BuildQueue.Single().FinishTime);

        Assert.Equal(1, village.BuildingLevel(BuildingType.Watchtower));
        Assert.True(ScoreRules.Population(TestData.Shipped, village) > population);
    }
}
