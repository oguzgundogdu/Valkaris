using Valkaris.Ai;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class AiEconomyTests
{
    private static AiMatch Match(AiDifficulty difficulty = AiDifficulty.Normal, double minutes = 60) =>
        new(new[] { difficulty, difficulty }, minutes: minutes);

    [Fact]
    public void AnAiGrowsAHealthyEconomy()
    {
        var match = Match();

        match.Run(900);

        var village = match.State.Villages[1];
        Assert.True(village.BuildingLevel(BuildingType.Woodcutter) >= 3, $"Woodcutter {village.BuildingLevel(BuildingType.Woodcutter)}");
        Assert.True(village.BuildingLevel(BuildingType.Farm) >= 3, $"Farm {village.BuildingLevel(BuildingType.Farm)}");
        Assert.True(village.BuildingLevel(BuildingType.MainBuilding) >= 2);
        Assert.True(village.BuildingLevel(BuildingType.Barracks) >= 1);
        Assert.Equal(0, match.RejectedCommands);
    }

    [Fact]
    public void AllLevelsFollowTheBuildOrderWithoutRejections()
    {
        foreach (var difficulty in Enum.GetValues<AiDifficulty>())
        {
            var match = Match(difficulty);
            match.Run(1800);
            Assert.Equal(0, match.RejectedCommands);
        }
    }

    [Fact]
    public void AWarehouseIsBuiltWhenStockNearsTheCap()
    {
        var match = Match();
        var village = match.State.Villages[1];
        village.Resources = new Resources(950, 950, 950);

        new BuildPlanner(match.Views[1]).Plan(village);

        Assert.Contains(village.BuildQueue, q => q.Building == BuildingType.Warehouse);
    }

    [Fact]
    public void ACapturedCampRebuildsWhatItLacks()
    {
        var match = Match();
        var camp = match.State.Villages.Values.First(v => v.IsBarbarian);
        camp.OwnerId = 1;
        camp.BarbarianTier = null;
        camp.NextTroopRegenTime = null;
        Assert.Equal(0, camp.BuildingLevel(BuildingType.Woodcutter));

        match.Run(600);

        Assert.True(camp.BuildingLevel(BuildingType.Woodcutter) >= 1);
        Assert.True(camp.BuildingLevel(BuildingType.Quarry) >= 1);
        Assert.True(camp.BuildingLevel(BuildingType.Farm) >= 1);
    }

    [Fact]
    public void ASavingAiWaitsForTheNextStep()
    {
        var village = PlanWithLittleStone(AiDifficulty.Normal);

        Assert.Empty(village.BuildQueue);
    }

    [Fact]
    public void ASpenderBuysTheNextThingItCanAfford()
    {
        var village = PlanWithLittleStone(AiDifficulty.Easy);

        Assert.Equal(BuildingType.Quarry, Assert.Single(village.BuildQueue).Building);
    }

    /// <summary>One planning pass, with enough to afford a Quarry upgrade but not the Woodcutter upgrade that comes first.</summary>
    private static Village PlanWithLittleStone(AiDifficulty difficulty)
    {
        var match = Match(difficulty);
        var village = match.State.Villages[1];
        village.Resources = new Resources(200, 60, 100);

        new BuildPlanner(match.Views[1]).Plan(village);

        return village;
    }
}
