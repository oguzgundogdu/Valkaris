using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class BarbarianTests
{
    private const int Owner = 1;
    private const int Home = 1;

    private static readonly GameData Data = TestData.Shipped;
    private static readonly BarbarianConfig Config = Data.Map.Barbarians;

    private readonly GameState _state;
    private readonly Simulation _simulation;
    private readonly Village _camp;

    public BarbarianTests()
    {
        _state = TestMatch.Create();
        _simulation = new Simulation(_state, Data);
        _camp = FirstOfTier(_state, 0);
    }

    private static Village FirstOfTier(GameState state, int tier)
    {
        // Seeds decide the tiers, so make the village under test the tier we want.
        var village = state.Villages.Values.First(v => v.IsBarbarian);
        village.BarbarianTier = tier;
        var config = Data.Map.Barbarians.Tiers[tier];
        village.Resources = config.Resources;
        foreach (var unit in Enum.GetValues<UnitType>())
            village.RemoveTroops(unit, village.TroopCount(unit));
        foreach (var (unit, count) in config.Garrison)
            village.AddTroops(unit, count);
        return village;
    }

    private static Dictionary<UnitType, int> Force(UnitType unit, int count) => new() { [unit] = count };

    private static int GarrisonSize(Village village) => village.Troops.Values.Sum();

    private BattleReport Fight(MovementType type, UnitType unit, int count)
    {
        _state.Villages[Home].AddTroops(unit, count);
        Assert.True(new CommandHandler(_state, Data).Handle(Owner, new SendTroopsCommand(Home, _camp.Id, type, Force(unit, count))).Success);
        _simulation.Advance(_state.Movements[^1].ArrivalTime - _state.ElapsedTime);
        return _state.Reports[^1];
    }

    // Recovery: resources.

    [Fact]
    public void AnUntouchedCampDoesNotChange()
    {
        var resources = _camp.Resources;
        var garrison = GarrisonSize(_camp);

        _simulation.Advance(1000);

        Assert.Equal(resources, _camp.Resources);
        Assert.Equal(garrison, GarrisonSize(_camp));
        Assert.Null(_camp.NextTroopRegenTime);
    }

    [Fact]
    public void ResourcesRegenerateAtTheRateFromData()
    {
        _camp.Resources = new Resources(10, 20, 30);

        _simulation.Advance(50);

        var gain = Config.ResourceRegenPerSecond * 50;
        Assert.Equal(new Resources(10 + gain, 20 + gain, 30 + gain), _camp.Resources);
    }

    [Fact]
    public void ResourcesStopExactlyAtTheTierCap()
    {
        _camp.Resources = Resources.Zero;

        _simulation.Advance(100_000 / 100.0 * 3);

        Assert.Equal(Data.Map.Barbarians.Tiers[0].Resources, _camp.Resources);
    }

    [Fact]
    public void EachResourceStopsAtItsOwnCap()
    {
        var cap = Data.Map.Barbarians.Tiers[0].Resources;
        _camp.Resources = new Resources(cap.Wood, 0, cap.Food - 1);

        _simulation.Advance(5);

        Assert.Equal(cap.Wood, _camp.Resources.Wood);
        Assert.Equal(5 * Config.ResourceRegenPerSecond, _camp.Resources.Stone);
        Assert.Equal(cap.Food, _camp.Resources.Food);
    }

    [Fact]
    public void ResourceRegenAppliesTheGameSpeed()
    {
        var fast = TestMatch.Create(speed: 3);
        var camp = FirstOfTier(fast, 0);
        camp.Resources = Resources.Zero;

        new Simulation(fast, Data).Advance(10);

        Assert.Equal(30 * Config.ResourceRegenPerSecond, camp.Resources.Wood);
    }

    [Fact]
    public void ResourceRateComesFromData()
    {
        var data = TestData.Mutated(GameData.MapFile, root => root["barbarians"]!["resourceRegenPerSecond"] = 7);
        var state = TestMatch.Create(data: data);
        var camp = state.Villages.Values.First(v => v.IsBarbarian);
        camp.Resources = Resources.Zero;

        new Simulation(state, data).Advance(2);

        Assert.Equal(14, camp.Resources.Wood);
    }

    // Recovery: troops.

    [Fact]
    public void ALosingAttackStartsTroopRecoveryOneTroopAtATime()
    {
        var report = Fight(MovementType.Attack, UnitType.Cavalry, 100);
        Assert.True(report.AttackerWon);
        Assert.Equal(0, GarrisonSize(_camp));
        var battleTime = _state.ElapsedTime;

        Assert.Equal(battleTime + Config.TroopRegenSeconds, _camp.NextTroopRegenTime);

        _simulation.Advance(Config.TroopRegenSeconds - 0.001);
        Assert.Equal(0, GarrisonSize(_camp));

        _simulation.Advance(0.001);
        Assert.Equal(1, GarrisonSize(_camp));
        Assert.Equal(battleTime + 2 * Config.TroopRegenSeconds, _camp.NextTroopRegenTime!.Value, precision: 8);
    }

    [Fact]
    public void TroopsStopAtTheCapOfEveryUnitType()
    {
        Fight(MovementType.Attack, UnitType.Cavalry, 100);
        var tier = Data.Map.Barbarians.Tiers[0];
        var total = tier.Garrison.Values.Sum();

        _simulation.Advance(Config.TroopRegenSeconds * (total + 10));

        foreach (var (unit, cap) in tier.Garrison)
            Assert.Equal(cap, _camp.TroopCount(unit));
        Assert.Null(_camp.NextTroopRegenTime);
    }

    [Fact]
    public void TroopRegenAppliesTheGameSpeed()
    {
        var fast = TestMatch.Create(speed: 3);
        var camp = FirstOfTier(fast, 0);
        foreach (var unit in Enum.GetValues<UnitType>())
            camp.RemoveTroops(unit, camp.TroopCount(unit));
        fast.Villages[Home].AddTroops(UnitType.Cavalry, 1);
        new CommandHandler(fast, Data).Handle(Owner, new SendTroopsCommand(Home, camp.Id, MovementType.Raid, Force(UnitType.Cavalry, 1)));
        var simulation = new Simulation(fast, Data);
        simulation.Advance(fast.Movements[0].ArrivalTime);

        Assert.Equal(fast.ElapsedTime + Config.TroopRegenSeconds / 3, camp.NextTroopRegenTime!.Value, precision: 8);
    }

    [Fact]
    public void TroopRateComesFromData()
    {
        var data = TestData.Mutated(GameData.MapFile, root => root["barbarians"]!["troopRegenSeconds"] = 5);
        var state = TestMatch.Create(data: data);
        var camp = state.Villages.Values.First(v => v.IsBarbarian);
        foreach (var unit in Enum.GetValues<UnitType>())
            camp.RemoveTroops(unit, camp.TroopCount(unit));
        state.Villages[Home].AddTroops(UnitType.Cavalry, 1);
        new CommandHandler(state, data).Handle(Owner, new SendTroopsCommand(Home, camp.Id, MovementType.Raid, Force(UnitType.Cavalry, 1)));
        new Simulation(state, data).Advance(state.Movements[0].ArrivalTime);

        Assert.Equal(state.ElapsedTime + 5, camp.NextTroopRegenTime!.Value, precision: 8);
    }

    [Fact]
    public void RegenRaisesNoGameEvents()
    {
        Fight(MovementType.Attack, UnitType.Cavalry, 100);
        _state.Villages[Home].Resources = Resources.Zero;

        var events = _simulation.Advance(Config.TroopRegenSeconds * 3 + 0.5);

        Assert.DoesNotContain(events, e => e is not TroopsReturned);
    }

    [Fact]
    public void ALootedCampIsWorthRaidingAgainAfterAFewMinutes()
    {
        var first = Fight(MovementType.Raid, UnitType.Cavalry, 100);
        Assert.True(first.Loot.Wood > 0);
        var afterRaid = _camp.Resources.Wood;

        _simulation.Advance(180);

        Assert.True(_camp.Resources.Wood > afterRaid + 100);
    }

    [Fact]
    public void CatapultDamageDoesNotRecover()
    {
        _camp.SetBuildingLevel(BuildingType.Wall, 5);
        _state.Villages[Home].AddTroops(UnitType.Cavalry, 100);
        _state.Villages[Home].AddTroops(UnitType.Catapult, 1);
        var troops = new Dictionary<UnitType, int> { [UnitType.Cavalry] = 100, [UnitType.Catapult] = 1 };
        new CommandHandler(_state, Data).Handle(Owner, new SendTroopsCommand(Home, _camp.Id, MovementType.Attack, troops, BuildingType.Wall));
        _simulation.Advance(_state.Movements[0].ArrivalTime);
        Assert.Equal(4, _camp.BuildingLevel(BuildingType.Wall));

        _simulation.Advance(2000);

        Assert.Equal(4, _camp.BuildingLevel(BuildingType.Wall));
    }

    // Event-accurate stepping.

    private (GameState State, List<GameEvent> Events) PlayRecovery(double step)
    {
        var state = GameFactory.CreateMatch(new GameSettings(TestMatch.OneHour, 1, MapSize.Small, 7), TestMatch.Slots(2), Data);
        var home = state.Villages[Home];

        // Far enough that the slower infantry arrive well after the cavalry.
        var camp = state.Villages.Values.Where(v => v.IsBarbarian).OrderByDescending(v => UnitRules.Distance(v.Position, home.Position)).First();
        home.AddTroops(UnitType.Cavalry, 200);
        home.AddTroops(UnitType.Swordsman, 200);

        // Cavalry wipe the camp first; the infantry fight it mid-recovery, then everyone goes home.
        var handler = new CommandHandler(state, Data);
        Assert.True(handler.Handle(Owner, new SendTroopsCommand(Home, camp.Id, MovementType.Attack, Force(UnitType.Cavalry, 200))).Success);
        Assert.True(handler.Handle(Owner, new SendTroopsCommand(Home, camp.Id, MovementType.Raid, Force(UnitType.Swordsman, 50))).Success);

        var simulation = new Simulation(state, Data);
        var events = new List<GameEvent>();
        var steps = (int)Math.Round(600 / step);
        for (var i = 0; i < steps; i++)
            events.AddRange(simulation.Advance(step));
        return (state, events);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.25)]
    public void RecoveryGivesTheSameResultAtAnyStepSize(double step)
    {
        var (big, bigEvents) = PlayRecovery(600);
        var (small, smallEvents) = PlayRecovery(step);

        Assert.Equal(2, big.Reports.Count);
        foreach (var village in big.Villages.Values)
        {
            var other = small.Villages[village.Id];
            Assert.Equal(village.Resources.Wood, other.Resources.Wood, precision: 6);
            Assert.Equal(village.Resources.Stone, other.Resources.Stone, precision: 6);
            Assert.Equal(village.Resources.Food, other.Resources.Food, precision: 6);
            Assert.Equal(village.Troops.OrderBy(t => t.Key), other.Troops.OrderBy(t => t.Key));
            Assert.Equal(village.NextTroopRegenTime, other.NextTroopRegenTime);
        }

        Assert.Equal(big.Reports[1].DefenderPresent.OrderBy(t => t.Key), small.Reports[1].DefenderPresent.OrderBy(t => t.Key));
        Assert.Equal(big.Reports[1].Loot, small.Reports[1].Loot);
        Assert.Equal(bigEvents.Select(e => e.Time), smallEvents.Select(e => e.Time));
    }

    [Fact]
    public void TheSecondBattleMeetsTheTroopsThatRecoveredSoFar()
    {
        var (state, _) = PlayRecovery(600);

        Assert.True(state.Reports[1].DefenderPresent.Values.Sum() > 0);
    }

    [Fact]
    public void ARegenTroopFinishingAsAnAttackArrivesCanDefend()
    {
        _camp.RemoveTroops(UnitType.Spearman, _camp.TroopCount(UnitType.Spearman));
        _camp.RemoveTroops(UnitType.Swordsman, _camp.TroopCount(UnitType.Swordsman));
        _state.Villages[Home].AddTroops(UnitType.Cavalry, 1);
        new CommandHandler(_state, Data).Handle(Owner, new SendTroopsCommand(Home, _camp.Id, MovementType.Raid, Force(UnitType.Cavalry, 1)));
        _camp.NextTroopRegenTime = _state.Movements[0].ArrivalTime;

        _simulation.Advance(_state.Movements[0].ArrivalTime);

        Assert.Equal(1, _state.Reports[0].DefenderPresent[UnitType.Spearman]);
    }

    [Fact]
    public void RestoredStateRecoversLikeTheOriginal()
    {
        Fight(MovementType.Attack, UnitType.Cavalry, 100);
        _simulation.Advance(45);

        var restored = SnapshotAssert.RoundTrips(_state);
        _simulation.Advance(300);
        new Simulation(restored, Data).Advance(300);

        var other = restored.Villages[_camp.Id];
        Assert.Equal(_camp.Resources, other.Resources);
        Assert.Equal(_camp.Troops.OrderBy(t => t.Key), other.Troops.OrderBy(t => t.Key));
        Assert.Equal(_camp.NextTroopRegenTime, other.NextTroopRegenTime);
    }

    // Rules.

    [Fact]
    public void BarbariansCannotBeReinforced()
    {
        _state.Villages[Home].AddTroops(UnitType.Cavalry, 1);

        var result = new CommandHandler(_state, Data).Handle(Owner, new SendTroopsCommand(Home, _camp.Id, MovementType.Reinforce, Force(UnitType.Cavalry, 1)));

        Assert.Equal(CommandRejection.InvalidMission, result.Rejection);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EachTierCanBeFoughtAndLooted(int tier)
    {
        var state = TestMatch.Create();
        var camp = FirstOfTier(state, tier);
        state.Villages[Home].AddTroops(UnitType.Swordsman, 500);
        new CommandHandler(state, Data).Handle(Owner, new SendTroopsCommand(Home, camp.Id, MovementType.Attack, Force(UnitType.Swordsman, 500)));

        new Simulation(state, Data).Advance(state.Movements[0].ArrivalTime);

        var report = Assert.Single(state.Reports);
        Assert.True(report.AttackerWon);
        Assert.Equal(Village.BarbarianOwnerId, report.DefenderId);
        Assert.True(report.Loot.Wood > 0);
        Assert.Equal(camp.BuildingLevel(BuildingType.Wall), report.WallLevel);
        SnapshotAssert.RoundTrips(state);
    }

    [Fact]
    public void LootLeavesTheProtectedShareAtACamp()
    {
        Fight(MovementType.Raid, UnitType.Cavalry, 1000);

        var capacity = BuildingRules.VillageCapacity(Data, _camp);
        Assert.True(_camp.Resources.Wood >= Data.Combat.ProtectedResourceShare * capacity - 1e-6);
    }
}
