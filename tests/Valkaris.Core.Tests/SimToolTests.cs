using Valkaris.Core.Commands;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;
using Valkaris.Sim;

namespace Valkaris.Core.Tests;

public class SimToolTests
{
    private static readonly SimOptions Quick = new()
    {
        Matches = 6,
        Players = 2,
        Difficulties = new[] { AiDifficulty.Easy, AiDifficulty.Hard },
        Minutes = 15,
        Speed = 2,
    };

    [Fact]
    public void TheSameSeedGivesTheSameReportAtAnyThreadCount()
    {
        string Run(int threads)
        {
            var (results, failures) = MatchRunner.RunAll(Quick with { Threads = threads }, TestData.Shipped);
            return SimReport.Build(results, failures).ToJson();
        }

        Assert.Equal(Run(1), Run(4));
    }

    [Fact]
    public void ADifferentSeedGivesADifferentRun()
    {
        var first = MatchRunner.RunOne(Quick, TestData.Shipped, 1, 0);
        var second = MatchRunner.RunOne(Quick, TestData.Shipped, 2, 0);

        Assert.NotEqual(first.Seats.Select(s => s.Score), second.Seats.Select(s => s.Score));
    }

    [Fact]
    public void DifficultiesRotateAcrossSeats()
    {
        Assert.Equal(AiDifficulty.Easy, MatchRunner.DifficultyOf(Quick, 0, 0));
        Assert.Equal(AiDifficulty.Hard, MatchRunner.DifficultyOf(Quick, 1, 0));
        Assert.Equal(AiDifficulty.Hard, MatchRunner.DifficultyOf(Quick, 0, 1));
        Assert.Equal(AiDifficulty.Easy, MatchRunner.DifficultyOf(Quick, 1, 1));
    }

    [Fact]
    public void OptionsAreParsed()
    {
        var options = SimOptions.Parse(new[] { "--matches=5", "--players=3", "--difficulty=Easy,Hard", "--map=Medium", "--minutes=20", "--speed=2", "--seed=9", "--threads=2", "--out=x.json" });

        Assert.Equal(5, options.Matches);
        Assert.Equal(3, options.Players);
        Assert.Equal(new[] { AiDifficulty.Easy, AiDifficulty.Hard }, options.Difficulties);
        Assert.Equal(MapSize.Medium, options.Map);
        Assert.Equal(20, options.Minutes);
        Assert.Equal(2, options.Speed);
        Assert.Equal(9UL, options.Seed);
        Assert.Equal(2, options.Threads);
        Assert.Equal("x.json", options.OutPath);
    }

    [Theory]
    [InlineData("--nope=1")]
    [InlineData("bare")]
    public void BadOptionsAreRefused(string argument) =>
        Assert.Throws<ArgumentException>(() => SimOptions.Parse(new[] { argument }));

    // The recorder, on matches whose outcome is known exactly.

    private sealed class Recorded
    {
        public Recorded(int players = 2, double duration = 3600)
        {
            State = TestMatch.Create(players: players, duration: duration);
            Session = new LocalGameSession(State, TestData.Shipped, 1);
            Recorder = new MatchRecorder(State, TestData.Shipped);
            Recorder.Attach(Session);
        }

        public GameState State { get; }
        public LocalGameSession Session { get; }
        public MatchRecorder Recorder { get; }

        public void Send(int player, ICommand command)
        {
            var result = Session.SendAs(player, command);
            Recorder.RecordCommand(result);
        }

        public MatchResult Finish()
        {
            Session.Tick(State.Settings.MatchDurationSeconds!.Value);
            Recorder.Sample();
            return Recorder.Finish(7);
        }
    }

    private static Dictionary<UnitType, int> Force(UnitType unit, int count) => new() { [unit] = count };

    [Fact]
    public void LootIsSplitBetweenBarbariansAndPlayers()
    {
        var match = new Recorded();
        var camp = match.State.Villages.Values.First(v => v.IsBarbarian);
        foreach (var (unit, count) in camp.Troops.ToList())
            camp.RemoveTroops(unit, count);
        match.State.Villages[2].RemoveTroops(UnitType.Spearman, match.State.Villages[2].TroopCount(UnitType.Spearman));
        match.State.Villages[1].AddTroops(UnitType.Cavalry, 40);
        match.Send(1, new SendTroopsCommand(1, camp.Id, MovementType.Raid, Force(UnitType.Cavalry, 20)));
        match.Send(1, new SendTroopsCommand(1, 2, MovementType.Raid, Force(UnitType.Cavalry, 20)));

        var result = match.Finish();

        Assert.Equal(match.State.Reports.Where(r => r.DefenderId == Village.BarbarianOwnerId).Sum(r => r.Loot.Wood + r.Loot.Stone + r.Loot.Food), result.LootFromBarbarians);
        Assert.Equal(match.State.Reports.Where(r => r.DefenderId == 2).Sum(r => r.Loot.Wood + r.Loot.Stone + r.Loot.Food), result.LootFromPlayers);
        Assert.True(result.LootFromBarbarians > 0);
        Assert.True(result.LootFromPlayers > 0);
        Assert.Equal(2, result.Battles);
    }

    [Fact]
    public void FirstTimesAreRecordedPerPlayer()
    {
        var match = new Recorded();
        var village = match.State.Villages[1];
        village.SetBuildingLevel(BuildingType.Barracks, 5);
        village.SetBuildingLevel(BuildingType.Warehouse, 8);
        village.Resources = new Resources(5000, 5000, 5000);
        match.Send(1, new TrainCommand(1, UnitType.Settler, 1));
        match.Send(1, new TrainCommand(1, UnitType.Lord, 1));
        var settlerDone = village.TrainingQueue[0].FinishTime;
        var lordDone = village.TrainingQueue[1].FinishTime;

        var seat = match.Finish().Seats[0];

        Assert.Equal(settlerDone, seat.FirstSettler);
        Assert.Equal(lordDone, seat.FirstLord);
        Assert.Null(match.Finish().Seats[1].FirstLord);
    }

    [Fact]
    public void ResourcesAtTheCapAndLostProductionAreMeasured()
    {
        var match = new Recorded();
        var village = match.State.Villages[1];
        var capacity = TestData.Shipped.Buildings.Buildings[BuildingType.Warehouse].Capacity!.ValueAt(1);
        village.Resources = new Resources(capacity, capacity, capacity);

        match.Session.Tick(100);
        match.Recorder.Sample();
        match.Session.Tick(match.State.Settings.MatchDurationSeconds!.Value);
        var result = match.Recorder.Finish(1);

        Assert.Equal(6, result.ResourceSamples);
        Assert.Equal(3, result.SamplesAtCap);
        Assert.Equal(900, result.ProductionLost, 6);
        Assert.Equal(1800, result.ProductionTotal, 6);
    }

    [Fact]
    public void TransfersAreCounted()
    {
        var match = new Recorded();
        var village = match.State.Villages[1];
        village.SetBuildingLevel(BuildingType.Marketplace, 1);
        var camp = match.State.Villages.Values.First(v => v.IsBarbarian);
        camp.OwnerId = 1;
        camp.BarbarianTier = null;
        camp.NextTroopRegenTime = null;
        var capacity = TestData.Shipped.Buildings.Buildings[BuildingType.Warehouse].Capacity!.ValueAt(camp.BuildingLevel(BuildingType.Warehouse));
        camp.Resources = new Resources(capacity - 10, 0, 0);
        match.Send(1, new SendResourcesCommand(1, camp.Id, 50, 0, 0));

        var result = match.Finish();

        Assert.Equal(1, result.Transfers);
        Assert.Equal(10, result.ResourcesMoved, 6);
        Assert.Equal(40, result.ResourcesLostInTransfer, 6);
    }

    [Fact]
    public void DefenceSpendingCountsSpearmenAndWallsOnly()
    {
        var match = new Recorded();
        var village = match.State.Villages[1];
        village.SetBuildingLevel(BuildingType.Barracks, 2);
        village.SetBuildingLevel(BuildingType.Warehouse, 8);
        village.Resources = new Resources(5000, 5000, 5000);
        match.Send(1, new TrainCommand(1, UnitType.Spearman, 2));
        match.Send(1, new TrainCommand(1, UnitType.Swordsman, 2));
        match.Send(1, new BuildCommand(1, BuildingType.Wall));
        match.Send(1, new BuildCommand(1, BuildingType.Farm));

        var defence = match.Finish().Seats[0].Defence!;

        var spearman = TestData.Shipped.Units.Units[UnitType.Spearman].Cost;
        var wall = BuildingRules.UpgradeCost(TestData.Shipped, BuildingType.Wall, 1);
        Assert.Equal(2 * (spearman.Wood + spearman.Stone + spearman.Food) + wall.Wood + wall.Stone + wall.Food, defence.Spent, 6);
        Assert.Equal(1, defence.WallAtEnd);
    }

    [Fact]
    public void HomeDefenceIsMeasuredAtMidMatchAndAtTheEnd()
    {
        var match = new Recorded(duration: 1000);
        var village = match.State.Villages[1];
        foreach (var (unit, count) in village.Troops.ToList())
            village.RemoveTroops(unit, count);
        village.AddTroops(UnitType.Spearman, 10);

        match.Session.Tick(400);
        match.Recorder.Sample();
        match.Session.Tick(100);
        match.Recorder.Sample();
        village.AddTroops(UnitType.Spearman, 10);

        var defence = match.Finish().Seats[0].Defence!;

        var spearman = TestData.Shipped.Units.Units[UnitType.Spearman];
        var perSpearman = (spearman.DefenceVsInfantry + spearman.DefenceVsCavalry) / 2;
        Assert.Equal(10 * perSpearman, defence.HomeDefenceAtMid!.Value, 6);
        Assert.Equal(20 * perSpearman, defence.HomeDefenceAtEnd, 6);
    }

    [Fact]
    public void AttacksWonAreCountedPerSeat()
    {
        var match = new Recorded();
        var target = match.State.Villages[2];
        foreach (var (unit, count) in target.Troops.ToList())
            target.RemoveTroops(unit, count);
        match.State.Villages[1].AddTroops(UnitType.Cavalry, 5);
        match.Send(1, new SendTroopsCommand(1, 2, MovementType.Attack, Force(UnitType.Cavalry, 5)));

        var result = match.Finish();

        Assert.Equal(1, result.Seats[0].Defence!.AttacksWon);
        Assert.Equal(0, result.Seats[1].Defence!.AttacksWon);
    }

    [Fact]
    public void ScoutingIsCountedPerSeat()
    {
        var match = new Recorded();
        var target = match.State.Villages[2];
        foreach (var (unit, count) in target.Troops.ToList())
            target.RemoveTroops(unit, count);
        match.State.Villages[1].AddTroops(UnitType.Cavalry, 10);
        match.State.Villages[1].AddTroops(UnitType.Scout, 10);
        match.Send(1, new SendTroopsCommand(1, 2, MovementType.Scout, Force(UnitType.Scout, 1)));
        match.Session.Tick(match.State.Movements.Single().ArrivalTime);
        target.AddTroops(UnitType.Spearman, 1000);
        match.Send(1, new SendTroopsCommand(1, 2, MovementType.Scout, Force(UnitType.Scout, 2)));
        match.Send(1, new SendTroopsCommand(1, 2, MovementType.Attack, Force(UnitType.Cavalry, 1)));

        var scouting = match.Finish().Seats[0].Scouting!;

        Assert.Equal(2, scouting.Sent);
        Assert.Equal(1, scouting.Succeeded);
        Assert.Equal(2, scouting.TroopsLost);
        Assert.Equal(1, scouting.AttacksWithIntel);
    }

    [Fact]
    public void ResourcesLostAndDestroyedAreCountedOnPlayersOnly()
    {
        var match = new Recorded();
        var camp = match.State.Villages.Values.First(v => v.IsBarbarian);
        match.State.Villages[1].AddTroops(UnitType.Cavalry, 40);
        match.Send(1, new SendTroopsCommand(1, 2, MovementType.Attack, Force(UnitType.Cavalry, 20)));
        match.Send(1, new SendTroopsCommand(1, camp.Id, MovementType.Attack, Force(UnitType.Cavalry, 20)));

        var result = match.Finish();

        var units = TestData.Shipped.Units.Units;
        double Value(IReadOnlyDictionary<UnitType, int> troops) => troops.Sum(t => t.Value * (units[t.Key].Cost.Wood + units[t.Key].Cost.Stone + units[t.Key].Cost.Food));
        var battle = match.State.Reports.Single(r => r.DefenderId == 2);
        Assert.Equal(Value(battle.AttackerLost), result.AttackerResourcesLost, 6);
        Assert.Equal(Value(battle.DefenderLost), result.DefenderResourcesLost, 6);
    }

    [Fact]
    public void TheExchangeRatioIsReportedForBothSides()
    {
        MatchResult Match(double lost, double destroyed) => new(1, MatchEndReason.TimeUp, 1, false, Array.Empty<SeatResult>(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new Dictionary<string, int>(), lost, destroyed);

        var report = SimReport.Build(new[] { Match(100, 50), Match(200, 100) }, Array.Empty<MatchFailure>());

        Assert.Equal(2, report.AttackerLostPerDestroyed, 6);
        Assert.Equal(0.5, report.DefenderLostPerDestroyed, 6);
    }

    [Fact]
    public void TheUnitMixIsAShareOfMilitarySpending()
    {
        var match = new Recorded();
        var village = match.State.Villages[1];
        village.SetBuildingLevel(BuildingType.Barracks, 5);
        village.SetBuildingLevel(BuildingType.Warehouse, 8);
        village.Resources = new Resources(5000, 5000, 5000);
        match.Send(1, new TrainCommand(1, UnitType.Spearman, 2));
        match.Send(1, new TrainCommand(1, UnitType.Settler, 1));

        var result = match.Finish();
        var report = SimReport.Build(new[] { result with { Seats = new[] { result.Seats[0] } } }, Array.Empty<MatchFailure>());

        var cost = TestData.Shipped.Units.Units[UnitType.Spearman].Cost;
        Assert.Equal(2 * (cost.Wood + cost.Stone + cost.Food), result.Seats[0].UnitSpending![UnitType.Spearman], 6);
        Assert.False(result.Seats[0].UnitSpending!.ContainsKey(UnitType.Settler));
        Assert.Equal(1, report.ByDifficulty.Single().UnitMix[UnitType.Spearman], 6);
    }

    [Fact]
    public void TheSpreadIsAlsoGivenAsAMultipleOfEven() =>
        Assert.Equal(2, new AttackSpread(1, 0.5, 0.25, 0).MultipleOfEven, 6);

    [Fact]
    public void AnUnlimitedMatchPastTheCapIsRecordedAsUnfinished()
    {
        var options = Quick with { Minutes = null, CapMinutes = 2, Matches = 1 };

        var result = MatchRunner.RunOne(options, TestData.Shipped, 1, 0);
        var report = SimReport.Build(new[] { result }, Array.Empty<MatchFailure>());

        Assert.True(result.Unfinished);
        Assert.Equal(1, report.UnfinishedShare);
        Assert.Equal(0, report.EndedWithinTwoHoursShare);
    }

    [Fact]
    public void UnlimitedIsAnOption()
    {
        var options = SimOptions.Parse(new[] { "--minutes=unlimited", "--cap-minutes=90" });

        Assert.Null(options.Minutes);
        Assert.Equal(90, options.CapMinutes);
    }

    [Fact]
    public void SurrendersAreCountedApartFromEliminations()
    {
        var match = new Recorded(players: 3);
        match.Send(3, new SurrenderCommand());

        var result = match.Finish();
        var report = SimReport.Build(new[] { result }, Array.Empty<MatchFailure>());

        Assert.Equal(1, result.Surrenders);
        Assert.Equal(0, result.Eliminations);
        Assert.Equal(1, report.PlayerOutShareOfMatches);
    }

    [Fact]
    public void RejectedCommandsAreCountedByReason()
    {
        var match = new Recorded();
        match.Send(1, new BuildCommand(2, BuildingType.Farm));
        match.Send(1, new BuildCommand(1, BuildingType.Farm));

        var result = match.Finish();

        Assert.Equal(2, result.Commands);
        Assert.Equal(1, result.Rejections[nameof(CommandRejection.NotOwner)]);
    }

    [Fact]
    public void AttackSpreadShowsGangingUp()
    {
        Dictionary<int, int> At(int target) => new() { [target] = 5 };
        SeatResult Seat(int id, int sent, int received) => new(id, AiDifficulty.Normal, false, false, 1, 1, null, null, null, null, null, sent, received, At(1));
        MatchResult Match(params SeatResult[] seats) => new(1, MatchEndReason.TimeUp, 1, false, seats, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new Dictionary<string, int>());

        var report = SimReport.Build(
            new[]
            {
                Match(Seat(1, 0, 10), Seat(2, 5, 0), Seat(3, 5, 0), Seat(4, 0, 0)),
                Match(Seat(1, 5, 5), Seat(2, 5, 5), Seat(3, 5, 5), Seat(4, 5, 5)),
            },
            Array.Empty<MatchFailure>());

        Assert.Equal(0.625, report.Attacks.MostAttackedShare, 6);
        Assert.Equal(0.25, report.Attacks.EvenShare, 6);
        Assert.Equal(0.5, report.Attacks.MatchesWithOneTargetOverTwiceEven, 6);
        Assert.Equal(15, report.Attacks.MeanAttacksPerMatch, 6);
    }

    [Fact]
    public void WinsAreSharedOutByDifficultyAndSeat()
    {
        SeatResult Seat(int id, AiDifficulty level, bool won) => new(id, level, won, !won, won ? 3 : 0, won ? 100 : 0, 10, null, null, null, won ? 50 : null, 0, 0, new Dictionary<int, int>());
        MatchResult Match(bool draw, params SeatResult[] seats) => new(1, MatchEndReason.LastPlayerStanding, 600, draw, seats, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new Dictionary<string, int>());

        var report = SimReport.Build(
            new[]
            {
                Match(false, Seat(1, AiDifficulty.Hard, true), Seat(2, AiDifficulty.Easy, false)),
                Match(false, Seat(1, AiDifficulty.Easy, false), Seat(2, AiDifficulty.Hard, true)),
                Match(true, Seat(1, AiDifficulty.Hard, true), Seat(2, AiDifficulty.Easy, true)),
            },
            Array.Empty<MatchFailure>());

        var hard = report.ByDifficulty.Single(d => d.Difficulty == AiDifficulty.Hard);
        Assert.Equal(2, hard.Wins);
        Assert.Equal(1, hard.Draws);
        Assert.Equal(1.0, hard.CaptureShare);
        Assert.Equal(new[] { 0.5, 0.5 }, report.WinShareBySeat);
        Assert.Equal(2.0 / 3.0, report.DecidedShare, 6);
    }

    [Fact]
    public void FailuresCarryTheirSeed()
    {
        var bad = Quick with { Players = 99, Matches = 2 };

        var (results, failures) = MatchRunner.RunAll(bad, TestData.Shipped);

        Assert.Empty(results);
        Assert.Equal(new[] { 1UL, 2UL }, failures.Select(f => f.Seed));
    }

    [Fact]
    public void TheReportPrintsEverySection()
    {
        var (results, failures) = MatchRunner.RunAll(Quick, TestData.Shipped);

        var table = SimReport.Build(results, failures).ToTable();

        foreach (var section in new[] { "Match", "Easy", "Hard", "Economy", "War", "Attack spread", "Health" })
            Assert.Contains(section, table);
    }
}
