using Valkaris.Ai;
using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class AiSurrenderTests
{
    private const int Ai = 2;
    private const int Enemy = 1;

    private readonly GameState _state = TestMatch.Create(players: 3, duration: 36000);
    private readonly AiView _view;
    private readonly AiMemory _memory;
    private readonly SurrenderPlanner _planner;
    private int _reportId = 1;

    public AiSurrenderTests()
    {
        _state.Players[Ai].Difficulty = AiDifficulty.Normal;
        var handler = new CommandHandler(_state, TestData.Shipped);
        _view = new AiView(_state, TestData.Shipped, Ai, command => handler.Handle(Ai, command));
        _memory = new AiMemory(_view);
        _planner = new SurrenderPlanner(_view, _memory);
    }

    private AiProfile Profile => TestData.Shipped.Ai.Difficulties[AiDifficulty.Normal];

    private static void ClearTroops(Village village)
    {
        foreach (var (unit, count) in village.Troops.ToList())
            village.RemoveTroops(unit, count);
    }

    private Village GiveCamp()
    {
        var camp = _state.Villages.Values.First(v => v.IsBarbarian);
        camp.OwnerId = Ai;
        camp.BarbarianTier = null;
        camp.NextTroopRegenTime = null;
        ClearTroops(camp);
        return camp;
    }

    private void SeenBy(double attackPower, int attacker = Enemy, int defender = Ai)
    {
        var none = new Dictionary<UnitType, int>();
        _state.AddReport(new BattleReport(_reportId++, _state.ElapsedTime, MovementType.Attack, attacker, defender, attacker, defender, none, none, none, none, 0, attackPower, attackPower, true, Resources.Zero, null));
    }

    /// <summary>Two villages at best, one left, a capital lost, no army, and a huge force in the reports.</summary>
    private void MakeHopeless()
    {
        var camp = GiveCamp();
        ClearTroops(_state.Villages[Ai]);
        _planner.Plan();
        camp.OwnerId = Enemy;
        _planner.NoteCapitalLost();
        SeenBy(10000);
        _memory.Update();
    }

    private void Wait(double gameSeconds) => _state.ElapsedTime += gameSeconds;

    [Fact]
    public void AHopelessPlayerSurrendersAfterTheHold()
    {
        MakeHopeless();

        Assert.False(_planner.Plan());
        Assert.False(_state.Players[Ai].IsEliminated);

        Wait(Profile.SurrenderHoldSeconds);

        Assert.True(_planner.Plan());
        Assert.True(_state.Players[Ai].HasSurrendered);
    }

    [Fact]
    public void WithoutLosingACapitalItFightsOn()
    {
        var kept = GiveCamp();
        ClearTroops(_state.Villages[Ai]);
        foreach (var lost in _state.Villages.Values.Where(v => v.IsBarbarian).Take(3).ToList())
        {
            lost.OwnerId = Ai;
            lost.BarbarianTier = null;
        }
        _planner.Plan();
        foreach (var lost in _state.Villages.Values.Where(v => v.OwnerId == Ai && v.Id != kept.Id && v.Id != Ai))
            lost.OwnerId = Enemy;
        SeenBy(10000);
        _memory.Update();
        _planner.Plan();
        Wait(Profile.SurrenderHoldSeconds);

        Assert.False(_planner.Plan());
    }

    [Fact]
    public void WithOneVillageLeftNoCapitalNeedsToHaveFallen()
    {
        var camp = GiveCamp();
        ClearTroops(_state.Villages[Ai]);
        _planner.Plan();
        camp.OwnerId = Enemy;
        SeenBy(10000);
        _memory.Update();
        _planner.Plan();
        Wait(Profile.SurrenderHoldSeconds);

        Assert.True(_planner.Plan());
    }

    [Fact]
    public void KeepingMostVillagesItFightsOn()
    {
        MakeHopeless();
        var kept = _state.Villages.Values.Where(v => v.IsBarbarian).Skip(1).First();
        kept.OwnerId = Ai;
        kept.BarbarianTier = null;
        _planner.Plan();
        Wait(Profile.SurrenderHoldSeconds);

        Assert.False(_planner.Plan());
    }

    [Fact]
    public void WithAStrongArmyItFightsOn()
    {
        MakeHopeless();
        _state.Villages[Ai].AddTroops(UnitType.Swordsman, 1000);
        _planner.Plan();
        Wait(Profile.SurrenderHoldSeconds);

        Assert.False(_planner.Plan());
    }

    [Fact]
    public void AnArmyOnTheMoveCounts()
    {
        MakeHopeless();
        var home = _state.Villages[Ai];
        home.AddTroops(UnitType.Swordsman, 1000);
        Assert.True(_view.Send(new SendTroopsCommand(Ai, Enemy, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 1000 })));
        _planner.Plan();
        Wait(Profile.SurrenderHoldSeconds);

        Assert.False(_planner.Plan());
    }

    [Fact]
    public void RecoveringRestartsTheHold()
    {
        MakeHopeless();
        _planner.Plan();
        Wait(Profile.SurrenderHoldSeconds / 2);
        _state.Villages[Ai].AddTroops(UnitType.Swordsman, 1000);
        _planner.Plan();
        _state.Villages[Ai].RemoveTroops(UnitType.Swordsman, 1000);
        Wait(Profile.SurrenderHoldSeconds / 2);

        Assert.False(_planner.Plan());
    }

    [Fact]
    public void BarbariansDoNotCountAsAnEnemyForce()
    {
        SeenBy(100000, attacker: Ai, defender: Village.BarbarianOwnerId);
        _memory.Update();

        Assert.Equal(0, _memory.StrongestEnemySeen);
    }

    [Fact]
    public void AttacksOnThirdPartiesTeachItNothing()
    {
        SeenBy(100000, attacker: Enemy, defender: 3);
        _memory.Update();

        Assert.Equal(0, _memory.StrongestEnemySeen);
    }

    [Fact]
    public void GarrisonsItAttackedCount()
    {
        SeenBy(777, attacker: Ai, defender: Enemy);
        _memory.Update();

        Assert.Equal(777, _memory.StrongestEnemySeen);
    }

    [Fact]
    public void EasyGivesUpSoonerThanHard()
    {
        var levels = TestData.Shipped.Ai.Difficulties;
        Assert.True(levels[AiDifficulty.Easy].SurrenderVillageShare > levels[AiDifficulty.Hard].SurrenderVillageShare);
        Assert.True(levels[AiDifficulty.Easy].SurrenderStrengthRatio > levels[AiDifficulty.Hard].SurrenderStrengthRatio);
        Assert.True(levels[AiDifficulty.Easy].SurrenderHoldSeconds < levels[AiDifficulty.Hard].SurrenderHoldSeconds);
    }
}
