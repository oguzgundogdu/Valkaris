using Valkaris.Core.Commands;
using Valkaris.Core.Events;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class GodModeTests
{
    private const int Local = 1;
    private const int Rival = 2;

    private static readonly Valkaris.Core.Data.GameData Data = TestData.Shipped;

    private readonly GameState _state = TestMatch.Create(duration: 36000);
    private readonly LocalGameSession _session;
    private readonly Village _home;

    public GodModeTests()
    {
        _session = new LocalGameSession(_state, Data, Local);
        _home = _state.Villages[Local];
    }

    private void Build(BuildingType building) => Assert.True(_session.SendAs(Local, new BuildCommand(Local, building)).Success);

    private void Upgrades(BuildingType building, int times)
    {
        for (var i = 0; i < times; i++)
            Build(building);
    }

    // Upgrading.

    [Fact]
    public void AnUpgradeIsFreeAndInstant()
    {
        _session.SetGodMode(true);
        var stock = _home.Resources;
        var level = _home.BuildingLevel(BuildingType.Woodcutter);
        var time = _state.ElapsedTime;

        var option = BuildPreview.Evaluate(_state, Data, Local, Local, BuildingType.Woodcutter);
        Build(BuildingType.Woodcutter);

        Assert.Equal(Resources.Zero, option.Cost);
        Assert.Equal(0, option.DurationSeconds);
        Assert.False(option.CostExceedsCapacity);
        Assert.Equal(level + 1, _home.BuildingLevel(BuildingType.Woodcutter));
        Assert.True(_home.Resources.Wood >= stock.Wood);
        Assert.Empty(_home.BuildQueue);
        Assert.Equal(time, _state.ElapsedTime);
    }

    [Fact]
    public void BuildingsKeepTheirLevelsWhenItIsTurnedOn()
    {
        var before = _home.Buildings.ToDictionary(b => b.Key, b => b.Value);

        _session.SetGodMode(true);

        Assert.Equal(before, _home.Buildings.ToDictionary(b => b.Key, b => b.Value));
    }

    [Fact]
    public void ThereIsNoQueueLimit()
    {
        _session.SetGodMode(true);

        Upgrades(BuildingType.MainBuilding, Data.Game.MaxBuildQueueLength + 3);

        Assert.Equal(1 + Data.Game.MaxBuildQueueLength + 3, _home.BuildingLevel(BuildingType.MainBuilding));
    }

    [Fact]
    public void TheMainBuildingLimitStillApplies()
    {
        _session.SetGodMode(true);
        var limit = BuildingRules.MaxOtherBuildingLevel(Data, _home.BuildingLevel(BuildingType.MainBuilding));
        while (_home.BuildingLevel(BuildingType.Woodcutter) < limit)
            Build(BuildingType.Woodcutter);

        var option = BuildPreview.Evaluate(_state, Data, Local, Local, BuildingType.Woodcutter);

        Assert.Equal(CommandRejection.MainBuildingLevelTooLow, option.Rejection);
        Assert.Equal(CommandRejection.MainBuildingLevelTooLow, _session.SendAs(Local, new BuildCommand(Local, BuildingType.Woodcutter)).Rejection);
    }

    [Fact]
    public void TheMaxLevelStillApplies()
    {
        _session.SetGodMode(true);
        var max = Data.Buildings.Buildings[BuildingType.MainBuilding].MaxLevel;
        Upgrades(BuildingType.MainBuilding, max - _home.BuildingLevel(BuildingType.MainBuilding));

        Assert.Equal(CommandRejection.MaxLevelReached, _session.SendAs(Local, new BuildCommand(Local, BuildingType.MainBuilding)).Rejection);
    }

    [Fact]
    public void BuildingCompletedIsAnnounced()
    {
        var events = new List<GameEvent>();
        _session.EventRaised += events.Add;
        _session.SetGodMode(true);

        Build(BuildingType.Farm);

        Assert.Single(events.OfType<BuildingCompleted>());
    }

    // Resources.

    private void AssertFull(Village village)
    {
        var capacity = BuildingRules.VillageCapacity(Data, village);
        Assert.Equal(new Resources(capacity, capacity, capacity), village.Resources);
    }

    [Fact]
    public void ResourcesAreFullFromTheMomentItIsOn()
    {
        _session.SetGodMode(true);

        AssertFull(_home);
    }

    [Fact]
    public void SpendingNeverReducesThem()
    {
        _session.SetGodMode(true);
        _home.SetBuildingLevel(BuildingType.Marketplace, 3);
        var second = new Village(50, Local, new TileCoord(_home.Position.X + 3, _home.Position.Y));
        _state.AddVillage(second);

        Build(BuildingType.Farm);
        Assert.True(_session.SendAs(Local, new SendResourcesCommand(Local, second.Id, 100, 0, 0)).Success);

        AssertFull(_home);
    }

    [Fact]
    public void AWarehouseUpgradeFillsTheLargerCapacity()
    {
        _session.SetGodMode(true);

        Build(BuildingType.MainBuilding);
        Build(BuildingType.Warehouse);

        AssertFull(_home);
    }

    [Fact]
    public void ANewVillageIsFullOnTheNextTick()
    {
        _session.SetGodMode(true);
        var second = new Village(50, Local, new TileCoord(_home.Position.X + 3, _home.Position.Y));
        second.SetBuildingLevel(BuildingType.Warehouse, 1);
        _state.AddVillage(second);

        _session.Tick(0.1);

        AssertFull(second);
    }

    [Fact]
    public void OtherPlayersResourcesAreNotTouched()
    {
        var rival = _state.Villages[Rival];
        var stock = rival.Resources;

        _session.SetGodMode(true);

        Assert.Equal(stock, rival.Resources);
    }

    [Fact]
    public void TurningItOffLeavesTheStockAndSpendingCounts()
    {
        _session.SetGodMode(true);
        Build(BuildingType.Farm);
        _session.SetGodMode(false);
        var stock = _home.Resources;

        Build(BuildingType.Quarry);

        Assert.True(_home.Resources.Wood < stock.Wood);
    }

    // Training.

    private void WithBarracks(int level = 1) => _home.SetBuildingLevel(BuildingType.Barracks, level);

    [Fact]
    public void TrainingIsFreeAndInstant()
    {
        WithBarracks();
        _session.SetGodMode(true);
        var spears = _home.TroopCount(UnitType.Spearman);
        var time = _state.ElapsedTime;

        var option = TrainPreview.Evaluate(_state, Data, Local, Local, UnitType.Spearman, 5000);
        Assert.True(_session.SendAs(Local, new TrainCommand(Local, UnitType.Spearman, 5000)).Success);

        Assert.Equal(Resources.Zero, option.Cost);
        Assert.Equal(0, option.DurationSeconds);
        Assert.Equal(int.MaxValue, option.MaxCount);
        Assert.Equal(spears + 5000, _home.TroopCount(UnitType.Spearman));
        Assert.Empty(_home.TrainingQueue);
        Assert.Equal(time, _state.ElapsedTime);
        AssertFull(_home);
    }

    [Fact]
    public void TrainingHasNoQueueLimit()
    {
        WithBarracks();
        _session.SetGodMode(true);

        for (var i = 0; i < Data.Game.MaxTrainingQueueLength + 3; i++)
            Assert.True(_session.SendAs(Local, new TrainCommand(Local, UnitType.Spearman, 1)).Success);
    }

    [Fact]
    public void TheBarracksLevelStillApplies()
    {
        WithBarracks();
        _session.SetGodMode(true);

        Assert.Equal(CommandRejection.BarracksLevelTooLow, _session.SendAs(Local, new TrainCommand(Local, UnitType.Lord, 1)).Rejection);
    }

    [Fact]
    public void TurningItOffRestoresTrainingRules()
    {
        WithBarracks();
        _session.SetGodMode(true);
        _session.SetGodMode(false);

        var option = TrainPreview.Evaluate(_state, Data, Local, Local, UnitType.Spearman, 1);

        Assert.Equal(Data.Units.Units[UnitType.Spearman].Cost, option.Cost);
        Assert.True(option.DurationSeconds > 0);
    }

    [Fact]
    public void TheRivalsTrainingKeepsItsCostAndTime()
    {
        _state.Villages[Rival].SetBuildingLevel(BuildingType.Barracks, 1);
        _session.SetGodMode(true);

        var option = TrainPreview.Evaluate(_state, Data, Rival, Rival, UnitType.Spearman, 1);

        Assert.NotEqual(Resources.Zero, option.Cost);
        Assert.True(option.DurationSeconds > 0);
    }

    // Movements.

    private Village Second()
    {
        var second = new Village(50, Local, new TileCoord(_home.Position.X + 3, _home.Position.Y));
        second.SetBuildingLevel(BuildingType.Warehouse, 1);
        _state.AddVillage(second);
        return second;
    }

    private void SendLocal(MovementType type, int to, UnitType unit, int count)
    {
        _home.AddTroops(unit, count);
        Assert.True(_session.SendAs(Local, new SendTroopsCommand(Local, to, type, new Dictionary<UnitType, int> { [unit] = count })).Success);
    }

    [Theory]
    [InlineData(MovementType.Attack)]
    [InlineData(MovementType.Raid)]
    public void AttacksAndRaidsArriveAndReturnInstantly(MovementType type)
    {
        _session.SetGodMode(true);
        var sword = _home.TroopCount(UnitType.Swordsman);
        var time = _state.ElapsedTime;

        SendLocal(type, Rival, UnitType.Swordsman, 500);

        Assert.Single(_state.Reports);
        Assert.Empty(_state.Movements);
        Assert.True(_home.TroopCount(UnitType.Swordsman) > sword);
        Assert.Equal(time, _state.ElapsedTime);
    }

    [Fact]
    public void ScoutsArriveAndReturnInstantly()
    {
        _session.SetGodMode(true);

        SendLocal(MovementType.Scout, Rival, UnitType.Scout, 3);

        Assert.True(_state.ScoutReports.Single().Succeeded);
        Assert.Empty(_state.Movements);
        Assert.Equal(3, _home.TroopCount(UnitType.Scout));
    }

    [Fact]
    public void ReinforcementsArriveInstantly()
    {
        _session.SetGodMode(true);
        var second = Second();

        SendLocal(MovementType.Reinforce, second.Id, UnitType.Spearman, 4);

        Assert.Equal(4, second.TroopCount(UnitType.Spearman));
        Assert.Empty(_state.Movements);
    }

    [Fact]
    public void TransfersAndTheirCarriersAreInstant()
    {
        _session.SetGodMode(true);
        _home.SetBuildingLevel(BuildingType.Marketplace, 1);
        var second = Second();
        var carriers = CarrierRules.Free(_state, Data, _home);

        Assert.True(_session.SendAs(Local, new SendResourcesCommand(Local, second.Id, 10, 0, 0)).Success);

        Assert.Empty(_state.Movements);
        Assert.Equal(carriers, CarrierRules.Free(_state, Data, _home));
    }

    [Fact]
    public void SettlersFoundAVillageInstantly()
    {
        _session.SetGodMode(true);
        for (var i = 0; i < 3; i++)
            Build(BuildingType.MainBuilding);
        _home.AddTroops(UnitType.Settler, 1);
        var count = _state.Villages.Count;
        var tile = _state.Map.Tiles.Select((t, i) => (t, new TileCoord(i % _state.Map.Width, i / _state.Map.Width)))
            .Where(x => FoundPreview.TileRejection(_state, Data, x.Item2) is null)
            .Select(x => x.Item2)
            .First();

        Assert.True(_session.SendAs(Local, new FoundVillageCommand(Local, tile)).Success);

        Assert.Equal(count + 1, _state.Villages.Count);
        Assert.Empty(_state.Movements);
    }

    [Fact]
    public void PreviewsAgreeThatTravelTakesNoTime()
    {
        _session.SetGodMode(true);
        _home.AddTroops(UnitType.Swordsman, 1);

        var option = SendPreview.Evaluate(_state, Data, Local, new SendTroopsCommand(Local, Rival, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 1 }));

        Assert.Equal(0, option.DurationSeconds);
        Assert.Equal(_state.ElapsedTime, option.ArrivalTime);
    }

    [Fact]
    public void ARivalsMovementKeepsItsSpeed()
    {
        _session.SetGodMode(true);
        _state.Villages[Rival].AddTroops(UnitType.Swordsman, 5);
        var expected = UnitRules.TravelSeconds(Data, _state, _state.Villages[Rival].Position, _home.Position, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 5 });

        Assert.True(_session.SendAs(Rival, new SendTroopsCommand(Rival, Local, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 5 })).Success);

        var movement = _state.Movements.Single();
        Assert.Equal(_state.ElapsedTime + expected, movement.ArrivalTime, 6);
        Assert.Empty(_state.Reports);
    }

    [Fact]
    public void ARivalsReturnKeepsItsSpeed()
    {
        _session.SetGodMode(true);
        _state.Villages[Rival].AddTroops(UnitType.Swordsman, 500);
        _session.SendAs(Rival, new SendTroopsCommand(Rival, Local, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 500 }));

        _session.Tick(_state.Movements.Single().ArrivalTime - _state.ElapsedTime + 0.001);

        Assert.True(_state.Movements.Single(m => m.OwnerId == Rival).ArrivalTime > _state.ElapsedTime);
    }

    [Fact]
    public void TurningItOffRestoresTravelTime()
    {
        _session.SetGodMode(true);
        _session.SetGodMode(false);
        _home.AddTroops(UnitType.Swordsman, 1);

        var option = SendPreview.Evaluate(_state, Data, Local, new SendTroopsCommand(Local, Rival, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 1 }));

        Assert.True(option.DurationSeconds > 0);
    }

    [Fact]
    public void ABattleGoesTheSameWayWithGodMode()
    {
        BattleReport Fought(bool god)
        {
            var state = TestMatch.Create(duration: 36000);
            var session = new LocalGameSession(state, Data, Local);
            state.Villages[Local].AddTroops(UnitType.Swordsman, 60);
            var command = new SendTroopsCommand(Local, Rival, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 60 });
            if (god)
                session.SetGodMode(true);
            Assert.True(session.SendAs(Local, command).Success);
            if (!god)
                session.Tick(state.Movements.Single().ArrivalTime - state.ElapsedTime);
            return state.Reports.Single();
        }

        var normal = Fought(false);
        var withGod = Fought(true);

        Assert.Equal(normal.AttackPower, withGod.AttackPower);
        Assert.Equal(normal.DefencePower, withGod.DefencePower);
        Assert.Equal(normal.AttackerWon, withGod.AttackerWon);
        Assert.Equal(normal.AttackerLost, withGod.AttackerLost);
        Assert.Equal(normal.DefenderLost, withGod.DefenderLost);
    }

    // Work under way.

    [Fact]
    public void TurningItOnFinishesQueuedBuilds()
    {
        _home.Resources = new Resources(5000, 5000, 5000);
        Build(BuildingType.MainBuilding);
        Build(BuildingType.MainBuilding);

        _session.SetGodMode(true);

        Assert.Empty(_home.BuildQueue);
        Assert.Equal(3, _home.BuildingLevel(BuildingType.MainBuilding));
    }

    [Fact]
    public void AnOldQueueCantLowerAnInstantUpgrade()
    {
        _home.Resources = new Resources(5000, 5000, 5000);
        Build(BuildingType.MainBuilding);

        _session.SetGodMode(true);
        Build(BuildingType.MainBuilding);
        _session.Tick(600);

        Assert.Equal(3, _home.BuildingLevel(BuildingType.MainBuilding));
    }

    [Fact]
    public void TurningItOnFinishesTrainingAndMovements()
    {
        _home.Resources = new Resources(5000, 5000, 5000);
        _home.SetBuildingLevel(BuildingType.Barracks, 1);
        _home.AddTroops(UnitType.Scout, 1);
        var spears = _home.TroopCount(UnitType.Spearman);
        Assert.True(_session.SendAs(Local, new TrainCommand(Local, UnitType.Spearman, 2)).Success);
        Assert.True(_session.SendAs(Local, new SendTroopsCommand(Local, Rival, MovementType.Scout, new Dictionary<UnitType, int> { [UnitType.Scout] = 1 })).Success);

        _session.SetGodMode(true);

        Assert.Equal(spears + 2, _home.TroopCount(UnitType.Spearman));
        Assert.Single(_state.ScoutReports);
    }

    // Turning it off, and who it applies to.

    [Fact]
    public void TurningItOffKeepsLevelsAndRestoresTheRules()
    {
        _session.SetGodMode(true);
        Upgrades(BuildingType.MainBuilding, 3);

        _session.SetGodMode(false);

        Assert.Equal(4, _home.BuildingLevel(BuildingType.MainBuilding));
        var option = BuildPreview.Evaluate(_state, Data, Local, Local, BuildingType.MainBuilding);
        Assert.Equal(BuildingRules.UpgradeCost(Data, BuildingType.MainBuilding, 5), option.Cost);
        Assert.True(option.DurationSeconds > 0);
    }

    [Fact]
    public void OtherPlayersKeepNormalRules()
    {
        _session.SetGodMode(true);

        var option = BuildPreview.Evaluate(_state, Data, Rival, Rival, BuildingType.Farm);

        Assert.True(option.DurationSeconds > 0);
        Assert.NotEqual(Resources.Zero, option.Cost);
        Assert.False(GodRules.Applies(_state, Rival));
    }

    [Fact]
    public void ItCantBeToggledWhilePaused()
    {
        _session.SetPaused(true);

        Assert.False(_session.SetGodMode(true));
        Assert.False(_session.IsGodMode);
    }

    [Fact]
    public void ItIsOffByDefault()
    {
        Assert.False(_session.IsGodMode);
        Assert.False(GodRules.Applies(_state, Local));
    }
}
