using Valkaris.Core.Data;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class VisibilityTests
{
    private const int Me = 1;
    private const int Other = 2;
    private const int Third = 3;

    private static readonly GameData Data = TestData.Shipped;
    private static readonly IReadOnlyDictionary<UnitType, int> Troops = new Dictionary<UnitType, int> { [UnitType.Spearman] = 5 };

    private readonly GameState _state = TestMatch.Create(players: 3, duration: 36000);

    private TileCoord Mine => _state.Villages[Me].Position;

    private Movement Passing(MovementType type, int dx, int owner = Other) =>
        new(_state.NextMovementId++, owner, owner, null, type, Troops, Resources.Zero, 0, 100, null,
            new TileCoord(Mine.X + dx, Mine.Y), new TileCoord(Mine.X + dx, Mine.Y));

    [Fact]
    public void AMovementShowsOnlyWithinTheRadius()
    {
        var radius = (int)Data.Map.SightRadius;
        _state.AddMovement(Passing(MovementType.Reinforce, radius));
        _state.AddMovement(Passing(MovementType.Reinforce, radius + 1));

        Assert.Equal(_state.Movements[0].Id, Visibility.SightedMovements(_state, Data, Me).Single().Id);
    }

    [Fact]
    public void AWatchtowerSeesFurther()
    {
        var beyond = (int)Data.Map.SightRadius + 1;
        _state.AddMovement(Passing(MovementType.Reinforce, beyond));
        Assert.Empty(Visibility.SightedMovements(_state, Data, Me));

        _state.Villages[Me].SetBuildingLevel(BuildingType.Watchtower, (int)Math.Ceiling(1 / Data.Map.SightPerWatchtowerLevel));

        Assert.Single(Visibility.SightedMovements(_state, Data, Me));
    }

    [Fact]
    public void EveryVillageOfThePlayerCounts()
    {
        var near = new Village(99, Me, new TileCoord(Mine.X + 50, Mine.Y));
        _state.AddVillage(near);
        _state.AddMovement(Passing(MovementType.Reinforce, 50));

        Assert.Single(Visibility.SightedMovements(_state, Data, Me));
    }

    [Fact]
    public void ScoutsAreNeverSeen()
    {
        _state.AddMovement(Passing(MovementType.Scout, 0));

        Assert.Empty(Visibility.SightedMovements(_state, Data, Me));
    }

    [Fact]
    public void OwnMovementsAndIncomingAttacksAreNotSightings()
    {
        _state.AddMovement(Passing(MovementType.Reinforce, 0, owner: Me));
        _state.AddMovement(new Movement(_state.NextMovementId++, Other, Other, Me, MovementType.Attack, Troops, Resources.Zero, 0, 1000, null));

        Assert.Empty(Visibility.SightedMovements(_state, Data, Me));
        Assert.True(Visibility.IsIncoming(_state, Me, _state.Movements[1]));
    }

    [Fact]
    public void ASightingMovesAsTheMovementDoes()
    {
        _state.AddMovement(new Movement(_state.NextMovementId++, Other, Other, null, MovementType.Settle, Troops, Resources.Zero, 0, 100, null,
            new TileCoord(Mine.X - 2, Mine.Y), new TileCoord(Mine.X + 2, Mine.Y)));
        _state.ElapsedTime = 25;

        var seen = Visibility.SightedMovements(_state, Data, Me).Single();

        Assert.Equal(Mine.X + 1, seen.X, 6);
        Assert.Equal(Mine.Y, seen.Y, 6);
        Assert.Equal(-0.04, seen.VelocityX, 6);
    }

    private void Fight(int attacker, int target)
    {
        var from = _state.Villages[attacker];
        _state.AddMovement(new Movement(_state.NextMovementId++, attacker, from.Id, target, MovementType.Raid, Troops, Resources.Zero, 0, 1, null));
        new LocalGameSession(_state, Data, Me).Tick(2);
    }

    [Fact]
    public void BattlesBetweenOthersAreNewsCarryingOnlyNames()
    {
        Fight(Other, Third);

        var notice = Visibility.PublicNotices(_state, Me).Single();
        Assert.Equal(_state.Villages[Other].Name, notice.FromVillageName);
        Assert.Equal(_state.Villages[Third].Name, notice.TargetVillageName);
        Assert.Equal(new[] { "Time", "FromVillageName", "TargetVillageId", "TargetVillageName", "AttackerWon" }, typeof(PublicNotice).GetProperties().Select(p => p.Name).Where(n => n != "EqualityContract"));
    }

    [Fact]
    public void ThePlayersInABattleGetNoNoticeOfIt()
    {
        Fight(Other, Third);

        Assert.Empty(Visibility.PublicNotices(_state, Other));
        Assert.Empty(Visibility.PublicNotices(_state, Third));
    }

    [Fact]
    public void AttacksOnBarbariansAreNotNews()
    {
        var camp = new Village(99, Village.BarbarianOwnerId, new TileCoord(0, 0));
        _state.AddVillage(camp);

        Fight(Other, camp.Id);

        Assert.Empty(Visibility.PublicNotices(_state, Me));
    }

    [Fact]
    public void ANoticeKeepsTheNamesFromTheMomentOfTheBattle()
    {
        Fight(Other, Third);
        var name = _state.Villages[Third].Name;
        _state.Villages[Third].Name = "Renamed";

        Assert.Equal(name, Visibility.PublicNotices(_state, Me).Single().TargetVillageName);
    }

    [Fact]
    public void ClashesShowForOwnBattlesAndWithinSight()
    {
        var away = new Village(98, Other, new TileCoord(Mine.X + 50, Mine.Y));
        var close = new Village(99, Other, new TileCoord(Mine.X + 1, Mine.Y));

        Assert.True(Visibility.SeesClash(_state, Data, Me, new BattleArrival(0, Me, Other, "", Other, "")));
        Assert.True(Visibility.SeesClash(_state, Data, Me, new BattleArrival(0, Other, Me, "", Me, "")));

        _state.AddVillage(away);
        _state.AddVillage(close);
        Assert.False(Visibility.SeesClash(_state, Data, Me, new BattleArrival(0, Third, Other, "", away.Id, "")));
        Assert.True(Visibility.SeesClash(_state, Data, Me, new BattleArrival(0, Third, Other, "", close.Id, "")));
    }
}
