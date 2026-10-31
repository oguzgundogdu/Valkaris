using Valkaris.Core.Model;

namespace Valkaris.Core.Tests;

public class AiScoutTests
{
    private const int Ai = 1;
    private const int Enemy = 2;

    private static AiMatch Match(AiDifficulty difficulty = AiDifficulty.Hard, Valkaris.Core.Data.GameData? data = null) =>
        new(new[] { difficulty, AiDifficulty.Normal }, idle: new[] { Enemy }, data: data);

    private static void PastDelay(AiMatch match, AiDifficulty difficulty = AiDifficulty.Hard) =>
        match.Run(match.Data.Ai.Difficulties[difficulty].AggressionDelaySeconds + 1, step: 50);

    /// <summary>Leaves the enemy as the only target, by handing the AI every camp.</summary>
    private static void NoCamps(AiMatch match)
    {
        foreach (var camp in match.State.Villages.Values.Where(v => v.IsBarbarian))
        {
            camp.OwnerId = Ai;
            camp.BarbarianTier = null;
            camp.NextTroopRegenTime = null;
            foreach (var (unit, count) in camp.Troops.ToList())
                camp.RemoveTroops(unit, count);
        }
    }

    private static void SetGarrison(Village village, UnitType unit, int count)
    {
        foreach (var (present, amount) in village.Troops.ToList())
            village.RemoveTroops(present, amount);
        village.AddTroops(unit, count);
    }

    private static Movement? Scouts(AiMatch match) =>
        match.State.Movements.FirstOrDefault(m => m.OwnerId == Ai && m.Type == MovementType.Scout);

    private static Movement? Attack(AiMatch match) =>
        match.State.Movements.FirstOrDefault(m => m.OwnerId == Ai && m.Type == MovementType.Attack);

    private static AiMatch ReadyToScout(int enemyTroops = 5, int cavalry = 300)
    {
        var match = Match();
        PastDelay(match);
        NoCamps(match);
        SetGarrison(match.State.Villages[Enemy], UnitType.Spearman, enemyTroops);
        match.State.Villages[Ai].AddTroops(UnitType.Cavalry, cavalry);
        return match;
    }

    private static void RunUntil(AiMatch match, Func<bool> done, double limit = 3000)
    {
        var end = match.State.ElapsedTime + limit;
        while (!done() && match.State.ElapsedTime < end && !match.State.IsMatchOver)
            match.Run(5);
    }

    [Fact]
    public void ItDoesNotScoutBeforeTheAggressionDelay()
    {
        var match = Match();
        match.State.Villages[Ai].AddTroops(UnitType.Cavalry, 100);

        match.Run(match.Data.Ai.Difficulties[AiDifficulty.Hard].AggressionDelaySeconds - 10);

        Assert.Null(Scouts(match));
        Assert.Empty(match.State.ScoutReports);
    }

    [Fact]
    public void AnUnknownTargetIsScoutedBeforeItIsAttacked()
    {
        var match = ReadyToScout();

        RunUntil(match, () => Scouts(match) is not null || Attack(match) is not null);

        Assert.NotNull(Scouts(match));
        Assert.Equal(Enemy, Scouts(match)!.ToVillageId);
        Assert.Null(Attack(match));
    }

    [Fact]
    public void TheArmyWaitsForTheNews()
    {
        var match = ReadyToScout();
        RunUntil(match, () => Scouts(match) is not null);

        var arrival = Scouts(match)!.ArrivalTime;
        while (match.State.ElapsedTime < arrival - 10)
        {
            match.Run(5);
            Assert.Null(Attack(match));
        }
    }

    [Fact]
    public void WithFreshNewsItAttacks()
    {
        var match = ReadyToScout();

        RunUntil(match, () => Attack(match) is not null || match.State.Reports.Any(r => r.AttackerId == Ai && r.DefenderId == Enemy));

        Assert.NotEmpty(match.State.ScoutReports);
        Assert.True(match.State.ScoutReports[0].Succeeded);
    }

    [Fact]
    public void ScoutsAreOnlyAsManyAsTheEstimateNeeds()
    {
        var match = ReadyToScout();

        RunUntil(match, () => Scouts(match) is not null);

        Assert.True(Scouts(match)!.Troops.Values.Sum() < 50);
    }

    [Fact]
    public void TheFastestTroopsGo()
    {
        var match = Match();
        match.State.Villages[Ai].AddTroops(UnitType.Swordsman, 300);
        match.State.Villages[Ai].AddTroops(UnitType.Cavalry, 300);

        RunUntil(match, () => Scouts(match) is not null, limit: 3000);

        Assert.Equal(new[] { UnitType.Cavalry }, Scouts(match)!.Troops.Keys);
    }

    [Fact]
    public void CaughtScoutsAreFollowedByMoreAfterTheWait()
    {
        // Enough to scout with, but too few to attack on the estimate instead.
        var match = ReadyToScout(enemyTroops: 5000, cavalry: 8);
        RunUntil(match, () => match.State.ScoutReports.Count > 0);
        var first = match.State.ScoutReports[0];
        Assert.False(first.Succeeded);

        RunUntil(match, () => match.State.ScoutReports.Count > 1 || Scouts(match) is not null);

        var second = Scouts(match) ?? throw new Xunit.Sdk.XunitException("No second scouting.");
        var wait = (second.DepartureTime - first.Time) * match.State.Settings.SpeedMultiplier;
        Assert.True(wait >= match.Data.Ai.ScoutRetrySeconds);
        Assert.True(second.Troops.Values.Sum() >= first.Sent.Values.Sum() * 2);
    }

    [Fact]
    public void WithNoTroopsToSpareItNeverStalls()
    {
        var match = Match();
        PastDelay(match);
        NoCamps(match);
        var village = match.State.Villages[Ai];
        SetGarrison(village, UnitType.Spearman, 1);

        match.Run(600);

        Assert.DoesNotContain(match.State.ScoutReports, r => r.SenderId == Ai && r.Sent.ContainsKey(UnitType.Spearman) && village.TroopCount(UnitType.Spearman) == 0);
        Assert.Equal(0, match.RejectedCommands);
    }

    [Fact]
    public void AProfilesAgeLimitSetsHowOftenItLooksAgain()
    {
        int ScoutingsWith(double maxAge)
        {
            var data = TestData.Mutated("ai.json", r => r["difficulties"]!["Hard"]!["scoutMaxAgeSeconds"] = maxAge);
            var match = Match(data: data);
            PastDelay(match);
            NoCamps(match);
            SetGarrison(match.State.Villages[Enemy], UnitType.Spearman, match.Data.Combat.TroopsPerScout);
            match.State.Villages[Enemy].SetBuildingLevel(BuildingType.Wall, 10);
            match.State.Villages[Ai].AddTroops(UnitType.Cavalry, 3);
            match.Run(1500);
            return match.State.ScoutReports.Count(r => r.SenderId == Ai);
        }

        Assert.True(ScoutingsWith(60) > ScoutingsWith(100000));
    }

    [Fact]
    public void TheAiCommandsAreAccepted()
    {
        var match = ReadyToScout();

        match.Run(600);

        Assert.Equal(0, match.RejectedCommands);
    }
}
