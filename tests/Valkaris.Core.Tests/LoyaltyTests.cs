using Valkaris.Core.Commands;
using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class LoyaltyTests
{
    private const int Attacker = 1;
    private const int Home = 1;
    private const int Target = 2;

    private readonly GameState _state = TestMatch.Create(players: 3);
    private readonly CommandHandler _handler;
    private readonly Simulation _simulation;
    private readonly List<GameEvent> _events = new();

    public LoyaltyTests()
    {
        _handler = new CommandHandler(_state, TestData.Shipped);
        _simulation = new Simulation(_state, TestData.Shipped);
        Clear(_state.Villages[Target]);
    }

    private static void Clear(Village village)
    {
        foreach (var (unit, count) in village.Troops.ToList())
            village.RemoveTroops(unit, count);
    }

    private static Dictionary<UnitType, int> Force(params (UnitType Unit, int Count)[] troops) =>
        troops.ToDictionary(t => t.Unit, t => t.Count);

    private BattleReport Fight(Dictionary<UnitType, int> troops, int target = Target, MovementType type = MovementType.Attack)
    {
        foreach (var (unit, count) in troops)
            _state.Villages[Home].AddTroops(unit, count);

        var result = _handler.Handle(Attacker, new SendTroopsCommand(Home, target, type, troops));
        Assert.True(result.Success, result.Rejection.ToString());
        _events.AddRange(_simulation.Advance(_state.Movements[^1].ArrivalTime - _state.ElapsedTime));
        return _state.Reports[^1];
    }

    private Dictionary<UnitType, int> WithLord => Force((UnitType.Swordsman, 50), (UnitType.Lord, 1));

    [Fact]
    public void WonAttackWithALordLowersLoyaltyWithinTheRange()
    {
        var report = Fight(WithLord);

        Assert.True(report.AttackerWon);
        Assert.NotNull(report.Loyalty);
        Assert.InRange(report.Loyalty!.From - report.Loyalty.To, 20, 30);
        Assert.False(report.Loyalty.Captured);
        Assert.Equal(report.Loyalty.To, _state.Villages[Target].Loyalty);
        Assert.Equal(Attacker, _state.Reports[^1].AttackerId);
    }

    [Fact]
    public void ManyLordsLowerLoyaltyOnlyOnce()
    {
        var report = Fight(Force((UnitType.Swordsman, 50), (UnitType.Lord, 10)));

        Assert.InRange(report.Loyalty!.From - report.Loyalty.To, 20, 30);
    }

    [Fact]
    public void AttackWithoutALordDoesNothing()
    {
        var report = Fight(Force((UnitType.Swordsman, 50)));

        Assert.Null(report.Loyalty);
        Assert.Equal(Village.MaxLoyalty, _state.Villages[Target].Loyalty);
    }

    [Fact]
    public void LostAttackDoesNothing()
    {
        _state.Villages[Target].AddTroops(UnitType.Spearman, 500);

        var report = Fight(WithLord);

        Assert.False(report.AttackerWon);
        Assert.Null(report.Loyalty);
        Assert.Equal(Village.MaxLoyalty, _state.Villages[Target].Loyalty);
    }

    [Fact]
    public void ALordKilledInAWonAttackDoesNothing()
    {
        _state.Villages[Target].AddTroops(UnitType.Spearman, 30);

        var report = Fight(Force((UnitType.Swordsman, 20), (UnitType.Lord, 1)));

        Assert.True(report.AttackerWon);
        Assert.Equal(1, report.AttackerLost[UnitType.Lord]);
        Assert.Null(report.Loyalty);
    }

    [Fact]
    public void LordsAreNotConsumedAndReturnHome()
    {
        Fight(WithLord);

        Assert.Equal(1, _state.Movements.Single(m => m.Type == MovementType.Return).Troops[UnitType.Lord]);
    }

    [Fact]
    public void DropIsSeededAndCoversTheRange()
    {
        var seen = new HashSet<double>();
        for (ulong seed = 0; seed < 200; seed++)
        {
            var first = DropWithSeed(seed);
            Assert.Equal(first, DropWithSeed(seed));
            Assert.InRange(first, 20, 30);
            seen.Add(first);
        }

        Assert.Equal(11, seen.Count);
    }

    private static double DropWithSeed(ulong seed)
    {
        var state = GameFactory.CreateMatch(new GameSettings(3600, 1, MapSize.Small, seed), TestMatch.Slots(2), TestData.Shipped);
        var target = state.Villages[Target];
        Clear(target);
        state.Villages[Home].AddTroops(UnitType.Lord, 1);
        new CommandHandler(state, TestData.Shipped).Handle(Attacker, new SendTroopsCommand(Home, Target, MovementType.Attack, Force((UnitType.Lord, 1))));
        new Simulation(state, TestData.Shipped).Advance(state.Movements[0].ArrivalTime);
        var change = state.Reports[0].Loyalty!;
        return change.From - change.To;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 1)]
    [InlineData(1, 3)]
    public void RegenScalesWithMainBuildingAndSpeed(int mainLevel, double speed)
    {
        var state = TestMatch.Create(speed: speed);
        var village = state.Villages[Home];
        village.SetBuildingLevel(BuildingType.MainBuilding, mainLevel);
        village.Loyalty = 10;
        var config = TestData.Shipped.Villages.Loyalty;

        new Simulation(state, TestData.Shipped).Advance(100);

        var expected = 10 + (config.RegenPerSecond + config.RegenPerSecondPerMainLevel * mainLevel) * speed * 100;
        Assert.Equal(expected, village.Loyalty, 6);
    }

    [Fact]
    public void RegenStopsAtTheMaximum()
    {
        _state.Villages[Home].Loyalty = 99.9;

        _simulation.Advance(1000);

        Assert.Equal(Village.MaxLoyalty, _state.Villages[Home].Loyalty);
    }

    [Fact]
    public void BarbariansRegainLoyaltyToo()
    {
        var camp = _state.Villages.Values.First(v => v.IsBarbarian);
        camp.Loyalty = 10;

        _simulation.Advance(100);

        Assert.True(camp.Loyalty > 10);
    }

    [Fact]
    public void ResultDoesNotDependOnStepSize()
    {
        var expected = LoyaltyAfterMainBuildingUpgrade(600);

        Assert.Equal(expected, LoyaltyAfterMainBuildingUpgrade(1), 6);
        Assert.Equal(expected, LoyaltyAfterMainBuildingUpgrade(0.25), 6);
    }

    private static double LoyaltyAfterMainBuildingUpgrade(double step)
    {
        var state = TestMatch.Create();
        var village = state.Villages[Home];
        village.Loyalty = 10;
        new CommandHandler(state, TestData.Shipped).Handle(Attacker, new BuildCommand(Home, BuildingType.MainBuilding));
        var simulation = new Simulation(state, TestData.Shipped);
        for (var elapsed = 0.0; elapsed < 600; elapsed += step)
            simulation.Advance(step);
        return village.Loyalty;
    }

    // Capture.

    [Fact]
    public void ZeroLoyaltyChangesTheOwner()
    {
        var target = _state.Villages[Target];
        target.Loyalty = 1;
        target.Resources = new Resources(111, 222, 333);
        target.SetBuildingLevel(BuildingType.Farm, 4);
        target.EnqueueBuild(new BuildQueueItem(BuildingType.Wall, 1, 0, 9000));
        target.EnqueueTraining(new TrainingQueueItem(UnitType.Spearman, 5, 0, 9000));

        var report = Fight(Force((UnitType.Cavalry, 1), (UnitType.Lord, 1)));

        Assert.True(report.Loyalty!.Captured);
        Assert.Equal(0, report.Loyalty.To);
        Assert.Equal(2, report.DefenderId);
        Assert.Equal(Attacker, target.OwnerId);
        Assert.Equal(TestData.Shipped.Villages.Loyalty.AfterCapture, target.Loyalty);
        Assert.Empty(target.BuildQueue);
        Assert.Empty(target.TrainingQueue);
        Assert.Equal(4, target.BuildingLevel(BuildingType.Farm));
        Assert.True(target.Resources.Wood > 0);
        Assert.Contains(Target, _state.Players[Attacker].CapturedVillageIds);
        var captured = Assert.Single(_events.OfType<VillageCaptured>());
        Assert.Equal(2, captured.PreviousOwnerId);
        Assert.IsType<BattleResolved>(_events[_events.IndexOf(captured) - 1]);
    }

    [Fact]
    public void SurvivorsGoHomeAfterACapture()
    {
        _state.Villages[Target].Loyalty = 1;

        Fight(WithLord);

        var trip = Assert.Single(_state.Movements);
        Assert.Equal(MovementType.Return, trip.Type);
        Assert.Equal(Home, trip.ToVillageId);
        Assert.Equal(0, _state.Villages[Target].TroopCount(UnitType.Lord));
    }

    [Fact]
    public void ACapturedVillageNoLongerHoldsAnOldQueueEvent()
    {
        var target = _state.Villages[Target];
        target.Loyalty = 1;
        target.EnqueueBuild(new BuildQueueItem(BuildingType.Wall, 1, 0, 9000));

        Fight(WithLord);
        _simulation.Advance(TestMatch.OneHour);

        Assert.Equal(0, target.BuildingLevel(BuildingType.Wall));
    }

    [Fact]
    public void CapturedBarbarianVillageBecomesNormal()
    {
        var camp = _state.Villages.Values.First(v => v.IsBarbarian);
        Clear(camp);
        camp.Loyalty = 1;
        camp.Resources = Resources.Zero;

        Fight(WithLord, camp.Id);

        Assert.Equal(Attacker, camp.OwnerId);
        Assert.Null(camp.BarbarianTier);
        Assert.Null(camp.NextTroopRegenTime);
        Assert.Contains(camp.Id, _state.Players[Attacker].CapturedVillageIds);

        var woodAfterCapture = camp.Resources.Wood;
        _simulation.Advance(600);

        Assert.DoesNotContain(camp.Troops, t => t.Value > 0);
        Assert.Equal(woodAfterCapture, camp.Resources.Wood, 6);
    }

    [Fact]
    public void CapturedVillageProducesForItsNewOwnerAndCanBeUsed()
    {
        var target = _state.Villages[Target];
        target.Loyalty = 1;
        target.Resources = new Resources(500, 500, 500);

        Fight(WithLord);

        Assert.True(_handler.Handle(Attacker, new BuildCommand(Target, BuildingType.Wall)).Success);
        Assert.Equal(CommandRejection.NotOwner, _handler.Handle(2, new BuildCommand(Target, BuildingType.Farm)).Rejection);
    }

    [Fact]
    public void CaptureRoundTripsThroughASnapshot()
    {
        _state.Villages[Target].Loyalty = 1;

        Fight(WithLord);

        var restored = SnapshotAssert.RoundTrips(_state);
        Assert.Equal(_state.Villages[Target].Loyalty, restored.Villages[Target].Loyalty);
        Assert.Equal(_state.Reports[^1].Loyalty, restored.Reports[^1].Loyalty);
    }
}
