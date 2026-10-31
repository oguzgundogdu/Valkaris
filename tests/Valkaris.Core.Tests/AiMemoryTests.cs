using Valkaris.Ai;
using Valkaris.Core.Commands;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class AiMemoryTests
{
    private const int Self = 2;
    private const int Enemy = 1;

    private readonly GameState _state = TestMatch.Create(players: 3);
    private readonly AiView _view;
    private readonly AiMemory _memory;
    private int _reportId = 1;

    public AiMemoryTests()
    {
        _state.Players[Self].Difficulty = AiDifficulty.Normal;
        _view = new AiView(_state, TestData.Shipped, Self, command => new CommandHandler(_state, TestData.Shipped).Handle(Self, command));
        _memory = new AiMemory(_view);
    }

    private KnownVillage Known(int id) => _view.KnownVillages.Single(v => v.Id == id);

    private void Report(int attacker, int defender, int villageId, int spears, int killed = 0, int wall = 0, bool won = true, double loot = 0, LoyaltyChange? loyalty = null, bool isProtected = false)
    {
        var present = new Dictionary<UnitType, int> { [UnitType.Spearman] = spears };
        var lost = new Dictionary<UnitType, int> { [UnitType.Spearman] = killed };
        var none = new Dictionary<UnitType, int>();
        _state.AddReport(new BattleReport(_reportId++, _state.ElapsedTime, MovementType.Attack, attacker, defender, 2, villageId, none, none, present, lost, wall, 1, 1, won, new Resources(loot, 0, 0), null, loyalty, isProtected));
    }

    private static double SpearDefence => (TestData.Shipped.Units.Units[UnitType.Spearman].DefenceVsInfantry + TestData.Shipped.Units.Units[UnitType.Spearman].DefenceVsCavalry) / 2;

    [Fact]
    public void AnUnseenCampUsesTheBeliefFromData()
    {
        var camp = _view.KnownVillages.First(v => v.IsBarbarian);

        Assert.Equal(TestData.Shipped.Ai.Beliefs.BarbarianPowerByTier[camp.BarbarianTier!], _memory.EstimateDefence(camp));
    }

    [Fact]
    public void BeliefsAreNotTheRealGarrisons()
    {
        var data = TestData.Mutated("map.json", r => r["barbarians"]!["tiers"]![0]!["garrison"]!["Spearman"] = 500);
        var state = GameFactory.CreateMatch(new GameSettings(3600, 1, MapSize.Small), TestMatch.Slots(2), data);
        var view = new AiView(state, data, Self, _ => CommandResult.Ok);
        var camp = view.KnownVillages.First(v => v.BarbarianTier == data.Map.Barbarians.Tiers[0].Name);

        Assert.Equal(data.Ai.Beliefs.BarbarianPowerByTier[camp.BarbarianTier!], new AiMemory(view).EstimateDefence(camp));
    }

    [Fact]
    public void AnUnseenPlayerIsAssumedToGrowOverTime()
    {
        var beliefs = TestData.Shipped.Ai.Beliefs;
        Assert.Equal(beliefs.PlayerPowerAtStart, _memory.EstimateDefence(Known(Enemy)));

        _state.ElapsedTime = 600;

        Assert.Equal(beliefs.PlayerPowerAtStart + beliefs.PlayerPowerPerMinute * 10, _memory.EstimateDefence(Known(Enemy)), 6);
    }

    [Fact]
    public void AnUnseenPlayerGrowsFasterAtHigherGameSpeed()
    {
        var state = TestMatch.Create(speed: 3);
        var view = new AiView(state, TestData.Shipped, Self, _ => CommandResult.Ok);
        state.ElapsedTime = 200;

        var beliefs = TestData.Shipped.Ai.Beliefs;
        Assert.Equal(beliefs.PlayerPowerAtStart + beliefs.PlayerPowerPerMinute * 10, new AiMemory(view).EstimateDefence(view.KnownVillages.Single(v => v.Id == Enemy)), 6);
    }

    [Fact]
    public void AReportReplacesTheBelief()
    {
        Report(Self, Enemy, Enemy, spears: 100, killed: 40, wall: 2);

        _memory.Update();

        var expected = 60 * SpearDefence * (1 + TestData.Shipped.Combat.WallBonusPerLevel * 2);
        Assert.Equal(expected, _memory.EstimateDefence(Known(Enemy)), 6);
    }

    [Fact]
    public void AnEstimateGrowsAfterTheSighting()
    {
        Report(Self, Enemy, Enemy, spears: 100, killed: 100);
        _memory.Update();

        _state.ElapsedTime = 300;

        var beliefs = TestData.Shipped.Ai.Beliefs;
        var expected = beliefs.PlayerPowerPerMinute * 5;
        Assert.Equal(expected, _memory.EstimateDefence(Known(Enemy)), 6);
    }

    [Fact]
    public void ABarbarianEstimateStopsAtWhatWasSeen()
    {
        var camp = _view.KnownVillages.First(v => v.IsBarbarian);
        Report(Self, Village.BarbarianOwnerId, camp.Id, spears: 10, killed: 10);
        _memory.Update();

        _state.ElapsedTime = 100000;

        Assert.Equal(10 * SpearDefence, _memory.EstimateDefence(camp), 6);
    }

    [Fact]
    public void AChangeOfOwnerForgetsTheSighting()
    {
        var camp = _view.KnownVillages.First(v => v.IsBarbarian);
        Report(Self, Village.BarbarianOwnerId, camp.Id, spears: 1);
        _memory.Update();

        _state.Villages[camp.Id].OwnerId = Enemy;
        _state.Villages[camp.Id].BarbarianTier = null;

        Assert.Equal(TestData.Shipped.Ai.Beliefs.PlayerPowerAtStart, _memory.EstimateDefence(Known(camp.Id)));
    }

    [Fact]
    public void BattlesItWasNotAttackingInTeachNothing()
    {
        Report(Enemy, Self, Self, spears: 5);
        Report(Enemy, 3, 3, spears: 99);

        _memory.Update();

        Assert.Null(_memory.SightingOf(Self));
        Assert.Null(_memory.SightingOf(3));
    }

    [Fact]
    public void LoyaltyAndProtectionAreRemembered()
    {
        Report(Self, Enemy, Enemy, spears: 1, loyalty: new LoyaltyChange(100, 75, false));
        _memory.Update();
        Assert.Equal(75, _memory.SightingOf(Enemy)!.Loyalty);
        Assert.False(_memory.SightingOf(Enemy)!.CapitalProtected);

        Report(Self, Enemy, Enemy, spears: 1, isProtected: true);
        _memory.Update();
        Assert.True(_memory.SightingOf(Enemy)!.CapitalProtected);
    }

    [Fact]
    public void ExpectedLootStartsFullAndRecoversAfterARaid()
    {
        var beliefs = TestData.Shipped.Ai.Beliefs;
        var camp = _view.KnownVillages.First(v => v.IsBarbarian);
        var full = beliefs.BarbarianLootByTier[camp.BarbarianTier!];
        Assert.Equal(full, _memory.ExpectedBarbarianLoot(camp));

        Report(Self, Village.BarbarianOwnerId, camp.Id, spears: 1, loot: 100);
        _memory.Update();
        Assert.Equal(0, _memory.ExpectedBarbarianLoot(camp));

        _state.ElapsedTime = 120;
        Assert.Equal(Math.Min(full, beliefs.BarbarianLootRecoveryPerMinute * 2), _memory.ExpectedBarbarianLoot(camp), 6);
    }

    [Fact]
    public void AnUpdateOnlyReadsNewReports()
    {
        Report(Self, Enemy, Enemy, spears: 100);
        _memory.Update();
        var first = _memory.SightingOf(Enemy);

        _memory.Update();

        Assert.Same(first, _memory.SightingOf(Enemy));
    }

    // Scout reports.

    private void Scouted(int sender, int defender, int villageId, int? garrison, int wall = 0, double stock = 0, int scouts = 1)
    {
        var sent = new Dictionary<UnitType, int> { [UnitType.Cavalry] = scouts };
        _state.AddScoutReport(new ScoutReport(
            _reportId++,
            _state.ElapsedTime,
            sender,
            defender,
            2,
            villageId,
            sent,
            garrison is not null,
            garrison is { } count ? new Dictionary<UnitType, int> { [UnitType.Spearman] = count } : null,
            garrison is null ? null : wall,
            garrison is null ? null : new Resources(stock, 0, 0)));
    }

    [Fact]
    public void AScoutReportSetsTheEstimate()
    {
        Scouted(Self, Enemy, Enemy, 10, wall: 2);
        _memory.Update();

        var expected = Scouted(10 * SpearDefence * (1 + TestData.Shipped.Combat.WallBonusPerLevel * 2));
        Assert.Equal(expected, _memory.EstimateDefence(Known(Enemy)), 6);
    }

    [Fact]
    public void AScoutedCampGetsNoAllowanceForTroopsAway()
    {
        var camp = _view.KnownVillages.First(v => v.IsBarbarian);
        Scouted(Self, Village.BarbarianOwnerId, camp.Id, 10);
        _memory.Update();

        Assert.Equal(10 * SpearDefence, _memory.EstimateDefence(camp), 6);
    }

    [Fact]
    public void ScoutingIsNotRememberedAsABattle()
    {
        Scouted(Self, Enemy, Enemy, 10);
        _memory.Update();

        Assert.Null(_memory.SightingOf(Enemy)!.LastBattleTime);
    }

    [Fact]
    public void ScoutingKeepsWhatBattlesLearned()
    {
        Report(Self, Enemy, Enemy, 10, loyalty: new LoyaltyChange(100, 75, false));
        _memory.Update();
        var fought = _memory.SightingOf(Enemy)!.LastBattleTime;
        _state.ElapsedTime += 60;

        Scouted(Self, Enemy, Enemy, 4);
        _memory.Update();

        var sighting = _memory.SightingOf(Enemy)!;
        Assert.Equal(75, sighting.Loyalty);
        Assert.Equal(fought, sighting.LastBattleTime);
        Assert.Equal(4, sighting.Remaining[UnitType.Spearman]);
    }

    [Fact]
    public void AScoutedCampIsExpectedToRegrowToItsPeak()
    {
        var camp = _view.KnownVillages.First(v => v.IsBarbarian);
        Report(Self, Village.BarbarianOwnerId, camp.Id, 20, killed: 15);
        Scouted(Self, Village.BarbarianOwnerId, camp.Id, 5);
        _memory.Update();

        Assert.Equal(20 * SpearDefence, _memory.SightingOf(camp.Id)!.PeakDefence, 6);
    }

    [Fact]
    public void ScoutedStockReplacesTheLootBelief()
    {
        var camp = _view.KnownVillages.First(v => v.IsBarbarian);
        Scouted(Self, Village.BarbarianOwnerId, camp.Id, 5, stock: 40);
        _memory.Update();

        Assert.Equal(40, _memory.ExpectedBarbarianLoot(camp), 6);

        _state.ElapsedTime += 60;
        Assert.Equal(40 + TestData.Shipped.Ai.Beliefs.BarbarianLootRecoveryPerMinute, _memory.ExpectedBarbarianLoot(camp), 6);
    }

    [Fact]
    public void ABattleMakesScoutedStockOutOfDate()
    {
        var camp = _view.KnownVillages.First(v => v.IsBarbarian);
        Scouted(Self, Village.BarbarianOwnerId, camp.Id, 5, stock: 40);
        Report(Self, Village.BarbarianOwnerId, camp.Id, 5, killed: 5, won: true);
        _memory.Update();

        Assert.Null(_memory.SightingOf(camp.Id)!.Stock);
    }

    /// <summary>The allowance for troops away, and for help a defender can send, even to a village counted as empty.</summary>
    private double Scouted(double seen)
    {
        var beliefs = TestData.Shipped.Ai.Beliefs;
        var minutes = _state.ElapsedTime * _state.Settings.SpeedMultiplier / 60;
        return seen * beliefs.ScoutedPlayerAllowance + (beliefs.PlayerPowerAtStart + beliefs.PlayerPowerPerMinute * minutes) * beliefs.ScoutedHelpShare;
    }

    [Fact]
    public void AVillageCountedAsEmptyStillExpectsHelp()
    {
        Scouted(Self, Enemy, Enemy, 0);
        _memory.Update();

        Assert.True(_memory.EstimateDefence(Known(Enemy)) > 0);
        Assert.Equal(Scouted(0), _memory.EstimateDefence(Known(Enemy)), 6);
    }

    [Fact]
    public void AScoutedVillageIsExpectedToGrowFasterThanAFoughtOne()
    {
        var beliefs = TestData.Shipped.Ai.Beliefs;

        Assert.True(beliefs.ScoutedGrowthPerMinute > beliefs.PlayerPowerPerMinute);
    }

    private void ScoutedRoughly(int troopsLow, int troopsHigh, int wallLow, int wallHigh) =>
        _state.AddScoutReport(new ScoutReport(
            _reportId++, _state.ElapsedTime, Self, Enemy, 2, Enemy,
            new Dictionary<UnitType, int> { [UnitType.Scout] = 1 }, true, null, null, null,
            Quality: ScoutQuality.Narrow, TroopRange: new ValueRange(troopsLow, troopsHigh), WallRange: new ValueRange(wallLow, wallHigh)));

    [Fact]
    public void ARangeIsPlannedAgainstItsUpperEnd()
    {
        ScoutedRoughly(20, 40, 2, 3);
        _memory.Update();

        var data = TestData.Shipped;
        var expected = Scouted(40 * data.Ai.Beliefs.PowerPerTroop * (1 + data.Combat.WallBonusPerLevel * 3));
        Assert.Equal(expected, _memory.EstimateDefence(Known(Enemy)), 6);
    }

    [Fact]
    public void ARangeClearsAFailureAndGivesNoStock()
    {
        Scouted(Self, Enemy, Enemy, null, scouts: 5);
        ScoutedRoughly(0, 4, 0, 1);
        _memory.Update();

        Assert.Null(_memory.CaughtAt(Known(Enemy)));
        Assert.Null(_memory.SightingOf(Enemy)!.Stock);
    }

    [Fact]
    public void AnExactReportReplacesARange()
    {
        ScoutedRoughly(20, 40, 2, 3);
        Scouted(Self, Enemy, Enemy, 2);
        _memory.Update();

        Assert.Equal(Scouted(2 * SpearDefence), _memory.EstimateDefence(Known(Enemy)), 6);
    }

    [Fact]
    public void AnOverwhelmingReportGivesLoyaltyAndTheWatchtower()
    {
        _state.AddScoutReport(new ScoutReport(
            _reportId++, _state.ElapsedTime, Self, Enemy, 2, Enemy,
            new Dictionary<UnitType, int> { [UnitType.Scout] = 1 }, true,
            new Dictionary<UnitType, int>(), 0, Resources.Zero,
            Quality: ScoutQuality.Overwhelming,
            Buildings: new Dictionary<BuildingType, int> { [BuildingType.Watchtower] = 3 },
            Loyalty: 60));
        _memory.Update();

        Assert.Equal(60, _memory.SightingOf(Enemy)!.Loyalty);
        Assert.Equal(3, _memory.SightingOf(Enemy)!.WatchtowerLevel);
    }

    [Fact]
    public void CaughtScoutsSayNothingAboutTheGarrison()
    {
        var before = _memory.EstimateDefence(Known(Enemy));
        Scouted(Self, Enemy, Enemy, null, scouts: 100);
        _memory.Update();

        Assert.Equal(before, _memory.EstimateDefence(Known(Enemy)), 6);
        Assert.Equal(100, _memory.CaughtAt(Known(Enemy))!.Scouts);
    }

    [Fact]
    public void ASuccessfulScoutReplacesTheFailure()
    {
        Scouted(Self, Enemy, Enemy, null, scouts: 100);
        Scouted(Self, Enemy, Enemy, 2);
        _memory.Update();

        Assert.Null(_memory.CaughtAt(Known(Enemy)));
        Assert.Equal(Scouted(2 * SpearDefence), _memory.EstimateDefence(Known(Enemy)), 6);
    }

    [Fact]
    public void AFailureIsForgottenWhenTheVillageChangesHands()
    {
        Scouted(Self, Enemy, Enemy, null, scouts: 100);
        _memory.Update();
        _state.Villages[Enemy].OwnerId = 3;

        Assert.Null(_memory.CaughtAt(Known(Enemy)));
    }
}
