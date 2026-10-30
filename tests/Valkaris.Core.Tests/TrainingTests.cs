using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class TrainingTests
{
    private const int Owner = 1;
    private const int VillageId = 1;

    private static readonly GameData Data = TestData.Shipped;
    private static readonly UnitDefinition Spearman = Data.Units.Units[UnitType.Spearman];

    private readonly GameState _state = TestMatch.Create();
    private readonly CommandHandler _handler;
    private readonly Simulation _simulation;
    private readonly Village _village;

    public TrainingTests()
    {
        _handler = new CommandHandler(_state, Data);
        _simulation = new Simulation(_state, Data);
        _village = _state.Villages[VillageId];
        _village.SetBuildingLevel(BuildingType.Barracks, 1);
        _village.Resources = new Resources(10_000, 10_000, 10_000);
    }

    private static string Json(GameState state) => SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(state));

    private void AssertRejected(CommandRejection expected, TrainCommand command, int issuer = Owner)
    {
        var before = Json(_state);

        var result = _handler.Handle(issuer, command);

        Assert.Equal(expected, result.Rejection);
        Assert.Equal(before, Json(_state));
    }

    private static TrainCommand Train(UnitType unit = UnitType.Spearman, int count = 5, int villageId = VillageId) =>
        new(villageId, unit, count);

    [Fact]
    public void TrainingPaysTheCostOfTheWholeBatchAndQueuesIt()
    {
        var before = _village.Resources;

        Assert.True(_handler.Handle(Owner, Train()).Success);

        Assert.Equal(before - Spearman.Cost * 5, _village.Resources);
        var item = Assert.Single(_village.TrainingQueue);
        Assert.Equal(new TrainingQueueItem(UnitType.Spearman, 5, 0, Spearman.TrainingSeconds * 5), item);
    }

    [Fact]
    public void BatchArrivesAllTogetherExactlyOnTime()
    {
        _handler.Handle(Owner, Train());
        var start = _village.TroopCount(UnitType.Spearman);
        var finish = Spearman.TrainingSeconds * 5;

        Assert.Empty(_simulation.Advance(finish - 0.001));
        Assert.Equal(start, _village.TroopCount(UnitType.Spearman));

        var events = _simulation.Advance(0.001);

        Assert.Equal(new TroopsTrained(finish, VillageId, UnitType.Spearman, 5), Assert.Single(events));
        Assert.Equal(start + 5, _village.TroopCount(UnitType.Spearman));
        Assert.Empty(_village.TrainingQueue);
    }

    [Fact]
    public void QueuedBatchesRunInSequence()
    {
        _handler.Handle(Owner, Train(count: 1));
        _handler.Handle(Owner, Train(count: 2));

        Assert.Equal(_village.TrainingQueue[0].FinishTime, _village.TrainingQueue[1].StartTime);
        Assert.Equal(Spearman.TrainingSeconds * 3, _village.TrainingQueue[1].FinishTime);

        var events = _simulation.Advance(TestMatch.OneHour);
        Assert.Equal(new[] { 1, 2 }, events.Cast<TroopsTrained>().Select(e => e.Count));
    }

    [Fact]
    public void SpeedMultiplierShortensTraining()
    {
        var fast = TestMatch.Create(speed: 2);
        fast.Villages[VillageId].SetBuildingLevel(BuildingType.Barracks, 1);

        new CommandHandler(fast, Data).Handle(Owner, Train(count: 1));

        Assert.Equal(Spearman.TrainingSeconds / 2, fast.Villages[VillageId].TrainingQueue[0].FinishTime);
    }

    [Fact]
    public void HigherBarracksTrainsFaster()
    {
        Assert.Equal(Spearman.TrainingSeconds, UnitRules.SecondsPerUnit(Data, UnitType.Spearman, 1));
        Assert.Equal(Spearman.TrainingSeconds / 1.1, UnitRules.SecondsPerUnit(Data, UnitType.Spearman, 2), precision: 10);
        Assert.Equal(Spearman.TrainingSeconds / 1.5, UnitRules.SecondsPerUnit(Data, UnitType.Spearman, 6), precision: 10);
    }

    [Fact]
    public void BarracksUpgradeDoesNotChangeABatchAlreadyQueued()
    {
        _handler.Handle(Owner, Train());
        var finish = _village.TrainingQueue[0].FinishTime;

        _village.SetBuildingLevel(BuildingType.Barracks, 5);
        _simulation.Advance(1);

        Assert.Equal(finish, _village.TrainingQueue[0].FinishTime);
    }

    [Theory]
    [InlineData(UnitType.Spearman, 1)]
    [InlineData(UnitType.Swordsman, 2)]
    [InlineData(UnitType.Cavalry, 3)]
    [InlineData(UnitType.Catapult, 5)]
    public void UnitsUnlockAtTheirBarracksLevel(UnitType unit, int level)
    {
        _village.SetBuildingLevel(BuildingType.Barracks, level - 1);
        AssertRejected(CommandRejection.BarracksLevelTooLow, Train(unit));

        _village.SetBuildingLevel(BuildingType.Barracks, level);
        Assert.True(_handler.Handle(Owner, Train(unit)).Success);
    }

    [Fact]
    public void ValuesComeFromData()
    {
        var altered = TestData.Mutated(GameData.UnitsFile, root =>
        {
            root["units"]!["Spearman"]!["trainingSeconds"] = 7;
            root["trainingSpeedBonusPerLevel"] = 1.0;
        });

        Assert.Equal(7, UnitRules.SecondsPerUnit(altered, UnitType.Spearman, 1));
        Assert.Equal(3.5, UnitRules.SecondsPerUnit(altered, UnitType.Spearman, 2));
    }

    [Fact]
    public void RejectsWhenMatchIsOver()
    {
        _state.ElapsedTime = _state.Settings.MatchDurationSeconds;
        AssertRejected(CommandRejection.MatchOver, Train());
    }

    [Fact]
    public void RejectsMissingVillage() => AssertRejected(CommandRejection.VillageNotFound, Train(villageId: 999));

    [Fact]
    public void RejectsAnotherPlayersVillage() => AssertRejected(CommandRejection.NotOwner, Train(), issuer: 2);

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void RejectsNonPositiveCount(int count) => AssertRejected(CommandRejection.InvalidCount, Train(count: count));

    [Fact]
    public void RejectsWhenQueueIsFull()
    {
        for (var i = 0; i < Data.Game.MaxTrainingQueueLength; i++)
            Assert.True(_handler.Handle(Owner, Train(count: 1)).Success);

        AssertRejected(CommandRejection.QueueFull, Train(count: 1));
    }

    [Theory]
    [InlineData(0, 1000, 1000)]
    [InlineData(1000, 0, 1000)]
    [InlineData(1000, 1000, 0)]
    public void RejectsWhenAnyResourceIsShort(double wood, double stone, double food)
    {
        _village.Resources = new Resources(wood, stone, food);

        AssertRejected(CommandRejection.InsufficientResources, Train(count: 1));
    }

    [Fact]
    public void MaxCountIsTheLargestAffordableBatch()
    {
        _village.Resources = new Resources(10_000, 10_000, 610);

        var option = TrainPreview.Evaluate(_state, Data, Owner, VillageId, UnitType.Spearman, 1);

        Assert.Equal(10, option.MaxCount);
        Assert.True(TrainPreview.Evaluate(_state, Data, Owner, VillageId, UnitType.Spearman, 10).CanTrain);
        Assert.Equal(
            CommandRejection.InsufficientResources,
            TrainPreview.Evaluate(_state, Data, Owner, VillageId, UnitType.Spearman, 11).Rejection);
    }

    [Fact]
    public void PreviewAndHandlerAgree()
    {
        var setups = new Action<GameState>[]
        {
            _ => { },
            s => s.Villages[VillageId].Resources = Resources.Zero,
            s => s.Villages[VillageId].Resources = new Resources(100_000, 100_000, 100_000),
            s => s.Villages[VillageId].SetBuildingLevel(BuildingType.Barracks, 5),
            s => s.ElapsedTime = s.Settings.MatchDurationSeconds,
            s =>
            {
                s.Villages[VillageId].SetBuildingLevel(BuildingType.Barracks, 5);
                s.Villages[VillageId].Resources = new Resources(100_000, 100_000, 100_000);
                s.Villages[VillageId].EnqueueTraining(new TrainingQueueItem(UnitType.Spearman, 1, 0, 10));
                s.Villages[VillageId].EnqueueTraining(new TrainingQueueItem(UnitType.Spearman, 1, 10, 20));
            },
        };

        foreach (var setup in setups)
        foreach (var unit in Enum.GetValues<UnitType>())
        foreach (var player in new[] { Owner, 2 })
        foreach (var count in new[] { 0, 1, 500 })
        {
            var state = TestMatch.Create();
            setup(state);

            var preview = TrainPreview.Evaluate(state, Data, player, VillageId, unit, count);
            var result = new CommandHandler(state, Data).Handle(player, new TrainCommand(VillageId, unit, count));

            Assert.Equal(preview.Rejection, result.Rejection);
        }
    }

    [Fact]
    public void QueueInProgressRoundTrips()
    {
        _handler.Handle(Owner, Train());
        _simulation.Advance(3);

        var restored = SnapshotAssert.RoundTrips(_state);

        Assert.Equal(_village.TrainingQueue, restored.Villages[VillageId].TrainingQueue);
    }
}
