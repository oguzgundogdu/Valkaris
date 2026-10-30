using Valkaris.Core.Commands;
using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

/// <summary>Movements already on the way when a village changes owner.</summary>
public class OwnerChangeTests
{
    private const int Owner = 1;
    private const int Other = 2;
    private const int Home = 1;
    private const int Enemy = 2;
    private static readonly Dictionary<UnitType, int> Troops = new() { [UnitType.Swordsman] = 10 };

    private readonly GameState _state = TestMatch.Create();
    private readonly CommandHandler _handler;
    private readonly Simulation _simulation;
    private readonly Village _second;

    public OwnerChangeTests()
    {
        _handler = new CommandHandler(_state, TestData.Shipped);
        _simulation = new Simulation(_state, TestData.Shipped);
        _second = _state.Villages.Values.First(v => v.IsBarbarian);
        _second.OwnerId = Owner;
        _second.BarbarianTier = null;
        _state.Villages[Home].AddTroops(UnitType.Swordsman, 10);
    }

    private double SendAndGetArrival(int to, MovementType type)
    {
        Assert.True(_handler.Handle(Owner, new SendTroopsCommand(Home, to, type, Troops)).Success);
        return _state.Movements[^1].ArrivalTime;
    }

    [Fact]
    public void ReturningTroopsRerouteToTheNearestVillageWithTheirLoot()
    {
        var third = _state.Villages.Values.Where(v => v.IsBarbarian).MaxBy(v => UnitRules.Distance(v.Position, _state.Villages[Home].Position))!;
        third.OwnerId = Owner;
        third.BarbarianTier = null;
        var loot = new Resources(10, 20, 30);
        _state.AddMovement(new Movement(50, Owner, Enemy, Home, MovementType.Return, Troops, loot, 0, 5, null));
        _state.Villages[Home].OwnerId = Other;

        var nearest = new[] { _second, third }.OrderBy(v => UnitRules.Distance(v.Position, _state.Villages[Home].Position)).ThenBy(v => v.Id).First();
        var events = _simulation.Advance(5);

        Assert.Empty(events.OfType<TroopsReturned>());
        var reroute = Assert.Single(_state.Movements);
        Assert.Equal(MovementType.Return, reroute.Type);
        Assert.Equal(Home, reroute.FromVillageId);
        Assert.Equal(nearest.Id, reroute.ToVillageId);
        Assert.Equal(loot, reroute.Loot);

        var wood = nearest.Resources.Wood;
        var troops = nearest.TroopCount(UnitType.Swordsman);
        var events2 = _simulation.Advance(reroute.ArrivalTime - _state.ElapsedTime);

        Assert.Equal(loot, Assert.Single(events2.OfType<TroopsReturned>()).Loot);
        Assert.Equal(troops + 10, nearest.TroopCount(UnitType.Swordsman));
        Assert.True(nearest.Resources.Wood > wood);
    }

    [Fact]
    public void ReinforcementsTurnBackIfTheVillageIsLost()
    {
        var arrival = SendAndGetArrival(_second.Id, MovementType.Reinforce);
        _second.OwnerId = Other;
        var present = _second.TroopCount(UnitType.Swordsman);

        var events = _simulation.Advance(arrival);

        Assert.Empty(events.OfType<ReinforcementsArrived>());
        Assert.Equal(present, _second.TroopCount(UnitType.Swordsman));
        var trip = Assert.Single(_state.Movements);
        Assert.Equal(MovementType.Return, trip.Type);
        Assert.Equal(Home, trip.ToVillageId);

        _simulation.Advance(trip.ArrivalTime - _state.ElapsedTime);
        Assert.Equal(10, _state.Villages[Home].TroopCount(UnitType.Swordsman));
    }

    [Theory]
    [InlineData(MovementType.Attack)]
    [InlineData(MovementType.Raid)]
    public void AnAttackOnAVillageNowOwnedMergesWithNoBattle(MovementType mission)
    {
        var arrival = SendAndGetArrival(Enemy, mission);
        _state.Villages[Enemy].OwnerId = Owner;
        var before = _state.Villages[Enemy].TroopCount(UnitType.Swordsman);

        var events = _simulation.Advance(arrival);

        Assert.Empty(_state.Reports);
        Assert.Single(events.OfType<ReinforcementsArrived>());
        Assert.Equal(before + 10, _state.Villages[Enemy].TroopCount(UnitType.Swordsman));
        Assert.Empty(_state.Movements);
    }

    [Fact]
    public void AttackFromALostVillageGoesHomeToTheNearestOne()
    {
        _state.Villages[Enemy].RemoveTroops(UnitType.Spearman, _state.Villages[Enemy].TroopCount(UnitType.Spearman));
        var arrival = SendAndGetArrival(Enemy, MovementType.Attack);
        _state.Villages[Home].OwnerId = Other;

        _simulation.Advance(arrival);

        var trip = Assert.Single(_state.Movements);
        Assert.Equal(_second.Id, trip.ToVillageId);
        Assert.Equal(Enemy, trip.FromVillageId);
    }

    [Fact]
    public void ASettlerFromALostVillageGoesToTheNearestOne()
    {
        // Aimed at a taken tile, so it has to walk back.
        _state.AddMovement(new Movement(60, Owner, Home, null, MovementType.Settle, FoundPreview.OneSettler, Resources.Zero, 0, 5, null, _state.Villages[Enemy].Position));
        _state.Villages[Home].OwnerId = Other;

        var events = _simulation.Advance(5);

        Assert.Single(events.OfType<SettlingFailed>());
        var trip = Assert.Single(_state.Movements);
        Assert.Equal(_second.Id, trip.ToVillageId);
        Assert.Equal(_state.Villages[Enemy].Position, trip.FromTile);
    }

    [Theory]
    [InlineData(MovementType.Attack)]
    [InlineData(MovementType.Raid)]
    [InlineData(MovementType.Reinforce)]
    [InlineData(MovementType.Return)]
    public void NoMovementTypeFailsWhenBothEndsChangeHands(MovementType mission)
    {
        _state.AddMovement(new Movement(50, Owner, Home, _second.Id, mission, Troops, Resources.Zero, 0, 5, null));
        var keeper = _state.Villages.Values.Where(v => v.IsBarbarian).Last();
        keeper.OwnerId = Owner;
        keeper.BarbarianTier = null;
        _state.Villages[Home].OwnerId = Other;
        _second.OwnerId = Other;

        _simulation.Advance(TestMatch.OneHour - 1);

        Assert.Empty(_state.Movements);
    }
}
