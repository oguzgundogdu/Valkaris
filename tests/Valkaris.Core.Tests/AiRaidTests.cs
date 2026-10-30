using Valkaris.Core.Commands;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class AiRaidTests
{
    private const int Ai = 1;

    private static AiMatch Match(AiDifficulty difficulty = AiDifficulty.Normal) =>
        new(new[] { difficulty, AiDifficulty.Normal }, idle: new[] { 2 });

    private static double AttackOf(AiMatch match, Movement movement) =>
        movement.Troops.Sum(t => t.Value * match.Data.Units.Units[t.Key].Attack);

    private static double Belief(AiMatch match, Village target) =>
        match.Data.Ai.Beliefs.BarbarianPowerByTier[match.Data.Map.Barbarians.Tiers[target.BarbarianTier!.Value].Name];

    private static Movement? FirstRaid(AiMatch match) => match.State.Movements.FirstOrDefault(m => m.OwnerId == Ai && m.Type == MovementType.Raid);

    [Theory]
    [InlineData(AiDifficulty.Easy)]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void ARaidBeatsItsEstimateByTheProfilesMargin(AiDifficulty difficulty)
    {
        var match = Match(difficulty);
        match.State.Villages[Ai].AddTroops(UnitType.Swordsman, 100);

        match.Session.Tick(match.Data.Ai.Difficulties[difficulty].DecisionSeconds * 2);

        var raid = FirstRaid(match);
        Assert.NotNull(raid);
        var target = match.State.Villages[raid!.ToVillageId!.Value];
        Assert.True(AttackOf(match, raid) >= Belief(match, target) * match.Data.Ai.Difficulties[difficulty].RaidMargin);
    }

    [Fact]
    public void TooFewTroopsStayHome()
    {
        var match = Match();
        match.State.Villages[Ai].AddTroops(UnitType.Swordsman, 1);

        match.Session.Tick(31);

        Assert.Null(FirstRaid(match));
    }

    [Fact]
    public void TheGarrisonStaysHome()
    {
        var match = Match();
        var village = match.State.Villages[Ai];
        village.RemoveTroops(UnitType.Spearman, village.TroopCount(UnitType.Spearman));
        village.AddTroops(UnitType.Spearman, 100);

        match.Session.Tick(31);

        var share = match.Data.Ai.Difficulties[AiDifficulty.Normal].GarrisonShare;
        Assert.True(village.TroopCount(UnitType.Spearman) >= 100 * share);
    }

    [Fact]
    public void ReportsFromRaidsSteerTheNextChoice()
    {
        var match = Match();
        var village = match.State.Villages[Ai];
        village.AddTroops(UnitType.Swordsman, 200);

        match.Run(600);

        Assert.NotEmpty(match.State.Reports);
        Assert.Equal(0, match.RejectedCommands);
    }

    [Fact]
    public void LootReachesTheEconomy()
    {
        var match = Match();
        match.State.Villages[Ai].AddTroops(UnitType.Swordsman, 100);

        match.Run(900);

        Assert.Contains(match.State.Reports, r => r.AttackerId == Ai && r.AttackerWon && r.Loot.Wood + r.Loot.Stone + r.Loot.Food > 0);
    }

    [Fact]
    public void AnEmptiedTargetIsLeftAloneDuringTheCooldown()
    {
        var match = Match();
        match.State.Villages[Ai].AddTroops(UnitType.Swordsman, 300);
        var raided = new Dictionary<int, List<double>>();

        match.Run(600, step: 1);

        foreach (var group in match.State.Reports.Where(r => r.AttackerId == Ai).GroupBy(r => r.DefenderVillageId))
        {
            var times = group.Select(r => r.Time).OrderBy(t => t).ToList();
            for (var i = 1; i < times.Count; i++)
                Assert.True(times[i] - times[i - 1] >= match.Data.Ai.Difficulties[AiDifficulty.Normal].RaidCooldownSeconds, $"Raided {times[i] - times[i - 1]}s apart");
        }
    }

    [Fact]
    public void ATargetFoundToBeTooStrongIsNotRaidedAgain()
    {
        var match = Match();
        var village = match.State.Villages[Ai];
        foreach (var camp in match.State.Villages.Values.Where(v => v.IsBarbarian))
            camp.AddTroops(UnitType.Spearman, 5000);
        village.AddTroops(UnitType.Swordsman, 30);

        match.Run(900, step: 1);

        // A first probe is lost to each target at most, because the belief was wrong. Then the report corrects it.
        foreach (var group in match.State.Reports.Where(r => r.AttackerId == Ai).GroupBy(r => r.DefenderVillageId))
            Assert.Single(group);
        Assert.Contains(match.State.Reports, r => r.AttackerId == Ai && !r.AttackerWon);
    }

    [Fact]
    public void AnAttackedVillageDoesNotRaid()
    {
        var match = Match();
        match.State.Villages[Ai].AddTroops(UnitType.Swordsman, 100);
        match.State.Villages[2].AddTroops(UnitType.Spearman, 1);
        Assert.True(match.Session.SendAs(2, new SendTroopsCommand(2, Ai, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Spearman] = 1 })).Success);

        match.Session.Tick(31);

        Assert.Null(FirstRaid(match));
    }

    [Fact]
    public void TheRaidLimitHolds()
    {
        var match = Match(AiDifficulty.Easy);
        var village = match.State.Villages[Ai];
        village.AddTroops(UnitType.Swordsman, 500);

        match.Session.Tick(200);

        Assert.True(match.State.Movements.Count(m => m.OwnerId == Ai && m.Type == MovementType.Raid) <= match.Data.Ai.Difficulties[AiDifficulty.Easy].MaxRaidsPerVillage);
    }
}
