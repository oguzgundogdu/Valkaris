using Valkaris.Core.Commands;
using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class FoundingTests
{
    private const int Owner = 1;
    private const int Home = 1;

    private readonly GameState _state = TestMatch.Create();
    private readonly CommandHandler _handler;
    private readonly Simulation _simulation;

    public FoundingTests()
    {
        _handler = new CommandHandler(_state, TestData.Shipped);
        _simulation = new Simulation(_state, TestData.Shipped);
        _state.Villages[Home].AddTroops(UnitType.Settler, 3);
        _state.Villages[Home].SetBuildingLevel(BuildingType.MainBuilding, 10);
        foreach (var tile in _state.Map.Tiles)
            tile.Terrain = Terrain.Grass;
    }

    /// <summary>The first tile where a village may be founded right now.</summary>
    private TileCoord FreeTile() =>
        AllTiles().First(t => FoundPreview.TileRejection(_state, TestData.Shipped, t) is null);

    private IEnumerable<TileCoord> AllTiles() =>
        from x in Enumerable.Range(0, _state.Map.Width)
        from y in Enumerable.Range(0, _state.Map.Height)
        select new TileCoord(x, y);

    private CommandResult Found(TileCoord tile) => _handler.Handle(Owner, new FoundVillageCommand(Home, tile));

    private void AssertRejected(CommandRejection expected, TileCoord tile) => Assert.Equal(expected, Found(tile).Rejection);

    private IReadOnlyList<GameEvent> RunUntilNoMovements()
    {
        var events = new List<GameEvent>();
        while (_state.Movements.Count > 0)
            events.AddRange(_simulation.Advance(_state.Movements.Min(m => m.ArrivalTime) - _state.ElapsedTime + 0.001));
        return events;
    }

    [Fact]
    public void SettlerLeavesAndIsConsumedOnArrival()
    {
        var tile = FreeTile();
        var before = _state.Villages.Count;

        Assert.True(Found(tile).Success);
        Assert.Equal(2, _state.Villages[Home].TroopCount(UnitType.Settler));
        var movement = Assert.Single(_state.Movements);
        Assert.Equal(MovementType.Settle, movement.Type);
        Assert.Null(movement.ToVillageId);

        var events = RunUntilNoMovements();

        Assert.Single(events.OfType<VillageFounded>());
        Assert.Equal(before + 1, _state.Villages.Count);
        Assert.Equal(2, _state.Villages[Home].TroopCount(UnitType.Settler));
        var village = _state.Villages[_state.Map.TileAt(tile).VillageId!.Value];
        Assert.Equal(Owner, village.OwnerId);
        Assert.Equal(TestData.Shipped.Game.FoundedVillage.Resources.Wood, village.Resources.Wood, 0);
        Assert.Equal(Village.MaxLoyalty, village.Loyalty);
        Assert.Equal(TestData.Shipped.Game.FoundedVillage.Buildings[BuildingType.Farm], village.BuildingLevel(BuildingType.Farm));
        Assert.Contains(village.Name, TestData.Shipped.Map.FoundedVillageNames);
    }

    [Fact]
    public void OutsideTheMapIsRejected() => AssertRejected(CommandRejection.TileOutsideMap, new TileCoord(-1, 0));

    [Fact]
    public void WaterIsRejected()
    {
        var tile = FreeTile();
        _state.Map.TileAt(tile).Terrain = Terrain.Water;

        AssertRejected(CommandRejection.TileIsWater, tile);
    }

    [Fact]
    public void AVillageTileIsRejected() =>
        AssertRejected(CommandRejection.TileTaken, _state.Villages[2].Position);

    [Fact]
    public void AnotherPlayersVillageCannotSend() =>
        Assert.Equal(CommandRejection.NotOwner, _handler.Handle(2, new FoundVillageCommand(Home, FreeTile())).Rejection);

    [Fact]
    public void NoSettlerIsRejected()
    {
        _state.Villages[Home].RemoveTroops(UnitType.Settler, 3);

        AssertRejected(CommandRejection.NoSettlerHere, FreeTile());
    }

    [Theory]
    [InlineData(2.9, true)]
    [InlineData(3.0, true)]
    [InlineData(3.1, false)]
    public void MinimumDistanceApplies(double limit, bool allowed)
    {
        var data = TestData.Mutated("map.json", r => r["minFoundingDistance"] = limit);
        var tile = AllTiles().First(t => _state.Villages.Values.Min(v => UnitRules.Distance(v.Position, t)) == 3);

        var rejection = FoundPreview.TileRejection(_state, data, tile);

        Assert.Equal(allowed ? null : CommandRejection.TooCloseToVillage, rejection);
    }

    [Fact]
    public void TooCloseToAnyVillageIsRejected()
    {
        var target = _state.Villages[2].Position;
        var near = AllTiles().First(t => t != target && UnitRules.Distance(t, target) < TestData.Shipped.Map.MinFoundingDistance);

        AssertRejected(CommandRejection.TooCloseToVillage, near);
    }

    [Fact]
    public void LimitCountsSettlersInFlight()
    {
        _state.Villages[Home].SetBuildingLevel(BuildingType.MainBuilding, 2); // limit 2, owns 1

        Assert.True(Found(FreeTile()).Success);
        AssertRejected(CommandRejection.VillageLimitReached, FreeTile());
    }

    [Fact]
    public void LimitComesFromTheCapitalNotTheSendingVillage()
    {
        _state.Villages[Home].SetBuildingLevel(BuildingType.MainBuilding, 1); // limit 1

        AssertRejected(CommandRejection.VillageLimitReached, FreeTile());
    }

    [Fact]
    public void TileTakenInFlightSendsTheSettlerHome()
    {
        var tile = FreeTile();
        Assert.True(Found(tile).Success);
        Assert.True(Found(tile).Success); // both set off: the tile is free until one arrives

        var events = RunUntilNoMovements();

        Assert.Single(events.OfType<VillageFounded>());
        Assert.Equal(CommandRejection.TileTaken, Assert.Single(events.OfType<SettlingFailed>()).Reason);
        Assert.Equal(2, _state.Villages[Home].TroopCount(UnitType.Settler));
    }

    [Fact]
    public void ANewVillageTooCloseInFlightSendsTheSettlerHome()
    {
        var free = AllTiles().Where(t => FoundPreview.TileRejection(_state, TestData.Shipped, t) is null).ToList();
        var home = _state.Villages[Home].Position;
        var (first, second) = (from a in free
                               from b in free
                               where UnitRules.Distance(a, b) < TestData.Shipped.Map.MinFoundingDistance
                                   && UnitRules.Distance(a, home) < UnitRules.Distance(b, home)
                               select (a, b)).First();
        Assert.True(Found(first).Success);
        Assert.True(Found(second).Success);

        var events = RunUntilNoMovements();

        Assert.Equal(CommandRejection.TooCloseToVillage, Assert.Single(events.OfType<SettlingFailed>()).Reason);
    }

    [Fact]
    public void LimitDroppingInFlightSendsTheSettlerHome()
    {
        var tile = FreeTile();
        Assert.True(Found(tile).Success);
        _state.Villages[Home].SetBuildingLevel(BuildingType.MainBuilding, 1);

        var events = RunUntilNoMovements();

        Assert.Equal(CommandRejection.VillageLimitReached, Assert.Single(events.OfType<SettlingFailed>()).Reason);
        Assert.Null(_state.Map.TileAt(tile).VillageId);
        Assert.Equal(3, _state.Villages[Home].TroopCount(UnitType.Settler));
    }

    [Fact]
    public void LosingTheCapitalInFlightSendsTheSettlerHome()
    {
        Assert.True(Found(FreeTile()).Success);
        _state.Players[Owner].CapitalVillageId = null;

        var events = RunUntilNoMovements();

        Assert.Equal(CommandRejection.NoCapital, Assert.Single(events.OfType<SettlingFailed>()).Reason);
    }

    [Fact]
    public void VillagesAboveTheLimitAreKept()
    {
        Assert.True(Found(FreeTile()).Success);
        RunUntilNoMovements();
        _state.Villages[Home].SetBuildingLevel(BuildingType.MainBuilding, 1);

        _simulation.Advance(10);

        Assert.Equal(2, _state.Villages.Values.Count(v => v.OwnerId == Owner));
    }

    [Fact]
    public void NamesStayUniqueAndNumberWhenTheListRunsOut()
    {
        var data = TestData.Mutated("map.json", r => r["foundedVillageNames"] = new System.Text.Json.Nodes.JsonArray("Only"));
        var simulation = new Simulation(_state, data);
        var handler = new CommandHandler(_state, data);
        Assert.True(handler.Handle(Owner, new FoundVillageCommand(Home, FreeTile())).Success);
        Assert.True(handler.Handle(Owner, new FoundVillageCommand(Home, AllTiles().Last(t => FoundPreview.TileRejection(_state, data, t) is null))).Success);

        simulation.Advance(TestMatch.OneHour - 1);

        var founded = _state.Villages.Values.Where(v => v.Name.StartsWith("Only")).Select(v => v.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "Only", "Only 2" }, founded);
    }

    [Fact]
    public void FoundingRoundTripsThroughASnapshot()
    {
        Assert.True(Found(FreeTile()).Success);

        var restored = SnapshotAssert.RoundTrips(_state);

        new Simulation(restored, TestData.Shipped).Advance(TestMatch.OneHour - 1);
        _simulation.Advance(TestMatch.OneHour - 1);
        Assert.Equal(_state.Villages.Count, restored.Villages.Count);
        Assert.Equal(_state.Villages.Values.Last().Name, restored.Villages.Values.Last().Name);
    }

    [Theory]
    [InlineData(UnitType.Settler, MovementType.Attack, CommandRejection.SettlersCannotFight)]
    [InlineData(UnitType.Settler, MovementType.Raid, CommandRejection.SettlersCannotFight)]
    [InlineData(UnitType.Lord, MovementType.Raid, CommandRejection.LordsCannotRaid)]
    public void MissionRules(UnitType unit, MovementType mission, CommandRejection expected)
    {
        _state.Villages[Home].AddTroops(unit, 1);
        var troops = new Dictionary<UnitType, int> { [unit] = 1 };

        Assert.Equal(expected, _handler.Handle(Owner, new SendTroopsCommand(Home, 2, mission, troops)).Rejection);
    }

    [Fact]
    public void SettleIsNotAValidMissionForSendTroops()
    {
        var troops = new Dictionary<UnitType, int> { [UnitType.Settler] = 1 };

        Assert.Equal(CommandRejection.InvalidMission, _handler.Handle(Owner, new SendTroopsCommand(Home, 2, MovementType.Settle, troops)).Rejection);
    }
}
