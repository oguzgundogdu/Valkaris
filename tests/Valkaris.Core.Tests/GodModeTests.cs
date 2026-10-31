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
