using Valkaris.Ai;
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

    private static AiMatch ReadyToScout(int enemyTroops = 5, int cavalry = 300, int scouts = 30)
    {
        var match = Match();
        PastDelay(match);
        NoCamps(match);
        SetGarrison(match.State.Villages[Enemy], UnitType.Spearman, enemyTroops);
        match.State.Villages[Ai].AddTroops(UnitType.Cavalry, cavalry);
        match.State.Villages[Ai].AddTroops(UnitType.Scout, scouts);
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
    public void OnlyScoutsGoAndItTrainsThemItself()
    {
        var match = Match();
        match.State.Villages[Ai].AddTroops(UnitType.Swordsman, 300);
        match.State.Villages[Ai].AddTroops(UnitType.Cavalry, 300);

        RunUntil(match, () => Scouts(match) is not null, limit: 3000);

        Assert.Equal(new[] { UnitType.Scout }, Scouts(match)!.Troops.Keys);
    }

    [Fact]
    public void CaughtScoutsAreFollowedByMoreAfterTheWait()
    {
        var match = ReadyToScout(enemyTroops: 5000, cavalry: 8, scouts: 8);
        match.State.Villages[Enemy].AddTroops(UnitType.Scout, 2000);
        RunUntil(match, () => match.State.ScoutReports.Count > 0);
        var first = match.State.ScoutReports[0];
        Assert.False(first.Succeeded);

        RunUntil(match, () => match.State.ScoutReports.Count > 1 || Scouts(match) is not null);

        var second = Scouts(match) ?? throw new Xunit.Sdk.XunitException("No second scouting.");
        var wait = (second.DepartureTime - first.Time) * match.State.Settings.SpeedMultiplier;
        Assert.True(wait >= match.Data.Ai.ScoutRetrySeconds, $"waited {wait}");
        Assert.True(second.Troops.Values.Sum() >= first.Sent.Values.Sum() * 2, $"{second.Troops.Values.Sum()} after {first.Sent.Values.Sum()}");
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
            SetGarrison(match.State.Villages[Enemy], UnitType.Spearman, 5);
            match.State.Villages[Enemy].SetBuildingLevel(BuildingType.Wall, 10);
            match.State.Villages[Ai].AddTroops(UnitType.Scout, 3);
            match.Run(1500);
            return match.State.ScoutReports.Count(r => r.SenderId == Ai);
        }

        Assert.True(ScoutingsWith(60) > ScoutingsWith(100000));
    }

    [Fact]
    public void ItSendsEnoughToBeatWhatItCountedByItsMargin()
    {
        var match = ReadyToScout();
        var enemy = match.State.Villages[Enemy];
        match.State.AddScoutReport(new ScoutReport(
            1000, match.State.ElapsedTime, Ai, Enemy, Ai, Enemy,
            new Dictionary<UnitType, int> { [UnitType.Scout] = 1 }, true,
            new Dictionary<UnitType, int> { [UnitType.Scout] = 4 }, 0, Resources.Zero,
            Quality: ScoutQuality.Overwhelming,
            Buildings: new Dictionary<BuildingType, int> { [BuildingType.Watchtower] = 5 },
            Loyalty: 100));
        var view = match.Views[Ai];
        var memory = new AiMemory(view);
        memory.Update();

        var known = view.KnownVillages.First(v => v.Id == enemy.Id);

        Assert.Equal(4 * (1 + 5 * match.Data.Combat.Scouting.WatchtowerBonusPerLevel), memory.ExpectedScoutStrength(known), 6);
    }

    [Fact]
    public void CampsAreExpectedToHaveNoScouts()
    {
        var match = Match();
        var view = match.Views[Ai];
        var camp = view.KnownVillages.First(v => v.BarbarianTier is not null);

        Assert.Equal(0, new AiMemory(view).ExpectedScoutStrength(camp));
    }

    [Fact]
    public void PlayersAreExpectedToHaveMoreScoutsAsTimeGoesOn()
    {
        var match = Match();
        var view = match.Views[Ai];
        var memory = new AiMemory(view);
        var enemy = view.KnownVillages.First(v => v.Id == Enemy);
        var early = memory.ExpectedScoutStrength(enemy);

        match.Run(1200);

        Assert.True(memory.ExpectedScoutStrength(enemy) > early);
    }

    [Fact]
    public void ForceSizeStopsAtTheProfilesLimit()
    {
        var match = ReadyToScout(enemyTroops: 5, cavalry: 0, scouts: 500);
        match.State.Villages[Enemy].AddTroops(UnitType.Scout, 5000);

        match.Run(3000);

        var limit = match.Data.Ai.Difficulties[AiDifficulty.Hard].MaxScoutForce;
        Assert.All(match.State.ScoutReports.Where(r => r.SenderId == Ai), r => Assert.True(r.Sent.Values.Sum() <= limit));
        Assert.Contains(match.State.ScoutReports, r => r.SenderId == Ai && !r.Succeeded);
    }

    [Theory]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void ItBuildsAWatchtowerWithinItsProfile(AiDifficulty difficulty)
    {
        var match = new AiMatch(new[] { difficulty, AiDifficulty.Normal }, idle: new[] { Enemy });

        match.Run(1800);

        var limit = match.Data.Ai.Difficulties[difficulty].MaxWatchtowerLevel;
        var levels = match.State.Villages.Values.Where(v => v.OwnerId == Ai).Select(v => v.BuildingLevel(BuildingType.Watchtower)).ToList();
        Assert.Contains(levels, l => l > 0);
        Assert.All(levels, l => Assert.True(l <= limit));
    }

    [Fact]
    public void EasyBuildsNoWatchtowerBecauseItsDataSaysSo()
    {
        var match = new AiMatch(new[] { AiDifficulty.Easy, AiDifficulty.Normal }, idle: new[] { Enemy });

        match.Run(1800);

        Assert.All(match.State.Villages.Values.Where(v => v.OwnerId == Ai), v => Assert.Equal(0, v.BuildingLevel(BuildingType.Watchtower)));
    }

    [Fact]
    public void ItKeepsItsHomeScoutsOutOfMissions()
    {
        var data = TestData.Mutated("ai.json", r =>
        {
            r["difficulties"]!["Hard"]!["homeScoutsAtStart"] = 3;
            r["difficulties"]!["Hard"]!["homeScoutsPerMinute"] = 0;
            r["difficulties"]!["Hard"]!["maxHomeScouts"] = 3;
        });
        var match = new AiMatch(new[] { AiDifficulty.Hard, AiDifficulty.Normal }, idle: new[] { Enemy }, data: data);
        var village = match.State.Villages[Ai];

        for (var time = 0; time < 900 && Scouts(match) is null; time += 5)
            match.Run(5);

        Assert.NotNull(Scouts(match));
        Assert.True(village.TroopCount(UnitType.Scout) >= 3, $"{village.TroopCount(UnitType.Scout)} at home");
    }

    [Fact]
    public void TheAiCommandsAreAccepted()
    {
        var match = ReadyToScout();

        match.Run(600);

        Assert.Equal(0, match.RejectedCommands);
    }
}
