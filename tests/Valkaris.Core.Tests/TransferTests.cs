using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class TransferTests
{
    private const int Owner = 1;
    private const int Rival = 2;
    private const int Home = 1;
    private const int RivalVillage = 2;

    private static readonly GameData Data = TestData.Shipped;
    private static int Capacity => Data.Villages.Carriers.Capacity;

    private readonly GameState _state;
    private readonly CommandHandler _handler;
    private readonly Simulation _simulation;
    private readonly Village _home;
    private readonly Village _target;

    public TransferTests() : this(TestMatch.Create())
    {
    }

    private TransferTests(GameState state)
    {
        _state = state;
        _handler = new CommandHandler(state, Data);
        _simulation = new Simulation(state, Data);
        _home = state.Villages[Home];
        _home.SetBuildingLevel(BuildingType.Marketplace, 3);
        _home.SetBuildingLevel(BuildingType.Warehouse, 5);
        _home.Resources = new Resources(2000, 2000, 2000);
        _target = state.Villages.Values.First(v => v.IsBarbarian);
        _target.OwnerId = Owner;
        _target.BarbarianTier = null;
        _target.NextTroopRegenTime = null;
        _target.Resources = Resources.Zero;
    }

    private SendResourcesCommand Load(int wood = 0, int stone = 0, int food = 0, int? to = null, int from = Home) =>
        new(from, to ?? _target.Id, wood, stone, food);

    private void AssertRejected(CommandRejection expected, SendResourcesCommand command, int issuer = Owner)
    {
        var resources = _home.Resources;
        var movements = _state.Movements.Count;

        Assert.Equal(expected, SendResourcesPreview.Evaluate(_state, Data, issuer, command).Rejection);
        Assert.Equal(expected, _handler.Handle(issuer, command).Rejection);
        Assert.Equal(resources, _home.Resources);
        Assert.Equal(movements, _state.Movements.Count);
    }

    private Movement Send(SendResourcesCommand command)
    {
        var result = _handler.Handle(Owner, command);
        Assert.True(result.Success, result.Rejection.ToString());
        return _state.Movements[^1];
    }

    private IReadOnlyList<GameEvent> RunUntilArrival() =>
        _simulation.Advance(_state.Movements.Min(m => m.ArrivalTime) - _state.ElapsedTime);

    // Starting state.

    [Fact]
    public void StartingVillagesHaveNoMarketplace()
    {
        var state = TestMatch.Create();

        Assert.Equal(0, state.Villages[Home].BuildingLevel(BuildingType.Marketplace));
        Assert.True(state.Villages[Home].Buildings.ContainsKey(BuildingType.Marketplace));
    }

    [Fact]
    public void FoundedVillageConfigHasMarketplaceAtZero() =>
        Assert.Equal(0, Data.Game.FoundedVillage.Buildings[BuildingType.Marketplace]);

    [Fact]
    public void BarbarianVillagesHaveNoMarketplace()
    {
        var state = TestMatch.Create();

        Assert.All(state.Villages.Values.Where(v => v.IsBarbarian), v => Assert.Equal(0, v.BuildingLevel(BuildingType.Marketplace)));
    }

    // The preview and command.

    [Fact]
    public void SendingTakesTheResourcesAndAddsAMovement()
    {
        var movement = Send(Load(wood: 300, food: 100));

        Assert.Equal(new Resources(1700, 2000, 1900), _home.Resources);
        Assert.Equal(MovementType.Transfer, movement.Type);
        Assert.Equal(_target.Id, movement.ToVillageId);
        Assert.Equal(new Resources(300, 0, 100), movement.Loot);
        Assert.Equal(2, movement.Carriers);
        Assert.Empty(movement.Troops);
    }

    [Fact]
    public void ArrivalUsesCarrierSpeedAndGameSpeed()
    {
        var movement = Send(Load(wood: 10));
        var distance = UnitRules.Distance(_home.Position, _target.Position);

        Assert.Equal(distance / Data.Villages.Carriers.Speed, movement.ArrivalTime - movement.DepartureTime, 6);

        var fast = new TransferTests(TestMatch.Create(speed: 3));
        var quick = fast.Send(fast.Load(wood: 10));
        Assert.Equal(UnitRules.Distance(fast._home.Position, fast._target.Position) / Data.Villages.Carriers.Speed / 3, quick.ArrivalTime, 6);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(199, 1)]
    [InlineData(200, 1)]
    [InlineData(201, 2)]
    [InlineData(400, 2)]
    [InlineData(401, 3)]
    public void CarriersAreRoundedUp(int amount, int carriers)
    {
        Assert.Equal(200, Capacity);

        Assert.Equal(carriers, SendResourcesPreview.Evaluate(_state, Data, Owner, Load(wood: amount)).CarriersUsed);
    }

    [Fact]
    public void AllResourceTypesShareTheCarriers() =>
        Assert.Equal(2, SendResourcesPreview.Evaluate(_state, Data, Owner, Load(wood: 100, stone: 100, food: 1)).CarriersUsed);

    [Fact]
    public void ThePreviewReportsCarriersEvenWhenRejected()
    {
        var option = SendResourcesPreview.Evaluate(_state, Data, Owner, Load(wood: 5000));

        Assert.False(option.CanSend);
        Assert.Equal(25, option.CarriersUsed);
        Assert.Equal(6, option.CarriersFree);
        Assert.Equal(6, option.CarriersTotal);
    }

    [Fact]
    public void RejectsAMissingSender() => AssertRejected(CommandRejection.VillageNotFound, Load(wood: 1, from: 999));

    [Fact]
    public void RejectsAMissingTarget() => AssertRejected(CommandRejection.VillageNotFound, Load(wood: 1, to: 999));

    [Fact]
    public void RejectsAnotherPlayersSender() => AssertRejected(CommandRejection.NotOwner, Load(wood: 1), Rival);

    [Fact]
    public void RejectsAnotherPlayersTarget() => AssertRejected(CommandRejection.TargetNotYours, Load(wood: 1, to: RivalVillage));

    [Fact]
    public void RejectsABarbarianTarget() =>
        AssertRejected(CommandRejection.TargetNotYours, Load(wood: 1, to: _state.Villages.Values.First(v => v.IsBarbarian).Id));

    [Fact]
    public void RejectsTheSameVillage() => AssertRejected(CommandRejection.SameVillage, Load(wood: 1, to: Home));

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(-5, 10, 0)]
    public void RejectsNegativeAmounts(int wood, int stone, int food) =>
        AssertRejected(CommandRejection.InvalidCount, Load(wood, stone, food));

    [Fact]
    public void RejectsNothingSelected() => AssertRejected(CommandRejection.NoResourcesSelected, Load());

    [Fact]
    public void RejectsWithoutAMarketplaceAtTheSender()
    {
        _home.SetBuildingLevel(BuildingType.Marketplace, 0);

        AssertRejected(CommandRejection.NoMarketplace, Load(wood: 1));
    }

    [Fact]
    public void RejectsTooFewResources() => AssertRejected(CommandRejection.InsufficientResources, Load(stone: 2001));

    [Fact]
    public void RejectsTooFewCarriers() => AssertRejected(CommandRejection.NotEnoughCarriers, Load(wood: Capacity * 6 + 1));

    [Fact]
    public void AllowsExactlyAllCarriers() => Send(Load(wood: Capacity * 6));

    [Fact]
    public void RejectsAfterTheMatchEnds()
    {
        _simulation.Advance(TestMatch.OneHour);

        Assert.Equal(CommandRejection.MatchOver, _handler.Handle(Owner, Load(wood: 1)).Rejection);
        Assert.Equal(CommandRejection.MatchOver, SendResourcesPreview.Evaluate(_state, Data, Owner, Load(wood: 1)).Rejection);
    }

    [Fact]
    public void CarriersInUseAreNotFree()
    {
        Send(Load(wood: Capacity * 4));

        Assert.Equal(2, CarrierRules.Free(_state, Data, _home));
        AssertRejected(CommandRejection.NotEnoughCarriers, Load(wood: Capacity * 2 + 1));
        Send(Load(wood: Capacity * 2));
        Assert.Equal(0, CarrierRules.Free(_state, Data, _home));
    }

    [Fact]
    public void ALoweredMarketplaceNeverGivesNegativeFreeCarriers()
    {
        Send(Load(wood: Capacity * 6));

        _home.SetBuildingLevel(BuildingType.Marketplace, 1);

        Assert.Equal(0, CarrierRules.Free(_state, Data, _home));
        AssertRejected(CommandRejection.NotEnoughCarriers, Load(wood: 1));
    }

    // The target needs no Marketplace.

    [Fact]
    public void AFreshlyCapturedBarbarianVillageCanReceive()
    {
        var state = TestMatch.Create();
        var handler = new CommandHandler(state, Data);
        var simulation = new Simulation(state, Data);
        var home = state.Villages[Home];
        home.SetBuildingLevel(BuildingType.Marketplace, 1);
        home.AddTroops(UnitType.Swordsman, 50);
        home.AddTroops(UnitType.Lord, 1);
        var camp = state.Villages.Values.First(v => v.IsBarbarian);
        foreach (var (unit, count) in camp.Troops.ToList())
            camp.RemoveTroops(unit, count);
        camp.Loyalty = 1;
        var troops = new Dictionary<UnitType, int> { [UnitType.Swordsman] = 50, [UnitType.Lord] = 1 };
        Assert.True(handler.Handle(Owner, new SendTroopsCommand(Home, camp.Id, MovementType.Attack, troops)).Success);
        simulation.Advance(state.Movements[0].ArrivalTime);
        Assert.Equal(Owner, camp.OwnerId);
        Assert.Equal(0, camp.BuildingLevel(BuildingType.Marketplace));
        var before = camp.Resources.Wood;

        var result = handler.Handle(Owner, new SendResourcesCommand(Home, camp.Id, 100, 0, 0));
        Assert.True(result.Success, result.Rejection.ToString());
        var transfer = state.Movements[^1];
        var events = simulation.Advance(transfer.ArrivalTime - state.ElapsedTime);

        Assert.Equal(new Resources(100, 0, 0), events.OfType<ResourcesDelivered>().Single().Delivered);
        Assert.Equal(before + 100, camp.Resources.Wood, 6);
    }

    [Fact]
    public void AVillageWithoutAMarketplaceCannotSendBack()
    {
        _target.Resources = new Resources(100, 0, 0);

        AssertRejected(CommandRejection.NoMarketplace, Load(wood: 10, to: Home, from: _target.Id));
    }

    // Expected loss.

    [Fact]
    public void ThereIsNoExpectedLossWithRoom() =>
        Assert.Equal(Resources.Zero, SendResourcesPreview.Evaluate(_state, Data, Owner, Load(wood: 100)).ExpectedLoss);

    [Fact]
    public void ExpectedLossCoversTheOverflow()
    {
        var capacity = BuildingRules.VillageCapacity(Data, _target);
        _target.Resources = new Resources(capacity - 40, capacity, 0);

        var loss = SendResourcesPreview.Evaluate(_state, Data, Owner, Load(wood: 100, stone: 50, food: 50)).ExpectedLoss;

        Assert.Equal(60, loss.Wood, 6);
        Assert.Equal(50, loss.Stone, 6);
        Assert.Equal(0, loss.Food, 6);
    }

    [Fact]
    public void ExpectedLossIncludesProductionUntilArrival()
    {
        _target.SetBuildingLevel(BuildingType.Woodcutter, 1);
        var capacity = BuildingRules.VillageCapacity(Data, _target);
        var option = SendResourcesPreview.Evaluate(_state, Data, Owner, Load(wood: 100));
        var produced = BuildingRules.VillageProduction(Data, _target).Wood * option.DurationSeconds;
        _target.Resources = new Resources(capacity - 100, 0, 0);

        var loss = SendResourcesPreview.Evaluate(_state, Data, Owner, Load(wood: 100)).ExpectedLoss;

        Assert.Equal(Math.Min(produced, 100), loss.Wood, 6);
    }

    [Fact]
    public void ExpectedLossIncludesTransfersAlreadyOnTheWay()
    {
        var capacity = BuildingRules.VillageCapacity(Data, _target);
        _target.Resources = new Resources(capacity - 100, 0, 0);
        Send(Load(wood: 100));

        var loss = SendResourcesPreview.Evaluate(_state, Data, Owner, Load(wood: 100)).ExpectedLoss;

        Assert.Equal(100, loss.Wood, 6);
    }

    [Fact]
    public void TheEstimateMatchesTheRealLoss()
    {
        var capacity = BuildingRules.VillageCapacity(Data, _target);
        _target.Resources = new Resources(capacity - 30, 0, 0);
        var option = SendResourcesPreview.Evaluate(_state, Data, Owner, Load(wood: 100));

        Send(Load(wood: 100));
        var delivery = RunUntilArrival().OfType<ResourcesDelivered>().Single();

        Assert.Equal(option.ExpectedLoss.Wood, delivery.Lost.Wood, 6);
    }

    // The trip.

    [Fact]
    public void DeliveryAddsTheLoadAndCarriersWalkHomeEmpty()
    {
        Send(Load(wood: 300, food: 100));

        var events = RunUntilArrival();

        Assert.Equal(new Resources(300, 0, 100), events.OfType<ResourcesDelivered>().Single().Delivered);
        Assert.Equal(300, _target.Resources.Wood, 6);
        Assert.Equal(100, _target.Resources.Food, 6);
        var back = Assert.Single(_state.Movements);
        Assert.Equal(MovementType.CarrierReturn, back.Type);
        Assert.Equal(Home, back.ToVillageId);
        Assert.Equal(Resources.Zero, back.Loot);
        Assert.Equal(2, back.Carriers);
    }

    [Fact]
    public void DeliveryIsCappedByTheWarehouseAndTheRestIsLost()
    {
        var capacity = BuildingRules.VillageCapacity(Data, _target);
        _target.Resources = new Resources(capacity - 100, 0, 0);

        Send(Load(wood: 300));
        var delivery = RunUntilArrival().OfType<ResourcesDelivered>().Single();

        Assert.Equal(capacity, _target.Resources.Wood);
        Assert.Equal(100, delivery.Delivered.Wood, 6);
        Assert.Equal(200, delivery.Lost.Wood, 6);
    }

    [Fact]
    public void ADeliveryWithRoomLosesExactlyNothing()
    {
        _target.Resources = new Resources(1.0 / 3.0, 0.1, 0.7);

        Send(Load(wood: 37, stone: 41, food: 53));
        var delivery = RunUntilArrival().OfType<ResourcesDelivered>().Single();

        Assert.Equal(Resources.Zero, delivery.Lost);
    }

    [Fact]
    public void CarriersAreFreeOnlyAfterReturning()
    {
        Send(Load(wood: Capacity * 2));
        Assert.Equal(4, CarrierRules.Free(_state, Data, _home));

        RunUntilArrival();
        Assert.Equal(4, CarrierRules.Free(_state, Data, _home));

        var events = RunUntilArrival();
        Assert.Single(events.OfType<CarriersReturned>());
        Assert.Equal(6, CarrierRules.Free(_state, Data, _home));
        Assert.Empty(_state.Movements);
    }

    [Fact]
    public void ALostTargetSendsTheLoadHome()
    {
        var movement = Send(Load(wood: 300));
        _target.OwnerId = Rival;
        var targetWood = _target.Resources.Wood;

        var events = RunUntilArrival();

        Assert.DoesNotContain(events, e => e is ResourcesDelivered);
        Assert.Equal(targetWood, _target.Resources.Wood, 1);
        var back = Assert.Single(_state.Movements);
        Assert.Equal(MovementType.CarrierReturn, back.Type);
        Assert.Equal(movement.Loot, back.Loot);
        Assert.Equal(2, back.Carriers);
        Assert.Equal(Home, back.ToVillageId);

        var woodBefore = _home.Resources.Wood;
        var returned = RunUntilArrival().OfType<CarriersReturned>().Single();
        Assert.Equal(300, returned.Delivered.Wood, 6);
        Assert.True(_home.Resources.Wood > woodBefore);
    }

    [Fact]
    public void ALoadBroughtHomeIsCappedToo()
    {
        Send(Load(wood: 300));
        _target.OwnerId = Rival;
        RunUntilArrival();
        var capacity = BuildingRules.VillageCapacity(Data, _home);
        _home.Resources = new Resources(capacity - 50, 0, 0);

        var returned = RunUntilArrival().OfType<CarriersReturned>().Single();

        Assert.Equal(capacity, _home.Resources.Wood);
        Assert.True(returned.Lost.Wood > 0);
    }

    [Fact]
    public void ATargetRegainedBeforeArrivalStillReceives()
    {
        Send(Load(wood: 300));
        _target.OwnerId = Rival;
        _target.OwnerId = Owner;

        Assert.Single(RunUntilArrival().OfType<ResourcesDelivered>());
    }

    [Fact]
    public void AHomeLostOnTheWayStillDeliversAndEmptyCarriersDisappear()
    {
        Send(Load(wood: 300));
        _home.OwnerId = Rival;

        var events = RunUntilArrival();

        Assert.Single(events.OfType<ResourcesDelivered>());
        Assert.Empty(_state.Movements);
    }

    [Fact]
    public void BothEndsLostTakesTheLoadToTheNearestVillage()
    {
        var third = _state.Villages.Values.First(v => v.IsBarbarian);
        third.OwnerId = Owner;
        third.BarbarianTier = null;
        third.NextTroopRegenTime = null;
        Send(Load(wood: 300));
        _home.OwnerId = Rival;
        _target.OwnerId = Rival;

        RunUntilArrival();

        var back = Assert.Single(_state.Movements);
        Assert.Equal(third.Id, back.ToVillageId);
        Assert.Equal(300, back.Loot.Wood);

        var woodBefore = third.Resources.Wood;
        RunUntilArrival();
        Assert.Empty(_state.Movements);
        Assert.True(third.Resources.Wood > woodBefore);
    }

    [Fact]
    public void AHomeLostWhileCarriersReturnWithALoadReroutes()
    {
        var third = _state.Villages.Values.First(v => v.IsBarbarian);
        third.OwnerId = Owner;
        third.BarbarianTier = null;
        third.NextTroopRegenTime = null;
        Send(Load(wood: 300));
        _target.OwnerId = Rival;
        RunUntilArrival();
        _home.OwnerId = Rival;

        RunUntilArrival();

        var back = Assert.Single(_state.Movements);
        Assert.Equal(MovementType.CarrierReturn, back.Type);
        Assert.Equal(third.Id, back.ToVillageId);
        Assert.Equal(300, back.Loot.Wood);
    }

    [Fact]
    public void AHomeLostWhileEmptyCarriersReturnEndsTheTrip()
    {
        Send(Load(wood: 300));
        RunUntilArrival();
        _home.OwnerId = Rival;

        var events = RunUntilArrival();

        Assert.Empty(_state.Movements);
        Assert.DoesNotContain(events, e => e is CarriersReturned);
    }

    [Fact]
    public void ANewOwnersMarketplaceIgnoresTheOldOwnersTrips()
    {
        Send(Load(wood: Capacity * 6));
        _home.OwnerId = Rival;

        Assert.Equal(6, CarrierRules.Free(_state, Data, _home));
    }

    [Fact]
    public void BattlesLeaveTransfersAlone()
    {
        var movement = Send(Load(wood: 300));
        var spears = _home.TroopCount(UnitType.Spearman);
        _state.Villages[RivalVillage].AddTroops(UnitType.Swordsman, 100);
        new CommandHandler(_state, Data).Handle(Rival, new SendTroopsCommand(RivalVillage, _target.Id, MovementType.Raid, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 100 }));

        var events = _simulation.Advance(movement.ArrivalTime + 0.001);

        Assert.Equal(300, events.OfType<ResourcesDelivered>().Single().Delivered.Wood, 6);
        Assert.Equal(spears, _home.TroopCount(UnitType.Spearman));
    }

    [Fact]
    public void AnEliminatedOwnersTransfersAreRemoved()
    {
        var state = TestMatch.Create(players: 3);
        var home = state.Villages[3];
        home.SetBuildingLevel(BuildingType.Marketplace, 1);
        var second = state.Villages.Values.First(v => v.IsBarbarian);
        second.OwnerId = 3;
        second.BarbarianTier = null;
        second.NextTroopRegenTime = null;
        var handler = new CommandHandler(state, Data);
        Assert.True(handler.Handle(3, new SendResourcesCommand(3, second.Id, 10, 0, 0)).Success);

        foreach (var village in new[] { home, second })
        {
            foreach (var (unit, count) in village.Troops.ToList())
                village.RemoveTroops(unit, count);
            village.Loyalty = 1;
        }
        state.Villages[1].AddTroops(UnitType.Swordsman, 100);
        state.Villages[1].AddTroops(UnitType.Lord, 2);
        var simulation = new Simulation(state, Data);
        foreach (var target in new[] { second.Id, home.Id })
        {
            var troops = new Dictionary<UnitType, int> { [UnitType.Swordsman] = 50, [UnitType.Lord] = 1 };
            Assert.True(handler.Handle(1, new SendTroopsCommand(1, target, MovementType.Attack, troops)).Success);
        }
        while (!state.Players[3].IsEliminated)
            simulation.Advance(30);

        Assert.DoesNotContain(state.Movements, m => m.OwnerId == 3);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0.25)]
    public void ResultDoesNotDependOnStepSize(double step)
    {
        var big = new TransferTests(TestMatch.Create());
        big.Send(big.Load(wood: 300));
        big._simulation.Advance(600);

        var small = new TransferTests(TestMatch.Create());
        small.Send(small.Load(wood: 300));
        for (var elapsed = 0.0; elapsed < 600; elapsed += step)
            small._simulation.Advance(step);

        Assert.Equal(big._target.Resources.Wood, small._target.Resources.Wood, 6);
        Assert.Equal(big._home.Resources.Wood, small._home.Resources.Wood, 6);
        Assert.Equal(big._state.Movements.Count, small._state.Movements.Count);
    }

    [Fact]
    public void ARestoredTransferContinuesIdentically()
    {
        Send(Load(wood: 300));

        var restored = SnapshotAssert.RoundTrips(_state);
        new Simulation(restored, Data).Advance(600);
        _simulation.Advance(600);

        Assert.Equal(_target.Resources.Wood, restored.Villages[_target.Id].Resources.Wood, 6);
        Assert.Equal(CarrierRules.Free(_state, Data, _home), CarrierRules.Free(restored, Data, restored.Villages[Home]));
    }
}
