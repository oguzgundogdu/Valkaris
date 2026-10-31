using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class BuildPreviewTests
{
    private const int Owner = 1;
    private const int VillageId = 1;

    private static readonly GameData Data = TestData.Shipped;

    private readonly GameState _state = TestMatch.Create();
    private Village Village => _state.Villages[VillageId];

    private BuildOption Evaluate(BuildingType building, int player = Owner, int villageId = VillageId) =>
        BuildPreview.Evaluate(_state, Data, player, villageId, building);

    private void Rich() => Village.Resources = new Resources(100_000, 100_000, 100_000);

    [Fact]
    public void AffordableUpgradeCanBeBuilt()
    {
        var option = Evaluate(BuildingType.Woodcutter);

        Assert.True(option.CanBuild);
        Assert.Null(option.Rejection);
        Assert.Equal(2, option.TargetLevel);
        Assert.Equal(BuildingRules.UpgradeCost(Data, BuildingType.Woodcutter, 2), option.Cost);
        Assert.Equal(BuildingRules.BuildTimeSeconds(Data, BuildingType.Woodcutter, 2), option.DurationSeconds);
        Assert.Equal(0, option.StartTime);
        Assert.False(option.CostExceedsCapacity);
    }

    [Fact]
    public void EvaluatingDoesNotChangeState()
    {
        var before = SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(_state));

        Evaluate(BuildingType.Woodcutter);

        Assert.Equal(before, SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(_state)));
    }

    [Fact]
    public void DurationIsScaledBySpeed()
    {
        var fast = TestMatch.Create(speed: 2);

        var option = BuildPreview.Evaluate(fast, Data, Owner, VillageId, BuildingType.Woodcutter);

        Assert.Equal(BuildingRules.BuildTimeSeconds(Data, BuildingType.Woodcutter, 2) / 2, option.DurationSeconds);
    }

    [Fact]
    public void QueuedItemsMoveTheStartTimeAndTargetLevel()
    {
        var handler = new CommandHandler(_state, Data);
        handler.Handle(Owner, new BuildCommand(VillageId, BuildingType.Woodcutter));

        var option = Evaluate(BuildingType.Woodcutter);

        Assert.Equal(3, option.TargetLevel);
        Assert.Equal(Village.BuildQueue[0].FinishTime, option.StartTime);
    }

    [Fact]
    public void MatchOver()
    {
        new Simulation(_state, TestData.Shipped).Advance(_state.Settings.MatchDurationSeconds!.Value);

        Assert.Equal(CommandRejection.MatchOver, Evaluate(BuildingType.Farm).Rejection);
    }

    [Fact]
    public void VillageNotFound() =>
        Assert.Equal(CommandRejection.VillageNotFound, Evaluate(BuildingType.Farm, villageId: 999).Rejection);

    [Fact]
    public void NotOwner() =>
        Assert.Equal(CommandRejection.NotOwner, Evaluate(BuildingType.Farm, player: 2).Rejection);

    [Fact]
    public void QueueFullStillShowsCostAndTarget()
    {
        Rich();
        var handler = new CommandHandler(_state, Data);
        for (var i = 0; i < Data.Game.MaxBuildQueueLength; i++)
            handler.Handle(Owner, new BuildCommand(VillageId, BuildingType.Woodcutter));

        var option = Evaluate(BuildingType.Quarry);

        Assert.Equal(CommandRejection.QueueFull, option.Rejection);
        Assert.Equal(2, option.TargetLevel);
        Assert.Equal(BuildingRules.UpgradeCost(Data, BuildingType.Quarry, 2), option.Cost);
    }

    [Fact]
    public void MaxLevelHasNoCost()
    {
        var max = Data.Buildings.Buildings[BuildingType.MainBuilding].MaxLevel;
        Village.SetBuildingLevel(BuildingType.MainBuilding, max);

        var option = Evaluate(BuildingType.MainBuilding);

        Assert.Equal(CommandRejection.MaxLevelReached, option.Rejection);
        Assert.Equal(Resources.Zero, option.Cost);
    }

    [Fact]
    public void MainBuildingTooLowStillShowsCost()
    {
        Rich();
        var cap = BuildingRules.MaxOtherBuildingLevel(Data, 1);
        Village.SetBuildingLevel(BuildingType.Woodcutter, cap);

        var option = Evaluate(BuildingType.Woodcutter);

        Assert.Equal(CommandRejection.MainBuildingLevelTooLow, option.Rejection);
        Assert.Equal(BuildingRules.UpgradeCost(Data, BuildingType.Woodcutter, cap + 1), option.Cost);
    }

    [Fact]
    public void InsufficientResourcesStillShowsCost()
    {
        Village.Resources = Resources.Zero;

        var option = Evaluate(BuildingType.Woodcutter);

        Assert.Equal(CommandRejection.InsufficientResources, option.Rejection);
        Assert.Equal(BuildingRules.UpgradeCost(Data, BuildingType.Woodcutter, 2), option.Cost);
    }

    [Fact]
    public void CostAboveWarehouseCapacityIsFlagged()
    {
        Village.SetBuildingLevel(BuildingType.MainBuilding, 10);
        Village.SetBuildingLevel(BuildingType.Woodcutter, 9);

        var option = Evaluate(BuildingType.Woodcutter);

        Assert.True(option.Cost.Stone > BuildingRules.VillageCapacity(Data, Village));
        Assert.True(option.CostExceedsCapacity);
    }

    [Fact]
    public void PreviewAndHandlerAgreeAcrossStates()
    {
        var setups = new Action<GameState>[]
        {
            _ => { },
            s => s.Villages[VillageId].Resources = Resources.Zero,
            s => s.Villages[VillageId].Resources = new Resources(100_000, 100_000, 100_000),
            s => s.Villages[VillageId].SetBuildingLevel(BuildingType.MainBuilding, 0),
            s =>
            {
                s.Villages[VillageId].SetBuildingLevel(BuildingType.Farm, 10);
                s.Villages[VillageId].SetBuildingLevel(BuildingType.MainBuilding, 10);
                s.Villages[VillageId].Resources = new Resources(100_000, 100_000, 100_000);
            },
            s => s.ElapsedTime = s.Settings.MatchDurationSeconds!.Value,
            s => s.Villages[VillageId].EnqueueBuild(new BuildQueueItem(BuildingType.Farm, 2, 0, 10)),
            s =>
            {
                s.Villages[VillageId].EnqueueBuild(new BuildQueueItem(BuildingType.Farm, 2, 0, 10));
                s.Villages[VillageId].EnqueueBuild(new BuildQueueItem(BuildingType.Quarry, 2, 10, 20));
            },
        };

        foreach (var setup in setups)
        {
            foreach (var building in Enum.GetValues<BuildingType>())
            {
                foreach (var player in new[] { Owner, 2 })
                {
                    var state = TestMatch.Create();
                    setup(state);

                    var preview = BuildPreview.Evaluate(state, Data, player, VillageId, building);
                    var result = new CommandHandler(state, Data).Handle(player, new BuildCommand(VillageId, building));

                    Assert.Equal(preview.Rejection, result.Rejection);
                }
            }
        }
    }
}
