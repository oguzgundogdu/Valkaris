using Valkaris.Ai;
using Valkaris.Core.Commands;
using Valkaris.Core.Model;

namespace Valkaris.Core.Tests;

public class AiDefenceTests
{
    private const int Ai = 1;
    private const int Attacker = 2;

    private static AiMatch Match(AiDifficulty difficulty) =>
        new(new[] { difficulty, AiDifficulty.Normal }, idle: new[] { Attacker });

    /// <summary>For measuring training alone: fighting would kill some of what is being counted.</summary>
    private static AiMatch MatchWithoutRaids(AiDifficulty difficulty) =>
        new(new[] { difficulty, AiDifficulty.Normal }, idle: new[] { Attacker }, data: TestData.Mutated("ai.json", root =>
        {
            foreach (var level in Enum.GetNames<AiDifficulty>())
            {
                root["difficulties"]![level]!["maxRaidsPerVillage"] = 0;
                root["difficulties"]![level]!["aggressionDelaySeconds"] = 1000000;
                root["difficulties"]![level]!.AsObject().Remove("maxFightersPerMinute");
            }
        }));

    private static void SendAttack(AiMatch match, int count = 5)
    {
        var enemy = match.State.Villages[Attacker];
        enemy.AddTroops(UnitType.Swordsman, count);
        var result = match.Session.SendAs(Attacker, new SendTroopsCommand(Attacker, Ai, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = count }));
        Assert.True(result.Success);
    }

    private static Village Second(AiMatch match)
    {
        var camp = match.State.Villages.Values.Where(v => v.IsBarbarian).OrderBy(v => v.Id).First();
        camp.OwnerId = Ai;
        camp.BarbarianTier = null;
        camp.NextTroopRegenTime = null;
        foreach (var (unit, count) in camp.Troops.ToList())
            camp.RemoveTroops(unit, count);
        return camp;
    }

    private static int Trained(Village village, UnitType unit) =>
        village.TroopCount(unit) + village.TrainingQueue.Where(q => q.Unit == unit).Sum(q => q.Count);

    [Fact]
    public void AnAiTrainsTroopsOnceItHasABarracks()
    {
        var match = Match(AiDifficulty.Normal);

        match.Run(1200);

        var village = match.State.Villages[Ai];
        Assert.True(Trained(village, UnitType.Swordsman) + Trained(village, UnitType.Spearman) > 10);
        Assert.Equal(0, match.RejectedCommands);
    }

    [Theory]
    [InlineData(AiDifficulty.Easy)]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void TheGarrisonShareFollowsTheProfile(AiDifficulty difficulty)
    {
        var match = MatchWithoutRaids(difficulty);
        var village = match.State.Villages[Ai];
        village.SetBuildingLevel(BuildingType.Barracks, 3);
        village.SetBuildingLevel(BuildingType.Warehouse, 8);
        foreach (var producer in new[] { BuildingType.Woodcutter, BuildingType.Quarry, BuildingType.Farm })
            village.SetBuildingLevel(producer, 8);
        match.Run(1800);

        var spears = Trained(village, UnitType.Spearman);
        var fighters = spears + Trained(village, UnitType.Swordsman) + Trained(village, UnitType.Cavalry);
        var share = (double)spears / fighters;

        Assert.InRange(share, match.Data.Ai.Difficulties[difficulty].GarrisonShare - 0.1, match.Data.Ai.Difficulties[difficulty].GarrisonShare + 0.15);
    }

    [Fact]
    public void CavalryFollowsItsRatioOnceUnlocked()
    {
        var match = MatchWithoutRaids(AiDifficulty.Hard);
        var village = match.State.Villages[Ai];
        village.SetBuildingLevel(BuildingType.Barracks, 3);
        village.SetBuildingLevel(BuildingType.Warehouse, 8);

        match.Run(1800);

        Assert.True(Trained(village, UnitType.Cavalry) > 0);
    }

    [Fact]
    public void AnAttackMakesItTrainDefenders()
    {
        var match = Match(AiDifficulty.Normal);
        var village = match.State.Villages[Ai];
        village.SetBuildingLevel(BuildingType.Barracks, 3);
        village.AddTroops(UnitType.Spearman, 200);
        village.AddTroops(UnitType.Swordsman, 1);
        village.Resources = new Resources(900, 900, 900);
        SendAttack(match);

        match.Session.Tick(31);

        Assert.Contains(village.TrainingQueue, q => q.Unit == UnitType.Spearman);
    }

    [Fact]
    public void AnAttackPutsTheWallFirst()
    {
        var match = Match(AiDifficulty.Normal);
        var village = match.State.Villages[Ai];
        village.Resources = new Resources(900, 900, 900);
        SendAttack(match);

        match.Session.Tick(31);

        Assert.Equal(BuildingType.Wall, village.BuildQueue[0].Building);
    }

    [Fact]
    public void TheNextBuildIsLeftAffordableWhenSafe()
    {
        var match = Match(AiDifficulty.Normal);
        var village = match.State.Villages[Ai];
        village.SetBuildingLevel(BuildingType.Barracks, 3);
        village.SetBuildingLevel(BuildingType.Woodcutter, 9);
        village.Resources = new Resources(10, 10, 10);

        match.Session.Tick(31);

        Assert.Empty(village.TrainingQueue);
    }

    [Fact]
    public void AProfileThatReinforcesSendsHelpInTime()
    {
        var match = Match(AiDifficulty.Hard);
        var second = Second(match);
        second.AddTroops(UnitType.Cavalry, 40);
        SendAttack(match);
        var arrival = match.State.Movements.Single().ArrivalTime;

        match.Session.Tick(10);

        var help = match.State.Movements.Single(m => m.Type == MovementType.Reinforce);
        Assert.Equal(Ai, help.ToVillageId);
        Assert.Equal(20, help.Troops[UnitType.Cavalry]);
        Assert.True(help.ArrivalTime < arrival);
    }

    [Fact]
    public void AProfileThatDoesNotReinforceKeepsItsTroops()
    {
        var match = Match(AiDifficulty.Easy);
        var second = Second(match);
        second.AddTroops(UnitType.Cavalry, 40);
        SendAttack(match);

        match.Session.Tick(60);

        Assert.DoesNotContain(match.State.Movements, m => m.Type == MovementType.Reinforce);
    }

    [Fact]
    public void HelpThatWouldArriveTooLateStaysHome()
    {
        var match = Match(AiDifficulty.Hard);
        var second = match.State.Villages.Values.Where(v => v.IsBarbarian)
            .MaxBy(v => Valkaris.Core.Systems.UnitRules.Distance(v.Position, match.State.Villages[Ai].Position))!;
        second.OwnerId = Ai;
        second.BarbarianTier = null;
        second.NextTroopRegenTime = null;
        foreach (var (unit, count) in second.Troops.ToList())
            second.RemoveTroops(unit, count);
        second.AddTroops(UnitType.Spearman, 40);

        // The attack is seconds away, so slow Spearmen can't beat it.
        var cavalry = new Dictionary<UnitType, int> { [UnitType.Cavalry] = 1 };
        match.State.Villages[Attacker].AddTroops(UnitType.Cavalry, 1);
        var near = match.State.Villages.Values.Where(v => v.OwnerId == Attacker).First();
        Assert.True(match.Session.SendAs(Attacker, new SendTroopsCommand(near.Id, Ai, MovementType.Attack, cavalry)).Success);

        match.Session.Tick(31);

        Assert.DoesNotContain(match.State.Movements, m => m.Type == MovementType.Reinforce);
    }

    [Fact]
    public void OneAttackIsAnsweredOnce()
    {
        var match = MatchWithoutRaids(AiDifficulty.Hard);
        var second = Second(match);
        second.AddTroops(UnitType.Cavalry, 40);
        SendAttack(match);

        match.Session.Tick(60);

        Assert.Equal(20, second.TroopCount(UnitType.Cavalry));
    }

    // How much a level is willing to spend on defence.

    [Fact]
    public void EasyKeepsTheSmallestGarrisonShareAndTheLowestWall()
    {
        var profiles = TestData.Shipped.Ai.Difficulties;

        Assert.True(profiles[AiDifficulty.Easy].GarrisonShare < profiles[AiDifficulty.Normal].GarrisonShare);
        Assert.True(profiles[AiDifficulty.Easy].MaxWallLevel < profiles[AiDifficulty.Normal].MaxWallLevel);
        Assert.NotNull(profiles[AiDifficulty.Easy].MaxFightersPerMinute);
    }

    private static AiMatch MatchWithWallLimit(int limit) =>
        new(new[] { AiDifficulty.Normal, AiDifficulty.Normal }, idle: new[] { Attacker }, data: TestData.Mutated("ai.json", root =>
            root["difficulties"]!["Normal"]!["maxWallLevel"] = limit));

    [Fact]
    public void TheBuildOrderStopsAtTheWallLimit()
    {
        var match = MatchWithWallLimit(1);
        var village = match.State.Villages[Ai];
        foreach (var type in Enum.GetValues<BuildingType>().Where(b => b != BuildingType.Wall))
            village.SetBuildingLevel(type, 10);
        village.SetBuildingLevel(BuildingType.Wall, 1);
        village.Resources = new Resources(5000, 5000, 5000);

        var planner = new BuildPlanner(match.Views[Ai]);
        for (var i = 0; i < 5; i++)
            planner.Plan(village);

        Assert.DoesNotContain(village.BuildQueue, q => q.Building == BuildingType.Wall);
    }

    [Fact]
    public void AnAttackDoesNotPushTheWallPastTheLimit()
    {
        var match = MatchWithWallLimit(0);
        var village = match.State.Villages[Ai];
        village.Resources = new Resources(800, 800, 800);

        new BuildPlanner(match.Views[Ai]).Plan(village, threatened: true);

        Assert.DoesNotContain(village.BuildQueue, q => q.Building == BuildingType.Wall);
    }

    [Fact]
    public void AnAttackStillRaisesTheWallBelowTheLimit()
    {
        var match = MatchWithWallLimit(5);
        var village = match.State.Villages[Ai];
        village.Resources = new Resources(800, 800, 800);

        new BuildPlanner(match.Views[Ai]).Plan(village, threatened: true);

        Assert.Contains(village.BuildQueue, q => q.Building == BuildingType.Wall);
    }

    private static int FightersAfterTraining(double? limit, bool threatened)
    {
        var match = new AiMatch(new[] { AiDifficulty.Normal, AiDifficulty.Normal }, idle: new[] { Attacker }, data: TestData.Mutated("ai.json", root =>
        {
            root["difficulties"]!["Normal"]!["fightersPerMinute"] = 0;
            if (limit is { } value)
                root["difficulties"]!["Normal"]!["maxFightersPerMinute"] = value;
        }));
        var village = match.State.Villages[Ai];
        village.SetBuildingLevel(BuildingType.Barracks, 3);
        village.SetBuildingLevel(BuildingType.Warehouse, 8);
        village.Resources = new Resources(5000, 5000, 5000);
        foreach (var (unit, count) in village.Troops.ToList())
            village.RemoveTroops(unit, count);
        village.AddTroops(UnitType.Scout, 10);

        new TrainingPlanner(match.Views[Ai], new AiMemory(match.Views[Ai])).Plan(village, threatened, Resources.Zero);
        return village.TrainingQueue.Sum(q => q.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtTheArmyLimitNoFightersAreTrained(bool threatened) =>
        Assert.Equal(0, FightersAfterTraining(0, threatened));

    [Fact]
    public void WithNoLimitFightersAreTrained() => Assert.True(FightersAfterTraining(null, false) > 0);

    [Fact]
    public void WithFewSpearmenSwordsmenMakeUpTheGarrison()
    {
        var match = Match(AiDifficulty.Normal);
        var village = match.State.Villages[Ai];
        foreach (var (unit, count) in village.Troops.ToList())
            village.RemoveTroops(unit, count);
        village.AddTroops(UnitType.Swordsman, 100);

        var spare = new Forces(match.Views[Ai]).Spare(village);

        var kept = 100 - spare[UnitType.Swordsman];
        Assert.Equal((int)Math.Ceiling(100 * match.Data.Ai.Difficulties[AiDifficulty.Normal].GarrisonShare), kept);
    }

    [Fact]
    public void WithEnoughSpearmenAllSwordsmenAreSpare()
    {
        var match = Match(AiDifficulty.Normal);
        var village = match.State.Villages[Ai];
        foreach (var (unit, count) in village.Troops.ToList())
            village.RemoveTroops(unit, count);
        village.AddTroops(UnitType.Spearman, 100);
        village.AddTroops(UnitType.Swordsman, 10);

        Assert.Equal(10, new Forces(match.Views[Ai]).Spare(village)[UnitType.Swordsman]);
    }
}
