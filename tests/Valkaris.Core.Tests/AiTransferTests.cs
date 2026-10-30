using Valkaris.Core.Events;
using Valkaris.Core.Model;

namespace Valkaris.Core.Tests;

public class AiTransferTests
{
    private const int Ai = 1;

    private static (AiMatch Match, Village Home, Village Camp) Setup(AiDifficulty difficulty = AiDifficulty.Hard, int marketplace = 3)
    {
        var match = new AiMatch(new[] { difficulty, AiDifficulty.Normal }, idle: new[] { 2 });
        var home = match.State.Villages[Ai];
        home.SetBuildingLevel(BuildingType.Marketplace, marketplace);
        home.SetBuildingLevel(BuildingType.Warehouse, 5);
        home.Resources = new Resources(3000, 3000, 3000);

        var camp = match.State.Villages.Values.First(v => v.IsBarbarian);
        camp.OwnerId = Ai;
        camp.BarbarianTier = null;
        camp.NextTroopRegenTime = null;
        camp.Resources = Resources.Zero;
        foreach (var (unit, count) in camp.Troops.ToList())
            camp.RemoveTroops(unit, count);
        return (match, home, camp);
    }

    private static List<ResourcesDelivered> Deliveries(AiMatch match)
    {
        var deliveries = new List<ResourcesDelivered>();
        match.Session.EventRaised += e =>
        {
            if (e is ResourcesDelivered delivered)
                deliveries.Add(delivered);
        };
        return deliveries;
    }

    [Fact]
    public void ACapturedVillageIsSuppliedAndBuildsUp()
    {
        var (match, _, camp) = Setup();
        var deliveries = Deliveries(match);

        match.Run(600);

        Assert.Contains(deliveries, d => d.VillageId == camp.Id);
        Assert.True(camp.BuildingLevel(BuildingType.Woodcutter) >= 1);
        Assert.Equal(0, match.RejectedCommands);
    }

    [Fact]
    public void WithoutAMarketplaceNothingIsAttempted()
    {
        var (match, home, _) = Setup(marketplace: 0);
        home.SetBuildingLevel(BuildingType.MainBuilding, 1);
        home.Resources = new Resources(100, 100, 100);

        match.Session.Tick(20);

        Assert.DoesNotContain(match.State.Movements, m => m.Type == MovementType.Transfer);
        Assert.Equal(0, match.RejectedCommands);
    }

    [Fact]
    public void ASecondVillageGetsTheCapitalAMarketplace()
    {
        var (match, home, _) = Setup(marketplace: 0);

        match.Session.Tick(20);

        Assert.Contains(home.BuildQueue, q => q.Building == BuildingType.Marketplace);
    }

    [Fact]
    public void AloneThereIsNoMarketplace()
    {
        var match = new AiMatch(new[] { AiDifficulty.Hard, AiDifficulty.Normal }, idle: new[] { 2 });
        match.State.Villages[Ai].Resources = new Resources(900, 900, 900);

        match.Session.Tick(20);

        Assert.DoesNotContain(match.State.Villages[Ai].BuildQueue, q => q.Building == BuildingType.Marketplace);
    }

    [Fact]
    public void DonorsKeepTheirShare()
    {
        var (match, home, _) = Setup();
        home.Resources = new Resources(100, 100, 100);

        match.Session.Tick(20);

        Assert.DoesNotContain(match.State.Movements, m => m.Type == MovementType.Transfer);
    }

    [Fact]
    public void LoadsNeverOverflowTheTarget()
    {
        var (match, _, camp) = Setup();
        var deliveries = Deliveries(match);

        match.Run(1800);

        Assert.NotEmpty(deliveries);
        Assert.All(deliveries, d => Assert.Equal(Resources.Zero, d.Lost));
    }

    [Fact]
    public void OneTransferAtATimeForATarget()
    {
        var (match, _, camp) = Setup();

        match.Session.Tick(20);
        match.Session.Tick(10);

        Assert.True(match.State.Movements.Count(m => m.Type == MovementType.Transfer && m.ToVillageId == camp.Id) <= 1);
    }

    [Fact]
    public void AVillageUnderAttackIsNotSupplied()
    {
        var (match, _, camp) = Setup();
        match.State.Villages[2].AddTroops(UnitType.Spearman, 1);
        Assert.True(match.Session.SendAs(2, new Valkaris.Core.Commands.SendTroopsCommand(2, camp.Id, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Spearman] = 1 })).Success);

        match.Session.Tick(20);

        Assert.DoesNotContain(match.State.Movements, m => m.Type == MovementType.Transfer);
    }
}
