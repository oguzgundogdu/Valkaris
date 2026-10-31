using Valkaris.Ai;
using Valkaris.Core.Commands;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class AiAbandonedTests
{
    private const int Ai = 1;
    private const int Quitter = 2;

    private static AiMatch Abandoned(int garrison = 3)
    {
        var match = new AiMatch(new[] { AiDifficulty.Hard, AiDifficulty.Normal, AiDifficulty.Normal }, idle: new[] { 2, 3 });
        var village = match.State.Villages[Quitter];
        foreach (var (unit, count) in village.Troops.ToList())
            village.RemoveTroops(unit, count);
        village.AddTroops(UnitType.Spearman, garrison);
        village.Resources = new Resources(900, 900, 900);
        Assert.True(match.Session.SendAs(Quitter, new SurrenderCommand()).Success);
        return match;
    }

    private static KnownVillage Known(AiMatch match) => match.Views[Ai].KnownVillages.Single(v => v.Id == Quitter);

    [Fact]
    public void ItIsKnownAsAbandoned() => Assert.True(Known(Abandoned()).IsAbandoned);

    [Fact]
    public void BeforeAnyoneLooksTheBeliefsApply()
    {
        var match = Abandoned();
        var memory = new AiMemory(match.Views[Ai]);

        Assert.Equal(match.Data.Ai.Beliefs.AbandonedPower, memory.EstimateDefence(Known(match)));
        Assert.Equal(match.Data.Ai.Beliefs.AbandonedLoot, memory.ExpectedBarbarianLoot(Known(match)));
    }

    private static void ScoutedByHand(AiMatch match, int garrison, double stock) =>
        match.State.AddScoutReport(new ScoutReport(
            99, match.State.ElapsedTime, Ai, Village.BarbarianOwnerId, Ai, Quitter,
            new Dictionary<UnitType, int> { [UnitType.Cavalry] = 1 }, true,
            new Dictionary<UnitType, int> { [UnitType.Spearman] = garrison }, 0, new Resources(stock, 0, 0)));

    [Fact]
    public void NoRegrowthIsAssumed()
    {
        var match = Abandoned();
        var memory = new AiMemory(match.Views[Ai]);
        ScoutedByHand(match, 4, 100);
        memory.Update();
        var defence = memory.EstimateDefence(Known(match));
        var loot = memory.ExpectedBarbarianLoot(Known(match));

        match.State.ElapsedTime += 3000;

        Assert.Equal(defence, memory.EstimateDefence(Known(match)), 6);
        Assert.Equal(loot, memory.ExpectedBarbarianLoot(Known(match)), 6);
        Assert.Equal(100, loot, 6);
    }

    [Fact]
    public void ItIsScoutedBeforeItIsRaided()
    {
        var match = Abandoned();
        match.State.Villages[Ai].AddTroops(UnitType.Cavalry, 100);

        var firstRaid = double.PositiveInfinity;
        var firstScout = double.PositiveInfinity;
        for (var i = 0; i < 400 && double.IsInfinity(firstRaid); i++)
        {
            match.Run(5);
            foreach (var movement in match.State.Movements.Where(m => m.OwnerId == Ai && m.ToVillageId == Quitter))
            {
                if (movement.Type == MovementType.Scout)
                    firstScout = Math.Min(firstScout, movement.DepartureTime);
                if (movement.Type == MovementType.Raid)
                    firstRaid = Math.Min(firstRaid, movement.DepartureTime);
            }
        }

        Assert.True(firstScout < firstRaid, $"scout {firstScout}, raid {firstRaid}");
    }

    [Fact]
    public void AWholeMatchWithASurrenderRunsCleanly()
    {
        var match = new AiMatch(new[] { AiDifficulty.Hard, AiDifficulty.Normal, AiDifficulty.Normal }, minutes: 30);
        match.Run(300);
        match.Session.SendAs(2, new SurrenderCommand());

        match.Run(1500);

        Assert.Equal(0, match.RejectedCommands);
    }
}
