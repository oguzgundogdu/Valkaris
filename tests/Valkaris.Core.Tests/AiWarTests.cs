using Valkaris.Core.Events;
using Valkaris.Core.Model;

namespace Valkaris.Core.Tests;

public class AiWarTests
{
    private const int Ai = 1;
    private const int Enemy = 2;

    private static AiMatch Match(AiDifficulty difficulty = AiDifficulty.Hard) =>
        new(new[] { difficulty, AiDifficulty.Normal }, idle: new[] { Enemy });

    private static void PastDelay(AiMatch match, AiDifficulty difficulty = AiDifficulty.Hard) =>
        match.Run(match.Data.Ai.Difficulties[difficulty].AggressionDelaySeconds + 1, step: 50);

    /// <summary>Leaves the enemy as the only target, by handing the AI every camp.</summary>
    private static void NoCamps(AiMatch match)
    {
        foreach (var camp in match.State.Villages.Values.Where(v => v.IsBarbarian))
        {
            camp.OwnerId = Ai;
            camp.BarbarianTier = null;
            camp.NextTroopRegenTime = null;
            foreach (var (unit, count) in camp.Troops.ToList())
                camp.RemoveTroops(unit, count);
        }
    }

    private static Movement? FirstAttack(AiMatch match) =>
        match.State.Movements.FirstOrDefault(m => m.OwnerId == Ai && m.Type == MovementType.Attack);

    [Fact]
    public void NoAttacksBeforeTheProfilesDelay()
    {
        var match = Match();
        match.State.Villages[Ai].AddTroops(UnitType.Swordsman, 500);

        match.Run(match.Data.Ai.Difficulties[AiDifficulty.Hard].AggressionDelaySeconds - 10);

        Assert.DoesNotContain(match.State.Reports, r => r.Type == MovementType.Attack);
        Assert.Null(FirstAttack(match));
    }

    [Fact]
    public void AnArmyGoesAfterAPlayerOnceItCan()
    {
        var match = Match();
        PastDelay(match);
        match.State.Villages[Ai].AddTroops(UnitType.Swordsman, 300);

        match.Run(60);

        Assert.True(match.State.Movements.Any(m => m.Type == MovementType.Attack && m.ToVillageId == Enemy) || match.State.Reports.Any(r => r.DefenderId == Enemy));
    }

    [Fact]
    public void AnAttackBeatsItsEstimateByTheMargin()
    {
        var match = Match();
        PastDelay(match);
        match.State.Villages[Ai].AddTroops(UnitType.Swordsman, 300);

        match.Run(16, step: 1);

        var attack = FirstAttack(match);
        Assert.NotNull(attack);
        var power = attack!.Troops.Sum(t => t.Value * match.Data.Units.Units[t.Key].Attack);
        var beliefs = match.Data.Ai.Beliefs;
        var profile = match.Data.Ai.Difficulties[AiDifficulty.Hard];
        var estimate = beliefs.PlayerPowerAtStart + beliefs.PlayerPowerPerMinute * match.State.ElapsedTime / 60;
        Assert.True(power >= (estimate - 100) * profile.AttackMargin, $"power {power}, estimate {estimate}");
    }

    [Fact]
    public void LordsStayHomeWithoutTheirMargin()
    {
        var match = Match();
        PastDelay(match);
        var village = match.State.Villages[Ai];
        village.AddTroops(UnitType.Lord, 1);

        match.Run(16, step: 1);

        Assert.Equal(1, village.TroopCount(UnitType.Lord));
    }

    [Fact]
    public void ALordGoesWithALargeEnoughArmyAndCapturesACamp()
    {
        var match = Match();
        PastDelay(match);
        var village = match.State.Villages[Ai];
        village.AddTroops(UnitType.Lord, 2);
        village.AddTroops(UnitType.Swordsman, 400);
        var captured = new List<VillageCaptured>();
        match.Session.EventRaised += e =>
        {
            if (e is VillageCaptured c)
                captured.Add(c);
        };

        match.Run(1800);

        Assert.Contains(captured, c => c.NewOwnerId == Ai && c.PreviousOwnerId == Village.BarbarianOwnerId);
        Assert.True(match.RejectedCommands == 0, $"{match.RejectedCommands} rejected");
    }

    [Fact]
    public void AnAiCapturesAPlayersVillageAndWins()
    {
        var match = Match();
        PastDelay(match);
        NoCamps(match);
        var village = match.State.Villages[Ai];
        village.AddTroops(UnitType.Lord, 2);
        village.AddTroops(UnitType.Swordsman, 600);

        match.Run(3000);

        Assert.True(match.State.IsMatchOver);
        Assert.Equal(new[] { Ai }, match.State.Outcome!.WinnerIds);
        Assert.Equal(MatchEndReason.LastPlayerStanding, match.State.Outcome.Reason);
    }

    [Fact]
    public void AProtectedCapitalLeadsToCatapultsAndThenCapture()
    {
        var match = Match();
        PastDelay(match);
        NoCamps(match);
        var village = match.State.Villages[Ai];
        village.SetBuildingLevel(BuildingType.Barracks, 5);
        village.SetBuildingLevel(BuildingType.Warehouse, 8);
        foreach (var producer in new[] { BuildingType.Woodcutter, BuildingType.Quarry, BuildingType.Farm })
            village.SetBuildingLevel(producer, 8);
        village.AddTroops(UnitType.Lord, 2);
        village.AddTroops(UnitType.Swordsman, 800);
        var capital = match.State.Villages[Enemy];
        var threshold = match.Data.Villages.Capital.LockedAboveMainLevel;
        capital.SetBuildingLevel(BuildingType.MainBuilding, threshold + 1);

        match.Run(3000);

        Assert.Contains(match.State.Reports, r => r.AttackerId == Ai && r.DefenderVillageId == Enemy && r.CapitalProtected);
        Assert.Contains(match.State.Reports, r => r.AttackerId == Ai && r.DefenderVillageId == Enemy && r.CatapultDamage?.Building == BuildingType.MainBuilding);
        Assert.True(capital.BuildingLevel(BuildingType.MainBuilding) <= threshold);
        Assert.Contains(match.State.Reports, r => r.AttackerId == Ai && r.DefenderVillageId == Enemy && r.Loyalty is not null);
    }

    [Fact]
    public void ALordIsNotSentAtAKnownProtectedCapitalWithoutCatapults()
    {
        var match = Match();
        PastDelay(match);
        NoCamps(match);
        var village = match.State.Villages[Ai];
        village.AddTroops(UnitType.Lord, 2);
        village.AddTroops(UnitType.Swordsman, 300);
        match.State.Villages[Enemy].SetBuildingLevel(BuildingType.MainBuilding, 8);

        match.Run(1200);

        var protectedHits = match.State.Reports.Where(r => r.AttackerId == Ai && r.CapitalProtected).ToList();
        Assert.Single(protectedHits);
    }

    [Fact]
    public void AVillageUnderAttackKeepsItsArmyHome()
    {
        var match = Match();
        PastDelay(match);
        var village = match.State.Villages[Ai];
        village.AddTroops(UnitType.Swordsman, 300);
        match.State.Villages[Enemy].AddTroops(UnitType.Catapult, 1);
        Assert.True(match.Session.SendAs(Enemy, new Valkaris.Core.Commands.SendTroopsCommand(Enemy, Ai, MovementType.Attack, new Dictionary<UnitType, int> { [UnitType.Catapult] = 1 })).Success);

        match.Run(16, step: 1);

        Assert.Null(FirstAttack(match));
    }
}
