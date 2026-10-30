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
    private static readonly int PerScout = Data.Combat.TroopsPerScout;

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
        SetTroops(_home, UnitType.Cavalry, 20);
        SetTroops(_target, UnitType.Spearman, 0);
    }

    private static void SetTroops(Village village, UnitType unit, int count)
    {
        foreach (var (present, amount) in village.Troops.ToList())
            village.RemoveTroops(present, amount);
        village.AddTroops(unit, count);
    }

    private static SendTroopsCommand Scout(int count, UnitType unit = UnitType.Cavalry, int to = Defender) =>
        new(Sender, to, MovementType.Scout, new Dictionary<UnitType, int> { [unit] = count });

    private List<GameEvent> SendAndArrive(int scouts)
    {
        Assert.True(_handler.Handle(Sender, Scout(scouts)).Success);
        return _simulation.AdvanceTo(_state.Movements.Single().ArrivalTime).ToList();
    }

    private ScoutReport Report => _state.ScoutReports.Single();

    [Fact]
    public void TheGarrisonAtTheLimitIsScouted()
    {
        SetTroops(_target, UnitType.Spearman, 2 * PerScout);

        SendAndArrive(2);

        Assert.True(Report.Succeeded);
    }

    [Fact]
    public void OneTroopOverTheLimitCatchesTheScouts()
    {
        SetTroops(_target, UnitType.Spearman, 2 * PerScout + 1);

        SendAndArrive(2);

        Assert.False(Report.Succeeded);
    }

    [Fact]
    public void AnEmptyVillageIsAlwaysScouted()
    {
        SendAndArrive(1);

        Assert.True(Report.Succeeded);
        Assert.Empty(Report.Garrison!);
    }

    [Fact]
    public void AllUnitsPresentCountTowardsTheGarrison()
    {
        SetTroops(_target, UnitType.Spearman, PerScout);
        _target.AddTroops(UnitType.Lord, 1);

        SendAndArrive(1);

        Assert.False(Report.Succeeded);
    }

    [Fact]
    public void ThresholdComesFromData()
    {
        var data = TestData.Mutated(GameData.CombatFile, root => root["troopsPerScout"] = 1);
        var simulation = new Simulation(_state, data);
        SetTroops(_target, UnitType.Spearman, 2);
        _handler.Handle(Sender, Scout(1));

        simulation.AdvanceTo(_state.Movements.Single().ArrivalTime);

        Assert.False(Report.Succeeded);
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

        SendAndArrive(1);

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
        Assert.Equal(4, trip.Troops[UnitType.Cavalry]);
        Assert.Equal(Resources.Zero, trip.Loot);

        _simulation.AdvanceTo(trip.ArrivalTime);

        Assert.Equal(20, _home.TroopCount(UnitType.Cavalry));
    }

    [Fact]
    public void CaughtScoutsAllDie()
    {
        SetTroops(_target, UnitType.Spearman, 100);

        SendAndArrive(4);

        Assert.Empty(_state.Movements);
        Assert.Equal(16, _home.TroopCount(UnitType.Cavalry));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(1)]
    public void TheDefenderLosesNothing(int scouts)
    {
        SetTroops(_target, UnitType.Spearman, 10);
        _target.Loyalty = Village.MaxLoyalty;
        var stock = _target.Resources;

        Assert.True(_handler.Handle(Sender, Scout(scouts)).Success);
        var rng = _state.RngState;
        var arrival = _state.Movements.Single().ArrivalTime;
        _simulation.AdvanceTo(arrival);

        Assert.Equal(10, _target.TroopCount(UnitType.Spearman));
        Assert.Equal(Village.MaxLoyalty, _target.Loyalty, 3);
        Assert.Equal(rng, _state.RngState);
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

    [Fact]
    public void ASuccessIsKnownOnlyToTheSender()
    {
        SendAndArrive(1);

        Assert.True(Report.VisibleTo(Sender));
        Assert.False(Report.VisibleTo(Defender));
    }

    [Fact]
    public void AFailureIsKnownToBothSidesButNotToOthers()
    {
        SetTroops(_target, UnitType.Spearman, 100);

        SendAndArrive(1);

        Assert.True(Report.VisibleTo(Sender));
        Assert.True(Report.VisibleTo(Defender));
        Assert.False(Report.VisibleTo(3));
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
    public void FightersCanScout(UnitType unit)
    {
        _home.AddTroops(unit, 1);

        Assert.True(SendPreview.Evaluate(_state, Data, Sender, Scout(1, unit)).CanSend);
    }

    [Theory]
    [InlineData(UnitType.Catapult)]
    [InlineData(UnitType.Settler)]
    [InlineData(UnitType.Lord)]
    public void OtherUnitsCannotScout(UnitType unit)
    {
        _home.AddTroops(unit, 1);

        Assert.Equal(CommandRejection.OnlyFightersCanScout, _handler.Handle(Sender, Scout(1, unit)).Rejection);
        Assert.Empty(_state.Movements);
    }

    [Fact]
    public void AVillageOfYourOwnCannotBeScouted() =>
        Assert.Equal(CommandRejection.InvalidMission, _handler.Handle(Sender, Scout(1, to: Sender)).Rejection);

    [Fact]
    public void ScoutsTravelAtTheSlowestUnitsSpeed()
    {
        _home.AddTroops(UnitType.Spearman, 1);
        var mixed = new SendTroopsCommand(Sender, Defender, MovementType.Scout, new Dictionary<UnitType, int> { [UnitType.Cavalry] = 1, [UnitType.Spearman] = 1 });

        var cavalry = SendPreview.Evaluate(_state, Data, Sender, Scout(1)).DurationSeconds;
        var both = SendPreview.Evaluate(_state, Data, Sender, mixed).DurationSeconds;

        Assert.True(both > cavalry);
    }

    [Fact]
    public void ScoutsArrivingAtAVillageTheirOwnerNowHoldsMergeIn()
    {
        Assert.True(_handler.Handle(Sender, Scout(2)).Success);
        _target.OwnerId = Sender;

        _simulation.AdvanceTo(_state.Movements.Single().ArrivalTime);

        Assert.Empty(_state.ScoutReports);
        Assert.Equal(2, _target.TroopCount(UnitType.Cavalry));
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
        Assert.Equal(2, other.TroopCount(UnitType.Cavalry));
    }

    [Fact]
    public void ReportsAndScoutsInFlightSurviveASnapshot()
    {
        SendAndArrive(1);
        _handler.Handle(Sender, Scout(2));

        var json = SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(_state));
        var restored = SnapshotMapper.Restore(SnapshotMapper.FromJson(json));

        Assert.Equal(json, SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(restored)));
        Assert.Equal(_state.ScoutReports.Single().Id, restored.ScoutReports.Single().Id);
        Assert.True(restored.ScoutReports.Single().VisibleTo(Sender));
    }

    [Fact]
    public void BattleAndScoutReportsShareIds()
    {
        SendAndArrive(1);
        _handler.Handle(Sender, new SendTroopsCommand(Sender, Defender, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Cavalry] = 1 }));
        _simulation.AdvanceTo(_state.Movements.Max(m => m.ArrivalTime));

        Assert.NotEqual(Report.Id, _state.Reports.Single().Id);
    }

    [Theory]
    [InlineData(600)]
    [InlineData(1)]
    [InlineData(0.25)]
    public void TheResultDoesNotDependOnTheStepSize(double step)
    {
        SetTroops(_target, UnitType.Spearman, 3);
        _handler.Handle(Sender, Scout(1));

        for (var time = step; time <= 6000; time += step)
            _simulation.AdvanceTo(time);

        Assert.True(Report.Succeeded);
        Assert.Equal(ExpectedArrival(), Report.Time, 6);
        Assert.Equal(20, _home.TroopCount(UnitType.Cavalry));
    }

    private double ExpectedArrival() =>
        UnitRules.TravelSeconds(Data, _state, _home.Position, _target.Position, new Dictionary<UnitType, int> { [UnitType.Cavalry] = 1 });
}
