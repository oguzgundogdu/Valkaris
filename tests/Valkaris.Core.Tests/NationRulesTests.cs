using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;
using Xunit;

namespace Valkaris.Core.Tests;

/// <summary>Each bonus through the real rules and previews, with values made up for the test so tuning can't touch them.</summary>
public class NationRulesTests
{
    private const int Self = 1;
    private const int Rival = 2;

    private static readonly GameData Data = GameData.Load(new MutatedDataSource(GameData.NationsFile, root =>
    {
        var karuk = root["nations"]!["Karuk"]!;
        karuk["wallBonus"] = 0.5;
        karuk["warehouseCapacity"] = 2;
        karuk["unitSpeed"] = 0.5;
        karuk["unitCost"] = 2;
        karuk["trainingTime"] = 0.5;
        karuk["lootCapacity"] = 3;
        karuk["buildTime"] = 0.5;
        karuk["scoutStrength"] = 2;
    }));

    private readonly GameState _state = GameFactory.CreateMatch(
        new GameSettings(3600, 1, MapSize.Small, 1),
        new[] { new PlayerSlot("Self", Nation.Karuk, false), new PlayerSlot("Rival", Nation.Valdren, true) },
        Data);

    private Village Home => _state.Villages[Self];
    private Village Other => _state.Villages[Rival];

    [Fact]
    public void TheMissingOwnerIsNeutral() => Assert.Same(NationConfig.Neutral, NationRules.Of(Data, _state, Village.BarbarianOwnerId));

    [Fact]
    public void WarehouseCapacityFollowsTheOwner()
    {
        var plain = BuildingRules.WarehouseCapacity(Data, Home.BuildingLevel(BuildingType.Warehouse));
        Assert.Equal(plain * 2, BuildingRules.VillageCapacity(Data, _state, Home));
        Assert.Equal(plain, BuildingRules.VillageCapacity(Data, _state, Other));

        Other.OwnerId = Self;
        Assert.Equal(plain * 2, BuildingRules.VillageCapacity(Data, _state, Other));
    }

    [Fact]
    public void BuildTimeReachesThePreview()
    {
        var plain = BuildingRules.BuildTimeSeconds(Data, BuildingType.Farm, Home.BuildingLevel(BuildingType.Farm) + 1);
        Assert.Equal(plain / 2, BuildPreview.Evaluate(_state, Data, Self, Self, BuildingType.Farm).DurationSeconds);
        Assert.Equal(plain, BuildPreview.Evaluate(_state, Data, Rival, Rival, BuildingType.Farm).DurationSeconds);
    }

    [Fact]
    public void CostAndTrainingTimeReachThePreview()
    {
        Home.SetBuildingLevel(BuildingType.Barracks, 1);
        Home.Resources = new Resources(900, 900, 900);
        var plainCost = Data.Units.Units[UnitType.Spearman].Cost;

        var option = TrainPreview.Evaluate(_state, Data, Self, Self, UnitType.Spearman, 2);

        Assert.Equal(plainCost * 4, option.Cost);
        Assert.Equal(UnitRules.SecondsPerUnit(Data, UnitType.Spearman, 1), option.DurationSeconds);
        Assert.Equal(UnitRules.MaxAffordable(Data, UnitType.Spearman, Home.Resources, NationRules.Of(Data, _state, Self)), option.MaxCount);
    }

    [Fact]
    public void TheHandlerChargesWhatThePreviewSays()
    {
        Home.SetBuildingLevel(BuildingType.Barracks, 1);
        Home.Resources = new Resources(900, 900, 900);
        var option = TrainPreview.Evaluate(_state, Data, Self, Self, UnitType.Spearman, 2);

        var result = new CommandHandler(_state, Data).Handle(Self, new TrainCommand(Self, UnitType.Spearman, 2));

        Assert.True(result.Success);
        Assert.Equal(new Resources(900, 900, 900) - option.Cost, Home.Resources);
    }

    [Fact]
    public void UnitSpeedReachesTheSendPreview()
    {
        Home.AddTroops(UnitType.Spearman, 1);
        Other.AddTroops(UnitType.Spearman, 1);
        var troops = new Dictionary<UnitType, int> { [UnitType.Spearman] = 1 };
        var plain = UnitRules.TravelSeconds(Data, _state, Home.Position, Other.Position, troops);

        Assert.Equal(plain * 2, SendPreview.Evaluate(_state, Data, Self, new SendTroopsCommand(Self, Rival, MovementType.Raid, troops)).DurationSeconds, 6);
        Assert.Equal(plain, SendPreview.Evaluate(_state, Data, Rival, new SendTroopsCommand(Rival, Self, MovementType.Raid, troops)).DurationSeconds, 6);
    }

    [Fact]
    public void TheWallBonusScalesDefence()
    {
        var attackers = new Dictionary<UnitType, int> { [UnitType.Swordsman] = 1 };
        var defenders = new Dictionary<UnitType, int> { [UnitType.Spearman] = 10 };
        var bare = CombatResolver.Resolve(Data, MovementType.Attack, attackers, defenders, 0).DefencePower;
        var plain = CombatResolver.Resolve(Data, MovementType.Attack, attackers, defenders, 4).DefencePower;
        var halved = CombatResolver.Resolve(Data, MovementType.Attack, attackers, defenders, 4, 0.5).DefencePower;

        Assert.Equal((plain - bare) / 2, halved - bare, 6);
    }

    [Fact]
    public void LootCapacityFollowsTheAttacker()
    {
        Other.Resources = new Resources(5000, 5000, 5000);
        Other.SetBuildingLevel(BuildingType.Warehouse, 1);
        var survivors = new Dictionary<UnitType, int> { [UnitType.Spearman] = 3 };
        var carry = 3 * Data.Units.Units[UnitType.Spearman].LootCapacity;

        var byKaruk = LootRules.Calculate(Data, _state, Other, survivors, Self);
        var byValdren = LootRules.Calculate(Data, _state, Home, survivors, Rival);

        Assert.InRange(byKaruk.Wood + byKaruk.Stone + byKaruk.Food, carry * 3 - 3, carry * 3);
        Assert.True(byValdren.Wood + byValdren.Stone + byValdren.Food <= carry);
    }

    [Fact]
    public void ScoutStrengthHelpsAtHome()
    {
        Home.AddTroops(UnitType.Scout, 5);
        Other.AddTroops(UnitType.Scout, 5);

        Assert.Equal(10, ScoutRules.DefendingStrength(Data, _state, Home), 6);
        Assert.Equal(5, ScoutRules.DefendingStrength(Data, _state, Other), 6);
    }

    [Fact]
    public void ANationCantBeLeftOut()
    {
        var problem = Assert.Throws<GameDataException>(() => GameData.Load(new MutatedDataSource(GameData.NationsFile, root => root["nations"]!.AsObject().Remove("Solari"))));
        Assert.Contains("Solari", problem.Message);
    }

    [Fact]
    public void ABonusMustBePositive()
    {
        var problem = Assert.Throws<GameDataException>(() => GameData.Load(new MutatedDataSource(GameData.NationsFile, root => root["nations"]!["Karuk"]!["unitSpeed"] = 0)));
        Assert.Contains("UnitSpeed", problem.Message);
    }
}
