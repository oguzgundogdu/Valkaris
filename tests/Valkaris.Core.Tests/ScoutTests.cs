using System.Text.Json.Nodes;
using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class ScoutTests
{
    private const int Sender = 1;
    private const int Defender = 2;

    private static readonly GameData Data = TestData.Shipped;

    private readonly GameState _state = TestMatch.Create(duration: 36000);
    private readonly CommandHandler _handler;
    private readonly Simulation _simulation;
    private readonly Village _home;
    private readonly Village _target;

    public ScoutTests()
    {
        _handler = new CommandHandler(_state, Data);
        _simulation = new Simulation(_state, Data);
        _home = _state.Villages[Sender];
        _target = _state.Villages[Defender];
        SetTroops(_home, UnitType.Scout, 20);
        SetTroops(_target, UnitType.Spearman, 0);
    }

    private static void SetTroops(Village village, UnitType unit, int count)
    {
        foreach (var (present, amount) in village.Troops.ToList())
            village.RemoveTroops(present, amount);
        village.AddTroops(unit, count);
    }

    private static SendTroopsCommand Scout(int count, UnitType unit = UnitType.Scout, int to = Defender) =>
        new(Sender, to, MovementType.Scout, new Dictionary<UnitType, int> { [unit] = count });

    private List<GameEvent> SendAndArrive(int scouts)
    {
        Assert.True(_handler.Handle(Sender, Scout(scouts)).Success);
        return _simulation.AdvanceTo(_state.Movements.Single().ArrivalTime).ToList();
    }

    private ScoutReport Report => _state.ScoutReports.Single();

    /// <summary>A flat curve, so a test can force the outcome.</summary>
    private static GameData WithChance(double chance) => TestData.Mutated(GameData.CombatFile, root =>
        root["scouting"]!["successCurve"] = new JsonArray(new JsonObject { ["ratio"] = 0.0001, ["chance"] = chance }));

    private void Contest(GameData data, int scouts, int defenders, int watchtower = 0)
    {
        _target.AddTroops(UnitType.Scout, defenders);
        _target.SetBuildingLevel(BuildingType.Watchtower, watchtower);
        Assert.True(new CommandHandler(_state, data).Handle(Sender, Scout(scouts)).Success);
        new Simulation(_state, data).AdvanceTo(_state.Movements.Single().ArrivalTime);
    }

    [Fact]
    public void AnEmptyVillageIsAlwaysScouted()
    {
        var rng = _state.RngState;

        SendAndArrive(1);

        Assert.True(Report.Succeeded);
        Assert.Empty(Report.Garrison!);
        Assert.Equal(0, Report.ScoutsLost);
        Assert.Equal(rng, _state.RngState);
    }

    [Fact]
    public void AVillageWithAGarrisonButNoScoutsIsAlwaysScouted()
    {
        SetTroops(_target, UnitType.Spearman, 5000);

        SendAndArrive(1);

        Assert.True(Report.Succeeded);
    }

    [Fact]
    public void TheLargerForceUsuallyWinsAndTheCurveIsUsed()
    {
        Assert.Equal(0.97, ScoutRules.SuccessChance(Data, 5), 6);
        Assert.Equal(0.25, ScoutRules.SuccessChance(Data, 1), 6);
        Assert.Equal(0.97, ScoutRules.SuccessChance(Data, 50), 6);
        Assert.Equal((0.25 + 0.55) / 2, ScoutRules.SuccessChance(Data, 1.25), 6);
        Assert.Equal(0.05 / 2, ScoutRules.SuccessChance(Data, 0.25), 6);
        Assert.True(ScoutRules.SuccessChance(Data, 2) > 0.5);
    }

    [Theory]
    [InlineData(5, 1, 0, true, 300)]
    [InlineData(1, 1, 0, false, 300)]
    public void OutcomesFollowTheChanceAcrossSeeds(int scouts, int defenders, int tower, bool likely, int runs)
    {
        var wins = 0;
        for (var seed = 0; seed < runs; seed++)
        {
            var state = TestMatch.Create(duration: 36000, seed: (ulong)seed);
            state.Villages[Sender].AddTroops(UnitType.Scout, scouts);
            state.Villages[Defender].AddTroops(UnitType.Scout, defenders);
            state.Villages[Defender].SetBuildingLevel(BuildingType.Watchtower, tower);
            new CommandHandler(state, Data).Handle(Sender, Scout(scouts));
            new Simulation(state, Data).AdvanceTo(state.Movements.Single().ArrivalTime);
            wins += state.ScoutReports.Single().Succeeded ? 1 : 0;
        }

        Assert.True(likely ? wins > runs * 0.9 : wins < runs * 0.4, $"{wins} of {runs}");
    }

    [Fact]
    public void AWatchtowerMultipliesTheScoutsAtHome()
    {
        _target.AddTroops(UnitType.Scout, 10);
        Assert.Equal(10, ScoutRules.DefendingStrength(Data, _target), 6);

        _target.SetBuildingLevel(BuildingType.Watchtower, 3);

        Assert.Equal(10 * (1 + 3 * Data.Combat.Scouting.WatchtowerBonusPerLevel), ScoutRules.DefendingStrength(Data, _target), 6);
    }

    [Fact]
    public void AWatchtowerAloneStopsNobody()
    {
        _target.SetBuildingLevel(BuildingType.Watchtower, 10);

        SendAndArrive(1);

        Assert.True(Report.Succeeded);
    }

    [Fact]
    public void FiveAgainstOneKillsTheDefenderAndCostsAboutOne()
    {
        Contest(WithChance(1), 5, 1);

        Assert.True(Report.Succeeded);
        Assert.Equal(1, Report.ScoutsLost);
        Assert.Equal(0, _target.TroopCount(UnitType.Scout));
        Assert.Equal(4, _state.Movements.Single().Troops[UnitType.Scout]);
    }

    [Fact]
    public void OnSuccessBothLoseInProportionToTheRatio()
    {
        Contest(WithChance(1), 15, 5);

        Assert.Equal(4, Report.ScoutsLost);
        Assert.Equal(1, _target.TroopCount(UnitType.Scout));
    }

    [Fact]
    public void OnFailureAllAttackersDieAndTheDefenderLosesInProportion()
    {
        Contest(WithChance(0), 5, 15);

        Assert.False(Report.Succeeded);
        Assert.Equal(5, Report.ScoutsLost);
        Assert.Empty(_state.Movements);
        Assert.Equal(11, _target.TroopCount(UnitType.Scout));
    }

    [Fact]
    public void ASuccessAlwaysBringsSomeoneHome()
    {
        Contest(WithChance(1), 1, 50);

        Assert.True(Report.Succeeded);
        Assert.Equal(0, Report.ScoutsLost);
        Assert.Equal(1, _state.Movements.Single().Troops[UnitType.Scout]);
    }

    [Fact]
    public void WatchtowerStrengthCountsInTheLosses()
    {
        Contest(WithChance(1), 10, 5, watchtower: 5);

        Assert.Equal(5, Report.ScoutsLost);
        Assert.Equal(2, _target.TroopCount(UnitType.Scout));
    }

    [Fact]
    public void ScoutsAwayFromHomeDontDefend()
    {
        _target.AddTroops(UnitType.Scout, 5);
        new CommandHandler(_state, Data).Handle(Defender, new SendTroopsCommand(Defender, Sender, MovementType.Scout, new Dictionary<UnitType, int> { [UnitType.Scout] = 5 }));
        Assert.True(_handler.Handle(Sender, Scout(1)).Success);

        var mine = _state.Movements.Single(m => m.OwnerId == Sender);
        if (_state.Movements.Single(m => m.OwnerId == Defender).ArrivalTime < mine.ArrivalTime)
            throw new InvalidOperationException("The defenders must still be out.");
        _simulation.AdvanceTo(mine.ArrivalTime);

        Assert.True(_state.ScoutReports.Single(r => r.SenderId == Sender).Succeeded);
    }

    // Notices.

    private ScoutNotice Notice => _state.ScoutNotices.Single();

    [Fact]
    public void AFailureTellsTheDefenderWhoAndHowMany()
    {
        Contest(WithChance(0), 3, 9);

        Assert.True(Notice.Caught);
        Assert.Equal(Sender, Notice.SenderId);
        Assert.Equal(Defender, Notice.DefenderId);
        Assert.Equal(3, Notice.ScoutsSent);
        Assert.Equal(2, Notice.ScoutsLost);
    }

    [Fact]
    public void ASuccessWithoutAWatchtowerTellsTheDefenderNothing()
    {
        Contest(WithChance(1), 5, 3);

        Assert.Empty(_state.ScoutNotices);
    }

    [Fact]
    public void AnyWatchtowerNoticesASuccessButNotItsSize()
    {
        _target.SetBuildingLevel(BuildingType.Watchtower, 1);

        var events = SendAndArrive(5);

        Assert.False(Notice.Caught);
        Assert.Equal(Sender, Notice.SenderId);
        Assert.Null(Notice.ScoutsSent);
        Assert.Equal(Notice.Id, Assert.Single(events.OfType<ScoutNoticed>()).NoticeId);
    }

    [Fact]
    public void ATakenCampHasNobodyToTell()
    {
        var camp = _state.Villages.Values.First(v => v.IsBarbarian);
        camp.SetBuildingLevel(BuildingType.Watchtower, 1);

        Assert.True(_handler.Handle(Sender, Scout(1, to: camp.Id)).Success);
        _simulation.AdvanceTo(_state.Movements.Single().ArrivalTime);

        Assert.Empty(_state.ScoutNotices);
    }

    [Fact]
    public void CampsStartWithNoScoutsSoTheyAreAlwaysScouted()
    {
        Assert.All(Data.Map.Barbarians.Tiers, tier => Assert.Equal(0, tier.Garrison[UnitType.Scout]));
    }

    [Fact]
    public void ACampsScoutsComeBackAsItsOtherTroopsDo()
    {
        var data = TestData.Mutated(GameData.MapFile, root =>
        {
            foreach (var tier in root["barbarians"]!["tiers"]!.AsArray())
                tier!["garrison"]!["Scout"] = 3;
        });
        var state = TestMatch.Create(duration: 36000, data: data);
        var camp = state.Villages.Values.First(v => v.IsBarbarian);
        Assert.Equal(3, camp.TroopCount(UnitType.Scout));
        state.Villages[Sender].AddTroops(UnitType.Scout, 30);
        new CommandHandler(state, data).Handle(Sender, Scout(30, to: camp.Id));
        var simulation = new Simulation(state, data);
        simulation.AdvanceTo(state.Movements.Single().ArrivalTime);
        Assert.True(camp.TroopCount(UnitType.Scout) < 3);

        simulation.AdvanceTo(state.ElapsedTime + data.Map.Barbarians.TroopRegenSeconds * 10);

        Assert.Equal(3, camp.TroopCount(UnitType.Scout));
    }

    [Fact]
    public void AnAbandonedVillageKeepsItsScoutsAndTellsNobody()
    {
        _target.AddTroops(UnitType.Scout, 4);
        _target.OwnerId = Village.BarbarianOwnerId;
        _target.BarbarianTier = null;

        Contest(WithChance(0), 1, 0);

        Assert.False(Report.Succeeded);
        Assert.Empty(_state.ScoutNotices);
        Assert.True(_target.TroopCount(UnitType.Scout) > 0);
    }

    [Fact]
    public void SuccessReportsTheGarrisonWallAndStockAtArrival()
    {
        SetTroops(_target, UnitType.Spearman, 3);
        _target.SetBuildingLevel(BuildingType.Wall, 2);

        SendAndArrive(1);

        Assert.Equal(new Dictionary<UnitType, int> { [UnitType.Spearman] = 3 }, Report.Garrison);
        Assert.Equal(2, Report.WallLevel);
        Assert.Equal(_target.Resources, Report.Stock);
    }

    [Fact]
    public void FailureReportsNoIntel()
    {
        SetTroops(_target, UnitType.Spearman, 100);

        Contest(WithChance(0), 1, 5);

        Assert.Null(Report.Garrison);
        Assert.Null(Report.WallLevel);
        Assert.Null(Report.Stock);
    }

    [Fact]
    public void SuccessfulScoutsComeHomeWithNoLoot()
    {
        SendAndArrive(4);

        var trip = _state.Movements.Single();
        Assert.Equal(MovementType.Return, trip.Type);
        Assert.Equal(4, trip.Troops[UnitType.Scout]);
        Assert.Equal(Resources.Zero, trip.Loot);

        _simulation.AdvanceTo(trip.ArrivalTime);

        Assert.Equal(20, _home.TroopCount(UnitType.Scout));
    }

    [Fact]
    public void CaughtScoutsAllDie()
    {
        Contest(WithChance(0), 4, 5);

        Assert.Empty(_state.Movements);
        Assert.Equal(16, _home.TroopCount(UnitType.Scout));
    }

    [Fact]
    public void TheGarrisonIsUntouchedAndNoBattleIsFought()
    {
        SetTroops(_target, UnitType.Spearman, 10);
        _target.AddTroops(UnitType.Scout, 5);
        var stock = _target.Resources;

        Assert.True(_handler.Handle(Sender, Scout(3)).Success);
        _simulation.AdvanceTo(_state.Movements.Single().ArrivalTime);

        Assert.Equal(10, _target.TroopCount(UnitType.Spearman));
        Assert.Equal(Village.MaxLoyalty, _target.Loyalty, 3);
        Assert.Empty(_state.Reports);
        Assert.True(_target.Resources.Wood >= stock.Wood);
    }

    [Fact]
    public void ScoutingACampDoesNotStartItsRegrowth()
    {
        var camp = _state.Villages.Values.First(v => v.IsBarbarian);
        SetTroops(camp, UnitType.Spearman, 0);

        Assert.True(_handler.Handle(Sender, Scout(1, to: camp.Id)).Success);
        _simulation.AdvanceTo(_state.Movements.Single().ArrivalTime);

        Assert.True(Report.Succeeded);
        Assert.Equal(Village.BarbarianOwnerId, Report.DefenderId);
        Assert.Null(camp.NextTroopRegenTime);
    }

    // Report quality.

    [Theory]
    [InlineData(19, 10, ScoutQuality.Narrow)]
    [InlineData(20, 10, ScoutQuality.Clear)]
    [InlineData(39, 10, ScoutQuality.Clear)]
    [InlineData(40, 10, ScoutQuality.Overwhelming)]
    [InlineData(1, 0, ScoutQuality.Overwhelming)]
    public void TheMarginSetsTheQuality(int scouts, int defenders, ScoutQuality expected)
    {
        _home.AddTroops(UnitType.Scout, 40);

        Contest(WithChance(1), scouts, defenders);

        Assert.Equal(expected, Report.Quality);
    }

    [Fact]
    public void AWatchtowerLowersTheMargin()
    {
        _home.AddTroops(UnitType.Scout, 40);

        Contest(WithChance(1), 20, 5, watchtower: 5);

        Assert.Equal(ScoutQuality.Clear, Report.Quality);
    }

    [Fact]
    public void ANarrowSuccessGivesRangesAndOmitsResources()
    {
        SetTroops(_target, UnitType.Spearman, 30);
        _target.SetBuildingLevel(BuildingType.Wall, 2);

        Contest(WithChance(1), 1, 1);

        Assert.Null(Report.Garrison);
        Assert.Null(Report.WallLevel);
        Assert.Null(Report.Stock);
        Assert.Null(Report.Buildings);
        Assert.Null(Report.Loyalty);
        Assert.InRange(30, Report.TroopRange!.Low, Report.TroopRange.High);
        Assert.InRange(2, Report.WallRange!.Low, Report.WallRange.High);
    }

    [Fact]
    public void AClearSuccessGivesExactNumbersWithoutBuildingsOrLoyalty()
    {
        SetTroops(_target, UnitType.Spearman, 30);
        _target.SetBuildingLevel(BuildingType.Wall, 2);

        Contest(WithChance(1), 8, 3);

        Assert.Equal(30, Report.Garrison![UnitType.Spearman]);
        Assert.Equal(_target.TroopCount(UnitType.Scout), Report.Garrison[UnitType.Scout]);
        Assert.Equal(2, Report.WallLevel);
        Assert.Equal(_target.Resources, Report.Stock);
        Assert.Null(Report.TroopRange);
        Assert.Null(Report.WallRange);
        Assert.Null(Report.Buildings);
        Assert.Null(Report.Loyalty);
    }

    [Fact]
    public void AnOverwhelmingSuccessAlsoGivesBuildingsAndLoyalty()
    {
        _target.SetBuildingLevel(BuildingType.Wall, 2);
        _target.Loyalty = 77;

        SendAndArrive(1);

        Assert.Equal(2, Report.Buildings![BuildingType.Wall]);
        Assert.Equal(_target.BuildingLevel(BuildingType.MainBuilding), Report.Buildings[BuildingType.MainBuilding]);
        Assert.Equal(_target.Loyalty, Report.Loyalty!.Value, 6);
        Assert.NotNull(Report.Garrison);
    }

    [Fact]
    public void AFailureSeesNothingAtAll()
    {
        Contest(WithChance(0), 1, 5);

        Assert.Null(Report.Quality);
        Assert.Null(Report.TroopRange);
        Assert.Null(Report.WallRange);
    }

    [Fact]
    public void RangesAlwaysContainTheTruthAcrossValuesAndOffsets()
    {
        for (var value = 0; value <= 400; value++)
        {
            var width = ScoutRules.TroopRangeWidth(Data, value);
            for (var offset = 0; offset <= width; offset++)
            {
                var range = ScoutRules.Range(value, width, offset);
                Assert.InRange(value, range.Low, range.High);
                Assert.True(range.Low >= 0);
                Assert.Equal(width, range.High - range.Low);
            }
        }

        var maxWall = Data.Buildings.Buildings[BuildingType.Wall].MaxLevel;
        for (var wall = 0; wall <= maxWall; wall++)
        {
            foreach (var offset in new[] { 0, 1 })
            {
                var range = ScoutRules.Range(wall, 1, offset, maxWall);
                Assert.InRange(wall, range.Low, range.High);
                Assert.True(range.Low >= 0 && range.High <= maxWall && range.High > range.Low);
            }
        }
    }

    [Fact]
    public void WhereTheTruthSitsInTheRangeVaries()
    {
        var lows = new HashSet<int>();
        for (ulong seed = 0; seed < 40; seed++)
        {
            var state = TestMatch.Create(duration: 36000, seed: seed);
            var target = state.Villages[Defender];
            foreach (var (unit, count) in target.Troops.ToList())
                target.RemoveTroops(unit, count);
            target.AddTroops(UnitType.Spearman, 30);
            target.AddTroops(UnitType.Scout, 1);
            state.Villages[Sender].AddTroops(UnitType.Scout, 1);
            var data = WithChance(1);
            new CommandHandler(state, data).Handle(Sender, Scout(1));
            new Simulation(state, data).AdvanceTo(state.Movements.Single().ArrivalTime);
            lows.Add(state.ScoutReports.Single().TroopRange!.Low);
        }

        Assert.True(lows.Count > 3, $"{lows.Count} different ranges");
    }

    [Fact]
    public void AnEventNamesTheReport()
    {
        var events = SendAndArrive(1);

        Assert.Equal(Report.Id, Assert.Single(events.OfType<ScoutResolved>()).ReportId);
    }

    [Theory]
    [InlineData(UnitType.Spearman)]
    [InlineData(UnitType.Swordsman)]
    [InlineData(UnitType.Cavalry)]
    [InlineData(UnitType.Catapult)]
    [InlineData(UnitType.Settler)]
    [InlineData(UnitType.Lord)]
    public void OnlyScoutsCanScout(UnitType unit)
    {
        _home.AddTroops(unit, 1);

        Assert.Equal(CommandRejection.OnlyScoutsCanScout, _handler.Handle(Sender, Scout(1, unit)).Rejection);
        Assert.Empty(_state.Movements);
    }

    [Fact]
    public void AVillageOfYourOwnCannotBeScouted() =>
        Assert.Equal(CommandRejection.InvalidMission, _handler.Handle(Sender, Scout(1, to: Sender)).Rejection);

    [Fact]
    public void ScoutsArrivingAtAVillageTheirOwnerNowHoldsMergeIn()
    {
        Assert.True(_handler.Handle(Sender, Scout(2)).Success);
        _target.OwnerId = Sender;

        _simulation.AdvanceTo(_state.Movements.Single().ArrivalTime);

        Assert.Empty(_state.ScoutReports);
        Assert.Equal(2, _target.TroopCount(UnitType.Scout));
    }

    [Fact]
    public void ScoutsWhoseHomeWasLostGoToTheNearestVillage()
    {
        var other = _state.Villages.Values.First(v => v.IsBarbarian);
        other.OwnerId = Sender;
        other.BarbarianTier = null;
        other.NextTroopRegenTime = null;
        SetTroops(other, UnitType.Spearman, 0);
        SendAndArrive(2);
        _home.OwnerId = Defender;

        _simulation.AdvanceTo(_state.Movements.Single().ArrivalTime);
        _simulation.AdvanceTo(_state.Movements.Single().ArrivalTime);

        Assert.Empty(_state.Movements);
        Assert.Equal(2, other.TroopCount(UnitType.Scout));
    }

    [Fact]
    public void ReportsNoticesAndScoutsInFlightSurviveASnapshot()
    {
        _target.SetBuildingLevel(BuildingType.Watchtower, 1);
        SendAndArrive(1);
        Assert.Single(_state.ScoutNotices);
        _handler.Handle(Sender, Scout(2));

        var json = SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(_state));
        var restored = SnapshotMapper.Restore(SnapshotMapper.FromJson(json));

        Assert.Equal(json, SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(restored)));
        Assert.Equal(_target.Loyalty, restored.ScoutReports.Single().Loyalty!.Value, 3);
        Assert.Equal(_target.BuildingLevel(BuildingType.Watchtower), restored.ScoutReports.Single().Buildings![BuildingType.Watchtower]);
        Assert.Equal(_state.ScoutReports.Single().Id, restored.ScoutReports.Single().Id);
        Assert.Equal(_state.ScoutNotices.Single(), restored.ScoutNotices.Single());
    }

    [Fact]
    public void BattleAndScoutReportsShareIds()
    {
        _home.AddTroops(UnitType.Swordsman, 1);
        SendAndArrive(1);
        _handler.Handle(Sender, new SendTroopsCommand(Sender, Defender, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 1 }));
        _simulation.AdvanceTo(_state.Movements.Max(m => m.ArrivalTime));

        Assert.NotEqual(Report.Id, _state.Reports.Single().Id);
    }

    [Theory]
    [InlineData(600)]
    [InlineData(1)]
    [InlineData(0.25)]
    public void TheResultDoesNotDependOnTheStepSize(double step)
    {
        var results = new List<string>();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            results.Add(Run(seed, step));
            Assert.Equal(Run(seed, 600), results[^1]);
        }

        Assert.Contains(results, r => r.StartsWith("True"));
        Assert.Contains(results, r => r.StartsWith("False"));
    }

    private static string Run(ulong seed, double step)
    {
        var state = TestMatch.Create(duration: 36000, seed: seed);
        state.Villages[Sender].AddTroops(UnitType.Scout, 5);
        state.Villages[Defender].AddTroops(UnitType.Scout, 4);
        var simulation = new Simulation(state, Data);
        new CommandHandler(state, Data).Handle(Sender, new SendTroopsCommand(Sender, Defender, MovementType.Scout, new Dictionary<UnitType, int> { [UnitType.Scout] = 5 }));

        for (var time = step; time <= 1800; time += step)
            simulation.AdvanceTo(time);

        var report = state.ScoutReports.Single();
        return $"{report.Succeeded} {report.Time:0.000} {report.ScoutsLost} {state.Villages[Defender].TroopCount(UnitType.Scout)} {state.Villages[Sender].TroopCount(UnitType.Scout)}";
    }

    private double ExpectedArrival() =>
        UnitRules.TravelSeconds(Data, _state, _home.Position, _target.Position, new Dictionary<UnitType, int> { [UnitType.Scout] = 1 });
}
