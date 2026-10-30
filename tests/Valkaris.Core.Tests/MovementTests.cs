using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class MovementTests
{
    private const int Owner = 1;
    private const int Home = 1;
    private const int Enemy = 2;

    private static readonly GameData Data = TestData.Shipped;

    private readonly GameState _state = TestMatch.Create();
    private readonly CommandHandler _handler;
    private readonly Simulation _simulation;
    private readonly Village _home;

    public MovementTests()
    {
        _handler = new CommandHandler(_state, Data);
        _simulation = new Simulation(_state, Data);
        _home = _state.Villages[Home];
        foreach (var unit in Enum.GetValues<UnitType>())
            _home.AddTroops(unit, 10);
    }

    private static Dictionary<UnitType, int> Troops(UnitType unit, int count) => new() { [unit] = count };

    private static SendTroopsCommand Attack(UnitType unit = UnitType.Cavalry, int count = 3, int to = Enemy) =>
        new(Home, to, MovementType.Attack, Troops(unit, count));

    private static string Json(GameState state) => SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(state));

    private void AssertRejected(CommandRejection expected, SendTroopsCommand command, int issuer = Owner)
    {
        var before = Json(_state);

        var result = _handler.Handle(issuer, command);

        Assert.Equal(expected, result.Rejection);
        Assert.Equal(before, Json(_state));
    }

    private double Distance() => UnitRules.Distance(_home.Position, _state.Villages[Enemy].Position);

    [Fact]
    public void SendingRemovesTroopsAndCreatesAMovement()
    {
        var before = _home.TroopCount(UnitType.Cavalry);

        Assert.True(_handler.Handle(Owner, Attack(count: 3)).Success);

        Assert.Equal(before - 3, _home.TroopCount(UnitType.Cavalry));
        var movement = Assert.Single(_state.Movements);
        Assert.Equal(Owner, movement.OwnerId);
        Assert.Equal(Home, movement.FromVillageId);
        Assert.Equal(Enemy, movement.ToVillageId);
        Assert.Equal(3, movement.Troops[UnitType.Cavalry]);
        Assert.Equal(0, movement.DepartureTime);
    }

    [Fact]
    public void TravelTimeIsDistanceOverSpeed()
    {
        _handler.Handle(Owner, Attack(UnitType.Cavalry));

        Assert.Equal(Distance() / Data.Units.Units[UnitType.Cavalry].Speed, _state.Movements[0].ArrivalTime, precision: 10);
    }

    [Fact]
    public void TheSlowestUnitSetsThePace()
    {
        var mixed = new Dictionary<UnitType, int> { [UnitType.Cavalry] = 2, [UnitType.Catapult] = 1 };

        _handler.Handle(Owner, new SendTroopsCommand(Home, Enemy, MovementType.Attack, mixed));

        Assert.Equal(Distance() / Data.Units.Units[UnitType.Catapult].Speed, _state.Movements[0].ArrivalTime, precision: 10);
    }

    [Fact]
    public void UnitsCountedAsZeroDoNotSetThePace()
    {
        var troops = new Dictionary<UnitType, int> { [UnitType.Cavalry] = 2, [UnitType.Catapult] = 0 };

        _handler.Handle(Owner, new SendTroopsCommand(Home, Enemy, MovementType.Attack, troops));

        Assert.Equal(Distance() / Data.Units.Units[UnitType.Cavalry].Speed, _state.Movements[0].ArrivalTime, precision: 10);
        Assert.False(_state.Movements[0].Troops.ContainsKey(UnitType.Catapult));
    }

    [Fact]
    public void SpeedMultiplierShortensTravel()
    {
        var fast = TestMatch.Create(speed: 2);
        new CommandHandler(fast, Data).Handle(Owner, new SendTroopsCommand(Home, Enemy, MovementType.Attack, Troops(UnitType.Spearman, 1)));

        Assert.Equal(Distance() / Data.Units.Units[UnitType.Spearman].Speed / 2, fast.Movements[0].ArrivalTime, precision: 10);
    }

    [Fact]
    public void ReturnRestoresTroopsAndDepositsLootCappedByTheWarehouse()
    {
        var cap = BuildingRules.VillageCapacity(Data, _home);
        _home.Resources = new Resources(cap - 10, 0, 0);
        var before = _home.TroopCount(UnitType.Spearman);
        _state.AddMovement(new Movement(50, Owner, Enemy, Home, MovementType.Return, Troops(UnitType.Spearman, 4), new Resources(100, 30, 0), 0, 5, null));

        var events = _simulation.Advance(5);

        Assert.Equal(before + 4, _home.TroopCount(UnitType.Spearman));
        Assert.Equal(cap, _home.Resources.Wood);
        Assert.Equal(30 + 5 * BuildingRules.VillageProduction(Data, _home).Stone, _home.Resources.Stone, precision: 10);
        Assert.Equal(new TroopsReturned(5, Home, new Resources(100, 30, 0)), Assert.Single(events));
        Assert.Empty(_state.Movements);
    }

    [Fact]
    public void ReinforcementsJoinTheGarrison()
    {
        var second = new Village(9, Owner, new TileCoord(0, 0));
        _state.AddVillage(second);

        Assert.True(_handler.Handle(Owner, new SendTroopsCommand(Home, 9, MovementType.Reinforce, Troops(UnitType.Spearman, 4))).Success);
        var events = _simulation.Advance(TestMatch.OneHour);

        Assert.Equal(4, second.TroopCount(UnitType.Spearman));
        Assert.Contains(events, e => e is ReinforcementsArrived);
        Assert.Empty(_state.Movements);
    }

    [Fact]
    public void ArrivalsAfterTheMatchEndDoNotResolve()
    {
        var shortMatch = TestMatch.Create(duration: 5);
        new CommandHandler(shortMatch, Data).Handle(Owner, new SendTroopsCommand(Home, Enemy, MovementType.Attack, Troops(UnitType.Spearman, 1)));

        var events = new Simulation(shortMatch, Data).Advance(TestMatch.OneHour);

        Assert.Empty(events);
        Assert.Single(shortMatch.Movements);
    }

    [Fact]
    public void RejectsWhenMatchIsOver()
    {
        _state.ElapsedTime = _state.Settings.MatchDurationSeconds;
        AssertRejected(CommandRejection.MatchOver, Attack());
    }

    [Fact]
    public void RejectsMissingSourceVillage() =>
        AssertRejected(CommandRejection.VillageNotFound, new SendTroopsCommand(999, Enemy, MovementType.Attack, Troops(UnitType.Cavalry, 1)));

    [Fact]
    public void RejectsMissingTargetVillage() => AssertRejected(CommandRejection.VillageNotFound, Attack(to: 999));

    [Fact]
    public void RejectsAnotherPlayersVillage() => AssertRejected(CommandRejection.NotOwner, Attack(), issuer: 2);

    [Theory]
    [InlineData(MovementType.Attack)]
    [InlineData(MovementType.Raid)]
    public void RejectsAttackingYourOwnVillage(MovementType type) =>
        AssertRejected(CommandRejection.InvalidMission, new SendTroopsCommand(Home, Home, type, Troops(UnitType.Cavalry, 1)));

    [Fact]
    public void RejectsReinforcingAnotherPlayer() =>
        AssertRejected(CommandRejection.InvalidMission, new SendTroopsCommand(Home, Enemy, MovementType.Reinforce, Troops(UnitType.Cavalry, 1)));

    [Fact]
    public void RejectsReinforcingTheSameVillage() =>
        AssertRejected(CommandRejection.InvalidMission, new SendTroopsCommand(Home, Home, MovementType.Reinforce, Troops(UnitType.Cavalry, 1)));

    [Fact]
    public void RejectsSendingAReturnMovement() =>
        AssertRejected(CommandRejection.InvalidMission, new SendTroopsCommand(Home, Enemy, MovementType.Return, Troops(UnitType.Cavalry, 1)));

    [Fact]
    public void RejectsNegativeCounts() => AssertRejected(CommandRejection.InvalidCount, Attack(count: -1));

    [Fact]
    public void RejectsNoTroops()
    {
        AssertRejected(CommandRejection.NoTroopsSelected, Attack(count: 0));
        AssertRejected(CommandRejection.NoTroopsSelected, new SendTroopsCommand(Home, Enemy, MovementType.Attack, new Dictionary<UnitType, int>()));
    }

    [Fact]
    public void RejectsMoreTroopsThanPresent() => AssertRejected(CommandRejection.NotEnoughTroops, Attack(count: 11));

    [Fact]
    public void RejectsCatapultsOnARaid() =>
        AssertRejected(CommandRejection.CatapultsCannotRaid, new SendTroopsCommand(Home, Enemy, MovementType.Raid, Troops(UnitType.Catapult, 1)));

    [Fact]
    public void RejectsACatapultTargetWithoutCatapults() =>
        AssertRejected(CommandRejection.InvalidMission, new SendTroopsCommand(Home, Enemy, MovementType.Attack, Troops(UnitType.Cavalry, 1), BuildingType.Wall));

    [Fact]
    public void PreviewAndHandlerAgree()
    {
        var commands = new[]
        {
            Attack(),
            Attack(count: 0),
            Attack(count: 11),
            Attack(to: 999),
            new SendTroopsCommand(Home, Home, MovementType.Attack, Troops(UnitType.Cavalry, 1)),
            new SendTroopsCommand(Home, Enemy, MovementType.Raid, Troops(UnitType.Catapult, 1)),
            new SendTroopsCommand(Home, Enemy, MovementType.Attack, Troops(UnitType.Catapult, 1), BuildingType.Wall),
        };

        foreach (var command in commands)
        foreach (var player in new[] { Owner, 2 })
        {
            var state = TestMatch.Create();
            foreach (var unit in Enum.GetValues<UnitType>())
                state.Villages[Home].AddTroops(unit, 10);

            var preview = SendPreview.Evaluate(state, Data, player, command);
            var result = new CommandHandler(state, Data).Handle(player, command);

            Assert.Equal(preview.Rejection, result.Rejection);
            if (preview.CanSend)
                Assert.Equal(preview.ArrivalTime, state.Movements[0].ArrivalTime);
        }
    }

    [Fact]
    public void TrainingFinishingAtTheSameMomentAsAnArrivalGoesFirst()
    {
        // Equal times: the batch must be home before anything arrives, so it can defend.
        _home.EnqueueTraining(new TrainingQueueItem(UnitType.Spearman, 1, 0, 5));
        _state.AddMovement(new Movement(50, Owner, Enemy, Home, MovementType.Return, Troops(UnitType.Spearman, 1), Resources.Zero, 0, 5, null));

        var events = _simulation.Advance(5);

        Assert.IsType<TroopsTrained>(events[0]);
        Assert.IsType<TroopsReturned>(events[1]);
    }

    [Fact]
    public void MovementsInProgressRoundTrip()
    {
        _handler.Handle(Owner, new SendTroopsCommand(Home, Enemy, MovementType.Attack, Troops(UnitType.Catapult, 1), BuildingType.Wall));
        _simulation.Advance(3);

        var restored = SnapshotAssert.RoundTrips(_state);

        Assert.Equal(BuildingType.Wall, restored.Movements[0].CatapultTarget);
    }
}
