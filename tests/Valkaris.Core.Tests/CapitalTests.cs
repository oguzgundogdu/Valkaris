using Valkaris.Core.Commands;
using Valkaris.Core.Events;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class CapitalTests
{
    private const int Human = 1;
    private const int Rival = 2;
    private const int HumanCapital = 1;
    private const int RivalCapital = 2;

    private readonly GameState _state;
    private readonly CommandHandler _handler;
    private readonly Simulation _simulation;
    private readonly Village _second;
    private readonly Village _third;
    private readonly List<GameEvent> _events = new();

    public CapitalTests() : this(TestMatch.Create())
    {
    }

    private CapitalTests(GameState state)
    {
        _state = state;
        _handler = new CommandHandler(state, TestData.Shipped);
        _simulation = new Simulation(state, TestData.Shipped);
        var camps = state.Villages.Values.Where(v => v.IsBarbarian).ToList();
        _second = Convert(state, camps[0], Human);
        _third = Convert(state, camps[1], Human);
        Empty(state.Villages[HumanCapital]);
        Empty(state.Villages[RivalCapital]);
    }

    private static Village Convert(GameState state, Village village, int owner)
    {
        village.OwnerId = owner;
        village.BarbarianTier = null;
        village.NextTroopRegenTime = null;
        return village;
    }

    private static void Empty(Village village)
    {
        foreach (var (unit, count) in village.Troops.ToList())
            village.RemoveTroops(unit, count);
    }

    private BattleReport Attack(int attacker, int from, int target, params (UnitType Unit, int Count)[] force) =>
        Attack(attacker, from, target, null, force);

    private BattleReport Attack(int attacker, int from, int target, BuildingType? aim, params (UnitType Unit, int Count)[] force)
    {
        var troops = force.ToDictionary(t => t.Unit, t => t.Count);
        foreach (var (unit, count) in troops)
            _state.Villages[from].AddTroops(unit, count);

        var result = _handler.Handle(attacker, new SendTroopsCommand(from, target, MovementType.Attack, troops, aim));
        Assert.True(result.Success, result.Rejection.ToString());
        _events.AddRange(_simulation.Advance(_state.Movements[^1].ArrivalTime - _state.ElapsedTime));
        return _state.Reports[^1];
    }

    private void CaptureHumanCapital()
    {
        _state.Villages[HumanCapital].Loyalty = 1;
        Attack(Rival, RivalCapital, HumanCapital, (UnitType.Swordsman, 50), (UnitType.Lord, 1));
        Assert.Equal(Rival, _state.Villages[HumanCapital].OwnerId);
    }

    private static int Threshold => TestData.Shipped.Villages.Capital.LockedAboveMainLevel;

    // The lock.

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    public void LoyaltyIsLockedOnlyAboveTheThreshold(int levelsAbove, bool locked)
    {
        _state.Villages[RivalCapital].SetBuildingLevel(BuildingType.MainBuilding, Threshold + levelsAbove);

        var report = Attack(Human, HumanCapital, RivalCapital, (UnitType.Swordsman, 50), (UnitType.Lord, 1));

        Assert.Equal(locked, report.CapitalProtected);
        Assert.Equal(locked, report.Loyalty is null);
        Assert.Equal(locked, _state.Villages[RivalCapital].Loyalty == Village.MaxLoyalty);
    }

    [Fact]
    public void OtherVillagesAreNotLocked()
    {
        _third.SetBuildingLevel(BuildingType.MainBuilding, 10);
        Empty(_third);

        var report = Attack(Rival, RivalCapital, _third.Id, (UnitType.Swordsman, 50), (UnitType.Lord, 1));

        Assert.False(report.CapitalProtected);
        Assert.NotNull(report.Loyalty);
    }

    [Fact]
    public void CatapultsInTheSameWaveOpenTheLock()
    {
        var capital = _state.Villages[RivalCapital];
        capital.SetBuildingLevel(BuildingType.MainBuilding, Threshold + 1);

        var report = Attack(Human, HumanCapital, RivalCapital, BuildingType.MainBuilding, (UnitType.Swordsman, 100), (UnitType.Catapult, 10), (UnitType.Lord, 1));

        Assert.Equal(Threshold, capital.BuildingLevel(BuildingType.MainBuilding));
        Assert.False(report.CapitalProtected);
        Assert.NotNull(report.Loyalty);
    }

    [Fact]
    public void ARaidedOrLostAttackIsNeverReportedAsProtected()
    {
        _state.Villages[RivalCapital].SetBuildingLevel(BuildingType.MainBuilding, 10);
        _state.Villages[RivalCapital].AddTroops(UnitType.Spearman, 500);

        var report = Attack(Human, HumanCapital, RivalCapital, (UnitType.Swordsman, 5), (UnitType.Lord, 1));

        Assert.False(report.CapitalProtected);
    }

    // The choice.

    [Fact]
    public void LosingTheCapitalStartsAChoice()
    {
        CaptureHumanCapital();

        var player = _state.Players[Human];
        Assert.Null(player.CapitalVillageId);
        Assert.Equal(_state.ElapsedTime + TestData.Shipped.Villages.Capital.ChoiceSeconds, player.CapitalChoiceDeadline);
        Assert.Equal(player.CapitalChoiceDeadline, _events.OfType<CapitalChoiceRequired>().Single().Deadline);
    }

    [Fact]
    public void GameSpeedDoesNotShortenTheChoice()
    {
        var test = new CapitalTests(TestMatch.Create(speed: 3));

        test.CaptureHumanCapital();

        Assert.Equal(test._state.ElapsedTime + TestData.Shipped.Villages.Capital.ChoiceSeconds, test._state.Players[Human].CapitalChoiceDeadline);
    }

    [Fact]
    public void ChoosingAppliesThePenaltyAndLowersQueuedUpgrades()
    {
        CaptureHumanCapital();
        _second.SetBuildingLevel(BuildingType.MainBuilding, 5);
        _second.EnqueueBuild(new BuildQueueItem(BuildingType.MainBuilding, 6, 0, 9000));

        Assert.True(_handler.Handle(Human, new SetCapitalCommand(_second.Id)).Success);

        var player = _state.Players[Human];
        Assert.Equal(_second.Id, player.CapitalVillageId);
        Assert.Null(player.CapitalChoiceDeadline);
        var penalty = TestData.Shipped.Villages.Capital.PenaltyMainLevel;
        Assert.Equal(penalty, _second.BuildingLevel(BuildingType.MainBuilding));
        Assert.Equal(penalty + 1, _second.BuildQueue[0].TargetLevel);
    }

    [Fact]
    public void ThePenaltyNeverRaisesALevel()
    {
        CaptureHumanCapital();
        _second.SetBuildingLevel(BuildingType.MainBuilding, 0);

        _handler.Handle(Human, new SetCapitalCommand(_second.Id));

        Assert.Equal(0, _second.BuildingLevel(BuildingType.MainBuilding));
    }

    [Fact]
    public void SetCapitalIsRejectedOutsideAChoice() =>
        Assert.Equal(CommandRejection.NoCapitalChoicePending, _handler.Handle(Human, new SetCapitalCommand(_second.Id)).Rejection);

    [Fact]
    public void SetCapitalIsRejectedForAnotherPlayersVillage()
    {
        CaptureHumanCapital();

        Assert.Equal(CommandRejection.NotOwner, _handler.Handle(Human, new SetCapitalCommand(RivalCapital)).Rejection);
        Assert.Equal(CommandRejection.VillageNotFound, _handler.Handle(Human, new SetCapitalCommand(999)).Rejection);
        Assert.Null(_state.Players[Human].CapitalVillageId);
    }

    [Fact]
    public void FoundingWaitsForACapital()
    {
        CaptureHumanCapital();
        _second.AddTroops(UnitType.Settler, 1);
        var tile = new TileCoord(0, 0);

        var option = FoundPreview.Evaluate(_state, TestData.Shipped, Human, _second.Id, tile);

        // The tile may be unusable on this seed, so only the limit check is asserted directly.
        Assert.Equal(CommandRejection.NoCapital, FoundPreview.LimitRejection(_state, TestData.Shipped, Human));
        Assert.False(option.CanFound);
    }

    // Timeout.

    [Fact]
    public void TimeoutPicksTheHighestMainBuilding()
    {
        CaptureHumanCapital();
        _second.SetBuildingLevel(BuildingType.MainBuilding, 2);
        _third.SetBuildingLevel(BuildingType.MainBuilding, 6);

        var events = _simulation.Advance(TestData.Shipped.Villages.Capital.ChoiceSeconds);

        Assert.Equal(_third.Id, _state.Players[Human].CapitalVillageId);
        Assert.Null(_state.Players[Human].CapitalChoiceDeadline);
        Assert.Equal(TestData.Shipped.Villages.Capital.PenaltyMainLevel, _third.BuildingLevel(BuildingType.MainBuilding));
        Assert.Equal(_third.Id, events.OfType<CapitalChanged>().Single().VillageId);
    }

    [Fact]
    public void TimeoutBreaksATieByLowestId()
    {
        CaptureHumanCapital();
        _second.SetBuildingLevel(BuildingType.MainBuilding, 4);
        _third.SetBuildingLevel(BuildingType.MainBuilding, 4);

        _simulation.Advance(TestData.Shipped.Villages.Capital.ChoiceSeconds);

        Assert.Equal(Math.Min(_second.Id, _third.Id), _state.Players[Human].CapitalVillageId);
    }

    [Theory]
    [InlineData(600)]
    [InlineData(1)]
    [InlineData(0.25)]
    public void TimeoutIsExactAtAnyStepSize(double step)
    {
        CaptureHumanCapital();
        var deadline = _state.Players[Human].CapitalChoiceDeadline!.Value;
        var events = new List<GameEvent>();

        while (_state.ElapsedTime < deadline + 1)
            events.AddRange(_simulation.Advance(step));

        Assert.Equal(deadline, events.OfType<CapitalChanged>().Single().Time);
    }

    [Fact]
    public void AnIdleAiPlayerIsPickedForByTheTimeout()
    {
        var rivalSecond = Convert(_state, _state.Villages.Values.First(v => v.IsBarbarian), Rival);
        rivalSecond.SetBuildingLevel(BuildingType.MainBuilding, 4);
        _state.Villages[RivalCapital].Loyalty = 1;

        Attack(Human, HumanCapital, RivalCapital, (UnitType.Swordsman, 50), (UnitType.Lord, 1));

        Assert.Null(_state.Players[Rival].CapitalVillageId);
        Assert.Single(_events.OfType<CapitalChoiceRequired>());

        _simulation.Advance(TestData.Shipped.Villages.Capital.ChoiceSeconds);

        Assert.Equal(rivalSecond.Id, _state.Players[Rival].CapitalVillageId);
    }

    [Fact]
    public void PauseStopsTheTimer()
    {
        CaptureHumanCapital();
        var deadline = _state.Players[Human].CapitalChoiceDeadline;
        var session = new LocalGameSession(_state, TestData.Shipped, Human);

        session.SetPaused(true);
        session.Tick(TestMatch.OneHour);

        Assert.Equal(deadline, _state.Players[Human].CapitalChoiceDeadline);
        Assert.Null(_state.Players[Human].CapitalVillageId);
    }

    [Fact]
    public void ARestoredChoiceTimesOutIdentically()
    {
        CaptureHumanCapital();
        _third.SetBuildingLevel(BuildingType.MainBuilding, 6);

        var restored = SnapshotAssert.RoundTrips(_state);
        new Simulation(restored, TestData.Shipped).Advance(TestData.Shipped.Villages.Capital.ChoiceSeconds);

        Assert.Equal(_third.Id, restored.Players[Human].CapitalVillageId);
    }
}
