using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class BattleTests
{
    private const int Attacker = 1;
    private const int Defender = 2;
    private const int Home = 1;
    private const int Target = 2;

    private static readonly GameData Data = TestData.Shipped;

    private readonly GameState _state = TestMatch.Create();
    private readonly CommandHandler _handler;
    private readonly Simulation _simulation;
    private readonly Village _home;
    private readonly Village _target;

    public BattleTests()
    {
        _handler = new CommandHandler(_state, Data);
        _simulation = new Simulation(_state, Data);
        _home = _state.Villages[Home];
        _target = _state.Villages[Target];
        _target.RemoveTroops(UnitType.Spearman, _target.TroopCount(UnitType.Spearman));
    }

    private static Dictionary<UnitType, int> Force(params (UnitType Unit, int Count)[] troops) =>
        troops.ToDictionary(t => t.Unit, t => t.Count);

    /// <summary>Gives the attacker the troops, sends them, and runs up to the moment of the battle.</summary>
    private BattleReport Fight(MovementType type, Dictionary<UnitType, int> troops, BuildingType? catapultTarget = null)
    {
        foreach (var (unit, count) in troops)
            _home.AddTroops(unit, count);

        var result = _handler.Handle(Attacker, new SendTroopsCommand(Home, Target, type, troops, catapultTarget));
        Assert.True(result.Success, result.Rejection.ToString());

        var reports = _state.Reports.Count;
        _simulation.Advance(_state.Movements[^1].ArrivalTime - _state.ElapsedTime);
        Assert.Equal(reports + 1, _state.Reports.Count);
        return _state.Reports[^1];
    }

    // Results and reports.

    [Fact]
    public void BattleRaisesAnEventAndAReportForBothSides()
    {
        _target.AddTroops(UnitType.Spearman, 100);
        _home.AddTroops(UnitType.Swordsman, 100);
        _handler.Handle(Attacker, new SendTroopsCommand(Home, Target, MovementType.Attack, Force((UnitType.Swordsman, 100))));
        var arrival = _state.Movements[0].ArrivalTime;

        var events = _simulation.Advance(arrival);

        var report = Assert.Single(_state.Reports);
        Assert.Equal(new BattleResolved(arrival, report.Id), Assert.Single(events));
        Assert.Equal(arrival, report.Time);
        Assert.Equal(Attacker, report.AttackerId);
        Assert.Equal(Defender, report.DefenderId);
        Assert.Equal(Home, report.AttackerVillageId);
        Assert.Equal(Target, report.DefenderVillageId);
        Assert.Equal(100, report.AttackerSent[UnitType.Swordsman]);
        Assert.Equal(54, report.AttackerLost[UnitType.Swordsman]);
        Assert.Equal(100, report.DefenderPresent[UnitType.Spearman]);
        Assert.Equal(100, report.DefenderLost[UnitType.Spearman]);
        Assert.Equal(3000, report.AttackPower);
        Assert.Equal(2000, report.DefencePower);
        Assert.True(report.AttackerWon);
    }

    [Fact]
    public void DefenderLosesTroopsAndSurvivorsGoHome()
    {
        _target.AddTroops(UnitType.Spearman, 100);
        var before = _home.TroopCount(UnitType.Swordsman);

        Fight(MovementType.Attack, Force((UnitType.Swordsman, 100)));

        Assert.Equal(0, _target.TroopCount(UnitType.Spearman));
        var returning = Assert.Single(_state.Movements);
        Assert.Equal(MovementType.Return, returning.Type);
        Assert.Equal(46, returning.Troops[UnitType.Swordsman]);

        _simulation.Advance(returning.ArrivalTime - _state.ElapsedTime);
        Assert.Equal(before + 46, _home.TroopCount(UnitType.Swordsman));
        Assert.Empty(_state.Movements);
    }

    [Fact]
    public void ALoserWithNoSurvivorsSendsNoReturn()
    {
        _target.AddTroops(UnitType.Spearman, 100);

        var report = Fight(MovementType.Attack, Force((UnitType.Cavalry, 5)));

        Assert.False(report.AttackerWon);
        Assert.Empty(_state.Movements);
    }

    [Fact]
    public void ALosingRaidKeepsSomeTroopsButNoLoot()
    {
        _target.AddTroops(UnitType.Spearman, 100);
        _target.Resources = new Resources(900, 900, 900);

        var report = Fight(MovementType.Raid, Force((UnitType.Cavalry, 50)));

        Assert.False(report.AttackerWon);
        Assert.Equal(Resources.Zero, report.Loot);
        var returning = Assert.Single(_state.Movements);
        Assert.True(returning.Troops[UnitType.Cavalry] > 0);
        Assert.Equal(Resources.Zero, returning.Loot);
    }

    [Fact]
    public void SurvivorsTravelAtTheirOwnPace()
    {
        // No defenders, so everyone survives except where sent: only cavalry goes, at cavalry speed.
        Fight(MovementType.Raid, Force((UnitType.Cavalry, 5)));

        var returning = _state.Movements[0];
        var distance = UnitRules.Distance(_home.Position, _target.Position);
        Assert.Equal(distance / Data.Units.Units[UnitType.Cavalry].Speed, returning.ArrivalTime - returning.DepartureTime, precision: 8);
    }

    [Fact]
    public void WallLevelIsApplied()
    {
        _target.AddTroops(UnitType.Spearman, 100);
        _target.SetBuildingLevel(BuildingType.Wall, 10);

        var report = Fight(MovementType.Attack, Force((UnitType.Swordsman, 100)));

        Assert.Equal(10, report.WallLevel);
        Assert.Equal(3000, report.DefencePower, precision: 8);
        Assert.False(report.AttackerWon);
    }

    [Fact]
    public void TroopsThatLeftDoNotDefend()
    {
        _target.AddTroops(UnitType.Spearman, 100);
        Assert.True(new CommandHandler(_state, Data).Handle(Defender, new SendTroopsCommand(Target, Home, MovementType.Raid, Force((UnitType.Spearman, 100)))).Success);

        var report = Fight(MovementType.Raid, Force((UnitType.Cavalry, 1)));

        Assert.True(report.AttackerWon);
        Assert.Empty(report.DefenderPresent);
    }

    // Loot and protection.

    [Fact]
    public void ProtectedShareCannotBeLooted()
    {
        var capacity = BuildingRules.VillageCapacity(Data, _target);
        var protectedAmount = Data.Combat.ProtectedResourceShare * capacity;
        _target.Resources = new Resources(capacity, capacity, capacity);

        // Far more carrying capacity than there is to take.
        var report = Fight(MovementType.Raid, Force((UnitType.Cavalry, 1000)));

        Assert.Equal(capacity - protectedAmount, report.Loot.Wood, precision: 8);
        Assert.Equal(capacity - protectedAmount, report.Loot.Stone, precision: 8);
        Assert.Equal(capacity - protectedAmount, report.Loot.Food, precision: 8);
        Assert.True(_target.Resources.Wood >= protectedAmount);
        Assert.True(_target.Resources.Stone >= protectedAmount);
        Assert.True(_target.Resources.Food >= protectedAmount);
    }

    [Fact]
    public void DefenderBelowTheProtectedShareLosesNothing()
    {
        _target.Resources = new Resources(10, 20, 30);

        var report = Fight(MovementType.Raid, Force((UnitType.Cavalry, 100)));

        Assert.Equal(Resources.Zero, report.Loot);
    }

    [Fact]
    public void ProtectionIsPerResource()
    {
        var capacity = BuildingRules.VillageCapacity(Data, _target);
        _target.Resources = new Resources(capacity, 0, 0);

        var report = Fight(MovementType.Raid, Force((UnitType.Cavalry, 1000)));

        Assert.True(report.Loot.Wood > 0);
        Assert.Equal(0, report.Loot.Stone);
        Assert.Equal(0, report.Loot.Food);
    }

    [Fact]
    public void LootIsLimitedByCarryCapacityAndSplitProportionally()
    {
        // No production, so the amounts stay put while the troops travel.
        // Protected: 200 each at capacity 1000. Lootable: 600, 200, 0. Carry: 2 cavalry = 120.
        _target.SetBuildingLevel(BuildingType.Woodcutter, 0);
        _target.SetBuildingLevel(BuildingType.Quarry, 0);
        _target.SetBuildingLevel(BuildingType.Farm, 0);
        _target.Resources = new Resources(800, 400, 100);

        var report = Fight(MovementType.Raid, Force((UnitType.Cavalry, 2)));

        Assert.Equal(new Resources(90, 30, 0), report.Loot);
        Assert.Equal(new Resources(710, 370, 100), _target.Resources);
    }

    [Fact]
    public void ProtectedShareComesFromData()
    {
        var data = TestData.Mutated(GameData.CombatFile, root => root["protectedResourceShare"] = 0.5);
        var village = TestMatch.Create(data: data).Villages[1];
        village.Resources = new Resources(800, 100, 0);

        var loot = LootRules.Calculate(data, village, Force((UnitType.Cavalry, 1000)));

        Assert.Equal(new Resources(300, 0, 0), loot);
    }

    [Fact]
    public void LootArrivesWithTheReturn()
    {
        _target.Resources = new Resources(800, 400, 100);
        _home.Resources = Resources.Zero;

        var report = Fight(MovementType.Raid, Force((UnitType.Cavalry, 2)));

        Assert.Equal(_state.Movements[0].Loot, report.Loot);
        Assert.True(report.Loot.Wood > 0);

        var homeWood = _home.Resources.Wood;
        var seconds = _state.Movements[0].ArrivalTime - _state.ElapsedTime;
        _simulation.Advance(seconds);

        Assert.Equal(homeWood + seconds * BuildingRules.VillageProduction(Data, _home).Wood + report.Loot.Wood, _home.Resources.Wood, precision: 6);
    }

    [Fact]
    public void AttackAlsoLoots()
    {
        _target.Resources = new Resources(800, 400, 100);

        var report = Fight(MovementType.Attack, Force((UnitType.Cavalry, 2)));

        Assert.True(report.Loot.Wood > 0);
    }

    // Catapults.

    [Fact]
    public void SurvivingCatapultsDamageTheChosenBuilding()
    {
        _target.SetBuildingLevel(BuildingType.Wall, 5);

        var report = Fight(MovementType.Attack, Force((UnitType.Catapult, 1)), BuildingType.Wall);

        Assert.Equal(new CatapultDamage(BuildingType.Wall, 5, 4), report.CatapultDamage);
        Assert.Equal(4, _target.BuildingLevel(BuildingType.Wall));
    }

    [Fact]
    public void MoreCatapultsDoMoreDamage()
    {
        _target.SetBuildingLevel(BuildingType.Wall, 5);

        var report = Fight(MovementType.Attack, Force((UnitType.Catapult, 25)), BuildingType.Wall);

        Assert.Equal(new CatapultDamage(BuildingType.Wall, 5, 3), report.CatapultDamage);
    }

    [Fact]
    public void DamageStopsAtLevelZero()
    {
        _target.SetBuildingLevel(BuildingType.Wall, 1);

        var report = Fight(MovementType.Attack, Force((UnitType.Catapult, 100)), BuildingType.Wall);

        Assert.Equal(new CatapultDamage(BuildingType.Wall, 1, 0), report.CatapultDamage);
    }

    [Fact]
    public void TargetingAnUnbuiltBuildingDoesNothing()
    {
        var report = Fight(MovementType.Attack, Force((UnitType.Catapult, 5)), BuildingType.Barracks);

        Assert.Null(report.CatapultDamage);
        Assert.Equal(0, _target.BuildingLevel(BuildingType.Barracks));
    }

    [Fact]
    public void LosingAttackDoesNoCatapultDamage()
    {
        _target.AddTroops(UnitType.Spearman, 100);
        _target.SetBuildingLevel(BuildingType.Wall, 5);

        var report = Fight(MovementType.Attack, Force((UnitType.Catapult, 5)), BuildingType.Wall);

        Assert.False(report.AttackerWon);
        Assert.Null(report.CatapultDamage);
        Assert.Equal(5, _target.BuildingLevel(BuildingType.Wall));
    }

    [Fact]
    public void QueuedUpgradesAreLoweredWithTheDamage()
    {
        _target.SetBuildingLevel(BuildingType.Wall, 3);
        _target.EnqueueBuild(new BuildQueueItem(BuildingType.Wall, 4, 0, 100_000));
        _target.EnqueueBuild(new BuildQueueItem(BuildingType.Wall, 5, 100_000, 100_001));

        Fight(MovementType.Attack, Force((UnitType.Catapult, 20)), BuildingType.Wall);

        Assert.Equal(1, _target.BuildingLevel(BuildingType.Wall));
        Assert.Equal(new[] { 2, 3 }, _target.BuildQueue.Select(q => q.TargetLevel));
    }

    [Fact]
    public void RandomTargetIsDecidedByTheSeedAndIsAStandingBuilding()
    {
        BuildingType Play(ulong seed)
        {
            var state = GameFactory.CreateMatch(
                new GameSettings(TestMatch.OneHour, 1, MapSize.Small, seed), TestMatch.Slots(2), Data);
            state.Villages[Target].RemoveTroops(UnitType.Spearman, state.Villages[Target].TroopCount(UnitType.Spearman));
            state.Villages[Home].AddTroops(UnitType.Catapult, 1);
            new CommandHandler(state, Data).Handle(Attacker, new SendTroopsCommand(Home, Target, MovementType.Attack, Force((UnitType.Catapult, 1))));
            new Simulation(state, Data).Advance(state.Movements[0].ArrivalTime);
            return state.Reports[0].CatapultDamage!.Building;
        }

        Assert.Equal(Play(5), Play(5));

        var hits = Enumerable.Range(0, 100).Select(seed => Play((ulong)seed)).Distinct().ToList();
        Assert.True(hits.Count > 1);
        Assert.DoesNotContain(BuildingType.Barracks, hits);
        Assert.DoesNotContain(BuildingType.Wall, hits);
    }

    [Fact]
    public void AttackWithNoCatapultsUsesNoRandomNumbers()
    {
        var before = _state.RngState;

        var report = Fight(MovementType.Attack, Force((UnitType.Cavalry, 2)));

        Assert.Null(report.CatapultDamage);
        Assert.Equal(before, _state.RngState);
    }

    // Whole flow.

    [Fact]
    public void FullBattleRoundTripsAndContinuesLikeTheOriginal()
    {
        _target.AddTroops(UnitType.Spearman, 20);
        _target.Resources = new Resources(800, 800, 800);
        var report = Fight(MovementType.Attack, Force((UnitType.Swordsman, 100), (UnitType.Catapult, 5)));

        var restored = SnapshotAssert.RoundTrips(_state);
        Assert.Equal(report.Id, restored.Reports[0].Id);
        Assert.Equal(report.Loot, restored.Reports[0].Loot);

        _simulation.Advance(1000);
        new Simulation(restored, Data).Advance(1000);

        Assert.Equal(_home.Resources, restored.Villages[Home].Resources);
        Assert.Equal(_home.TroopCount(UnitType.Swordsman), restored.Villages[Home].TroopCount(UnitType.Swordsman));
        Assert.Empty(restored.Movements);
    }
}
