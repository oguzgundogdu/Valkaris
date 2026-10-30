using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class CommandHandlerTests
{
    private const int Owner = 1;
    private const int Rival = 2;
    private const int OwnVillage = 1;

    private static readonly GameData Data = TestData.Shipped;

    private readonly GameState _state;
    private readonly CommandHandler _handler;
    private readonly Village _village;

    public CommandHandlerTests()
    {
        _state = TestMatch.Create();
        _handler = new CommandHandler(_state, Data);
        _village = _state.Villages[OwnVillage];
    }

    private static string Json(GameState state) => SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(state));

    private void AssertRejected(CommandRejection expected, ICommand command, int issuer = Owner)
    {
        var before = Json(_state);

        var result = _handler.Handle(issuer, command);

        Assert.False(result.Success);
        Assert.Equal(expected, result.Rejection);
        Assert.Equal(before, Json(_state));
    }

    private static BuildCommand Build(BuildingType building, int villageId = OwnVillage) => new(villageId, building);

    private sealed record UnsupportedCommand : ICommand;

    [Fact]
    public void BuildPaysTheCostAndQueuesTheNextLevel()
    {
        var before = _village.Resources;

        var result = _handler.Handle(Owner, Build(BuildingType.Woodcutter));

        Assert.True(result.Success);
        Assert.Equal(before - BuildingRules.UpgradeCost(Data, BuildingType.Woodcutter, 2), _village.Resources);
        var item = Assert.Single(_village.BuildQueue);
        Assert.Equal(BuildingType.Woodcutter, item.Building);
        Assert.Equal(2, item.TargetLevel);
        Assert.Equal(BuildingRules.BuildTimeSeconds(Data, BuildingType.Woodcutter, 2), item.FinishTime);
    }

    [Fact]
    public void BuildDoesNotRaiseTheLevelUntilItCompletes()
    {
        _handler.Handle(Owner, Build(BuildingType.Woodcutter));

        Assert.Equal(1, _village.BuildingLevel(BuildingType.Woodcutter));
    }

    [Fact]
    public void LevelZeroBuildingCanBeBuilt()
    {
        var result = _handler.Handle(Owner, Build(BuildingType.Barracks));

        Assert.True(result.Success);
        Assert.Equal(1, _village.BuildQueue[0].TargetLevel);
    }

    [Fact]
    public void FinishTimeCountsFromNow()
    {
        _state.ElapsedTime = 100;

        _handler.Handle(Owner, Build(BuildingType.Woodcutter));

        Assert.Equal(100 + BuildingRules.BuildTimeSeconds(Data, BuildingType.Woodcutter, 2), _village.BuildQueue[0].FinishTime);
    }

    [Fact]
    public void SpeedMultiplierShortensBuildTime()
    {
        var fast = TestMatch.Create(speed: 2);

        new CommandHandler(fast, Data).Handle(Owner, Build(BuildingType.Woodcutter));

        Assert.Equal(
            BuildingRules.BuildTimeSeconds(Data, BuildingType.Woodcutter, 2) / 2,
            fast.Villages[OwnVillage].BuildQueue[0].FinishTime);
    }

    [Fact]
    public void QueuedItemStartsWhenThePreviousOneFinishes()
    {
        _handler.Handle(Owner, Build(BuildingType.Woodcutter));
        _handler.Handle(Owner, Build(BuildingType.Quarry));

        Assert.Equal(
            _village.BuildQueue[0].FinishTime + BuildingRules.BuildTimeSeconds(Data, BuildingType.Quarry, 2),
            _village.BuildQueue[1].FinishTime);
    }

    [Fact]
    public void SecondUpgradeOfTheSameBuildingTargetsTheLevelAfterAndCostsThatLevel()
    {
        _handler.Handle(Owner, Build(BuildingType.Woodcutter));
        var before = _village.Resources;

        _handler.Handle(Owner, Build(BuildingType.Woodcutter));

        Assert.Equal(3, _village.BuildQueue[1].TargetLevel);
        Assert.Equal(before - BuildingRules.UpgradeCost(Data, BuildingType.Woodcutter, 3), _village.Resources);
    }

    [Fact]
    public void ExactResourcesAreEnough()
    {
        _village.Resources = BuildingRules.UpgradeCost(Data, BuildingType.Woodcutter, 2);

        Assert.True(_handler.Handle(Owner, Build(BuildingType.Woodcutter)).Success);
        Assert.Equal(Resources.Zero, _village.Resources);
    }

    [Fact]
    public void RejectsUnknownCommand() => AssertRejected(CommandRejection.UnknownCommand, new UnsupportedCommand());

    [Fact]
    public void RejectsWhenMatchIsOver()
    {
        _state.ElapsedTime = _state.Settings.MatchDurationSeconds;

        AssertRejected(CommandRejection.MatchOver, Build(BuildingType.Woodcutter));
    }

    [Fact]
    public void RejectsMissingVillage() =>
        AssertRejected(CommandRejection.VillageNotFound, Build(BuildingType.Woodcutter, villageId: 999));

    [Fact]
    public void RejectsAnotherPlayersVillage() =>
        AssertRejected(CommandRejection.NotOwner, Build(BuildingType.Woodcutter), issuer: Rival);

    [Fact]
    public void RejectsWhenQueueIsFull()
    {
        for (var i = 0; i < Data.Game.MaxBuildQueueLength; i++)
            Assert.True(_handler.Handle(Owner, Build(BuildingType.Woodcutter)).Success);

        AssertRejected(CommandRejection.QueueFull, Build(BuildingType.Quarry));
    }

    [Fact]
    public void QueueLengthComesFromData()
    {
        var data = TestData.Mutated(GameData.GameFile, root => root["maxBuildQueueLength"] = 1);
        var state = TestMatch.Create(data: data);
        var handler = new CommandHandler(state, data);

        Assert.True(handler.Handle(Owner, Build(BuildingType.Woodcutter)).Success);
        Assert.Equal(CommandRejection.QueueFull, handler.Handle(Owner, Build(BuildingType.Quarry)).Rejection);
    }

    [Fact]
    public void RejectsAboveMaxLevel()
    {
        var max = Data.Buildings.Buildings[BuildingType.MainBuilding].MaxLevel;
        _village.SetBuildingLevel(BuildingType.MainBuilding, max);

        AssertRejected(CommandRejection.MaxLevelReached, Build(BuildingType.MainBuilding));
    }

    [Fact]
    public void MaxLevelCountsQueuedUpgrades()
    {
        var max = Data.Buildings.Buildings[BuildingType.MainBuilding].MaxLevel;
        _village.SetBuildingLevel(BuildingType.MainBuilding, max - 1);
        _village.Resources = new Resources(100_000, 100_000, 100_000);
        Assert.True(_handler.Handle(Owner, Build(BuildingType.MainBuilding)).Success);

        AssertRejected(CommandRejection.MaxLevelReached, Build(BuildingType.MainBuilding));
    }

    [Fact]
    public void RejectsAboveTheMainBuildingLimit()
    {
        var cap = BuildingRules.MaxOtherBuildingLevel(Data, _village.BuildingLevel(BuildingType.MainBuilding));
        _village.SetBuildingLevel(BuildingType.Woodcutter, cap);
        _village.Resources = new Resources(100_000, 100_000, 100_000);

        AssertRejected(CommandRejection.MainBuildingLevelTooLow, Build(BuildingType.Woodcutter));
    }

    [Fact]
    public void MainBuildingLimitCountsQueuedUpgrades()
    {
        var cap = BuildingRules.MaxOtherBuildingLevel(Data, _village.BuildingLevel(BuildingType.MainBuilding));
        _village.SetBuildingLevel(BuildingType.Woodcutter, cap - 1);
        _village.Resources = new Resources(100_000, 100_000, 100_000);
        Assert.True(_handler.Handle(Owner, Build(BuildingType.Woodcutter)).Success);

        AssertRejected(CommandRejection.MainBuildingLevelTooLow, Build(BuildingType.Woodcutter));
    }

    [Fact]
    public void QueuedMainBuildingUpgradeDoesNotRaiseTheLimit()
    {
        var cap = BuildingRules.MaxOtherBuildingLevel(Data, _village.BuildingLevel(BuildingType.MainBuilding));
        _village.SetBuildingLevel(BuildingType.Woodcutter, cap);
        _village.Resources = new Resources(100_000, 100_000, 100_000);
        Assert.True(_handler.Handle(Owner, Build(BuildingType.MainBuilding)).Success);

        AssertRejected(CommandRejection.MainBuildingLevelTooLow, Build(BuildingType.Woodcutter));
    }

    [Fact]
    public void MainBuildingItselfIsNotLimited()
    {
        var data = TestData.Mutated(GameData.BuildingsFile, root => root["maxOtherBuildingLevelByMainLevel"]![1] = 0);
        var state = TestMatch.Create(data: data);

        var result = new CommandHandler(state, data).Handle(Owner, Build(BuildingType.MainBuilding));

        Assert.True(result.Success);
    }

    [Theory]
    [InlineData(0, 1000, 1000)]
    [InlineData(1000, 0, 1000)]
    [InlineData(1000, 1000, 0)]
    public void RejectsWhenAnyResourceIsShort(double wood, double stone, double food)
    {
        _village.Resources = new Resources(wood, stone, food);

        AssertRejected(CommandRejection.InsufficientResources, Build(BuildingType.Woodcutter));
    }
}
