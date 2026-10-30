using Valkaris.Core.Events;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class AiExpansionTests
{
    private const int Ai = 1;
    private const int Enemy = 2;

    private static AiMatch Match(AiDifficulty difficulty = AiDifficulty.Hard, ulong seed = 1) =>
        new(new[] { difficulty, AiDifficulty.Normal }, seed: seed, idle: new[] { Enemy });

    private static Village Prepared(AiMatch match, int mainLevel = 10)
    {
        var village = match.State.Villages[Ai];
        village.SetBuildingLevel(BuildingType.MainBuilding, mainLevel);
        village.SetBuildingLevel(BuildingType.Barracks, 5);
        village.SetBuildingLevel(BuildingType.Warehouse, 8);
        foreach (var producer in new[] { BuildingType.Woodcutter, BuildingType.Quarry, BuildingType.Farm })
            village.SetBuildingLevel(producer, 8);
        return village;
    }

    private static List<GameEvent> Listen(AiMatch match)
    {
        var events = new List<GameEvent>();
        match.Session.EventRaised += events.Add;
        return events;
    }

    [Fact]
    public void ASettlerIsTrainedAndFoundsAVillage()
    {
        var match = Match();
        Prepared(match);
        var events = Listen(match);

        match.Run(1800);

        Assert.NotEmpty(events.OfType<VillageFounded>());
        Assert.Equal(0, match.RejectedCommands);
    }

    [Fact]
    public void NoSettlersBeforeTheProfilesDelay()
    {
        var match = Match();
        var village = Prepared(match);

        match.Run(match.Data.Ai.Difficulties[AiDifficulty.Hard].ExpansionDelaySeconds - 30);

        Assert.DoesNotContain(village.TrainingQueue, q => q.Unit == UnitType.Settler);
        Assert.Equal(0, village.TroopCount(UnitType.Settler));
    }

    [Theory]
    [InlineData(AiDifficulty.Easy)]
    [InlineData(AiDifficulty.Hard)]
    public void ItStopsAtItsWantedVillages(AiDifficulty difficulty)
    {
        var match = Match(difficulty);
        Prepared(match);

        match.Run(3000);

        var founded = match.State.Villages.Values.Count(v => v.OwnerId == Ai);
        Assert.True(founded <= match.Data.Ai.Difficulties[difficulty].WantedVillages + match.State.Players[Ai].CapturedVillageIds.Count, $"{founded} villages");
    }

    [Fact]
    public void TheVillageLimitStopsSettlers()
    {
        var match = Match();
        var village = Prepared(match, mainLevel: 1);
        village.SetBuildingLevel(BuildingType.MainBuilding, 1);
        var data = match.Data;

        match.Session.Tick(data.Ai.Difficulties[AiDifficulty.Hard].ExpansionDelaySeconds + 20);

        Assert.DoesNotContain(village.TrainingQueue, q => q.Unit == UnitType.Settler);
    }

    [Fact]
    public void FoundedTilesFollowTheRules()
    {
        foreach (var seed in new ulong[] { 1, 2, 3, 4, 5 })
        {
            var match = Match(seed: seed);
            Prepared(match);
            var events = Listen(match);

            match.Run(2400);

            foreach (var founded in events.OfType<VillageFounded>())
            {
                var village = match.State.Villages[founded.VillageId];
                Assert.NotEqual(Terrain.Water, match.State.Map.TileAt(village.Position).Terrain);
            }
            Assert.Equal(0, match.RejectedCommands);
        }
    }

    [Fact]
    public void ANewVillagePrefersToBeNearHomeAndFarFromEnemies()
    {
        var match = Match();
        Prepared(match);
        var events = Listen(match);

        match.Run(1800);

        var founded = match.State.Villages[events.OfType<VillageFounded>().First().VillageId];
        Assert.True(UnitRules.Distance(founded.Position, match.State.Villages[Ai].Position)
            < UnitRules.Distance(founded.Position, match.State.Villages[Enemy].Position));
    }

    // A new capital.

    private static (AiMatch Match, Village Safe, Village Other) LoseCapital(Action<Village, Village>? setup = null)
    {
        var match = Match();
        var camps = match.State.Villages.Values.Where(v => v.IsBarbarian).OrderBy(v => v.Id).Take(2).ToList();
        foreach (var camp in camps)
        {
            camp.OwnerId = Ai;
            camp.BarbarianTier = null;
            camp.NextTroopRegenTime = null;
        }
        setup?.Invoke(camps[0], camps[1]);

        var capital = match.State.Villages[Ai];
        foreach (var (unit, count) in capital.Troops.ToList())
            capital.RemoveTroops(unit, count);
        capital.Loyalty = 1;
        var enemy = match.State.Villages[Enemy];
        enemy.AddTroops(UnitType.Swordsman, 200);
        enemy.AddTroops(UnitType.Lord, 1);
        Assert.True(match.Session.SendAs(Enemy, new Valkaris.Core.Commands.SendTroopsCommand(Enemy, Ai, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 200, [UnitType.Lord] = 1 })).Success);

        return (match, camps[0], camps[1]);
    }

    [Fact]
    public void ANewCapitalIsChosenAtOnceByCommand()
    {
        var (match, _, _) = LoseCapital();
        var events = Listen(match);

        match.Run(600, step: 1);

        Assert.Single(events.OfType<CapitalChoiceRequired>());
        Assert.Empty(events.OfType<CapitalChanged>());
        Assert.NotNull(match.State.Players[Ai].CapitalVillageId);
        Assert.Null(match.State.Players[Ai].CapitalChoiceDeadline);
    }

    [Fact]
    public void ALowMainBuildingLosesLessToThePenalty()
    {
        var (match, low, high) = LoseCapital((a, b) =>
        {
            a.SetBuildingLevel(BuildingType.MainBuilding, 1);
            b.SetBuildingLevel(BuildingType.MainBuilding, 10);
        });

        match.Run(600, step: 1);

        Assert.Equal(low.Id, match.State.Players[Ai].CapitalVillageId);
        Assert.Equal(10, high.BuildingLevel(BuildingType.MainBuilding));
    }

    [Fact]
    public void AnEliminatedAiStopsDeciding()
    {
        var match = new AiMatch(new[] { AiDifficulty.Hard, AiDifficulty.Hard, AiDifficulty.Hard }, idle: new[] { 2, 3 });
        var capital = match.State.Villages[Ai];
        foreach (var (unit, count) in capital.Troops.ToList())
            capital.RemoveTroops(unit, count);
        capital.Loyalty = 1;
        var enemy = match.State.Villages[Enemy];
        enemy.AddTroops(UnitType.Swordsman, 200);
        enemy.AddTroops(UnitType.Lord, 1);
        Assert.True(match.Session.SendAs(Enemy, new Valkaris.Core.Commands.SendTroopsCommand(Enemy, Ai, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Swordsman] = 200, [UnitType.Lord] = 1 })).Success);

        match.Run(1200);

        Assert.True(match.State.Players[Ai].IsEliminated);
        Assert.Equal(0, match.RejectedCommands);
    }
}
