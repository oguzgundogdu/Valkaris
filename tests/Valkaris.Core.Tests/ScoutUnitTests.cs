using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class ScoutUnitTests
{
    private const int Attacker = 1;
    private const int Defender = 2;

    private static readonly GameData Data = TestData.Shipped;

    private readonly GameState _state = TestMatch.Create(duration: 36000);
    private readonly CommandHandler _handler;
    private readonly Simulation _simulation;
    private readonly Village _home;
    private readonly Village _target;

    public ScoutUnitTests()
    {
        _handler = new CommandHandler(_state, Data);
        _simulation = new Simulation(_state, Data);
        _home = _state.Villages[Attacker];
        _target = _state.Villages[Defender];
        Clear(_home);
        Clear(_target);
    }

    private static void Clear(Village village)
    {
        foreach (var (unit, count) in village.Troops.ToList())
            village.RemoveTroops(unit, count);
    }

    private BattleReport Fight(int swordsmen, int defendingSpearmen, int defendingScouts)
    {
        _home.AddTroops(UnitType.Swordsman, swordsmen);
        _target.AddTroops(UnitType.Spearman, defendingSpearmen);
        _target.AddTroops(UnitType.Scout, defendingScouts);
        var command = new SendTroopsCommand(Attacker, Defender, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = swordsmen });
        Assert.True(_handler.Handle(Attacker, command).Success);
        _simulation.AdvanceTo(_state.Movements.Single().ArrivalTime);
        return _state.Reports.Single();
    }

    [Fact]
    public void ItIsCheapAndTheFastestUnitAndCarriesNothing()
    {
        var scout = Data.Units.Units[UnitType.Scout];
        var total = scout.Cost.Wood + scout.Cost.Stone + scout.Cost.Food;
        var spear = Data.Units.Units[UnitType.Spearman].Cost;

        Assert.All(Data.Units.Units.Where(u => u.Key != UnitType.Scout), u => Assert.True(scout.Speed > u.Value.Speed));
        Assert.True(total < spear.Wood + spear.Stone + spear.Food);
        Assert.Equal(0, scout.LootCapacity);
        Assert.Equal(0, scout.DefenceVsInfantry);
        Assert.Equal(0, scout.DefenceVsCavalry);
    }

    [Theory]
    [InlineData(MovementType.Attack)]
    [InlineData(MovementType.Raid)]
    public void ItCantAttackOrRaid(MovementType type)
    {
        _home.AddTroops(UnitType.Scout, 3);
        _home.AddTroops(UnitType.Swordsman, 3);
        var troops = new Dictionary<UnitType, int> { [UnitType.Scout] = 1, [UnitType.Swordsman] = 1 };

        Assert.Equal(CommandRejection.ScoutsCannotFight, _handler.Handle(Attacker, new SendTroopsCommand(Attacker, Defender, type, troops)).Rejection);
    }

    [Fact]
    public void AnyNumberOfScoutsLeavesTheBattleUnchanged()
    {
        var without = Fight(20, 10, 0);

        var other = new ScoutUnitTests();
        var with = other.Fight(20, 10, 500);

        Assert.Equal(without.DefencePower, with.DefencePower);
        Assert.Equal(without.AttackerLost, with.AttackerLost);
        Assert.Equal(without.DefenderLost, with.DefenderLost);
        Assert.DoesNotContain(UnitType.Scout, with.DefenderPresent.Keys);
        Assert.DoesNotContain(UnitType.Scout, with.DefenderLost.Keys);
    }

    [Fact]
    public void ABattleLeavesTheScoutsAlive()
    {
        Fight(20, 1, 7);

        Assert.Equal(7, _target.TroopCount(UnitType.Scout));
    }

    [Fact]
    public void ScoutsAloneStopNobody()
    {
        Assert.True(Fight(1, 0, 50).AttackerWon);
    }

    [Fact]
    public void ScoutsDieWhenTheVillageIsCaptured()
    {
        _home.AddTroops(UnitType.Swordsman, 20);
        _home.AddTroops(UnitType.Lord, 1);
        _target.AddTroops(UnitType.Scout, 7);
        _target.Loyalty = 1;
        var troops = new Dictionary<UnitType, int> { [UnitType.Swordsman] = 20, [UnitType.Lord] = 1 };
        Assert.True(_handler.Handle(Attacker, new SendTroopsCommand(Attacker, Defender, MovementType.Attack, troops)).Success);

        _simulation.AdvanceTo(_state.Movements.Single().ArrivalTime);

        Assert.Equal(Attacker, _target.OwnerId);
        Assert.Equal(0, _target.TroopCount(UnitType.Scout));
    }

    [Fact]
    public void ReinforcingScoutsJoinTheVillage()
    {
        var second = new Village(50, Attacker, new TileCoord(_home.Position.X + 1, _home.Position.Y));
        _state.AddVillage(second);
        _home.AddTroops(UnitType.Scout, 3);
        var command = new SendTroopsCommand(Attacker, second.Id, MovementType.Reinforce, new Dictionary<UnitType, int> { [UnitType.Scout] = 3 });
        Assert.True(_handler.Handle(Attacker, command).Success);

        _simulation.AdvanceTo(_state.Movements.Single().ArrivalTime);

        Assert.Equal(3, second.TroopCount(UnitType.Scout));
    }
}
