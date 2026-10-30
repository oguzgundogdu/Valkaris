using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class CombatResolverTests
{
    private static readonly GameData Data = TestData.Shipped;

    private static Dictionary<UnitType, int> Force(params (UnitType Unit, int Count)[] troops) =>
        troops.ToDictionary(t => t.Unit, t => t.Count);

    private static CombatResult Attack(Dictionary<UnitType, int> attackers, Dictionary<UnitType, int> defenders, int wall = 0, GameData? data = null) =>
        CombatResolver.Resolve(data ?? Data, MovementType.Attack, attackers, defenders, wall);

    private static CombatResult Raid(Dictionary<UnitType, int> attackers, Dictionary<UnitType, int> defenders) =>
        CombatResolver.Resolve(Data, MovementType.Raid, attackers, defenders, 0);

    private static readonly Dictionary<UnitType, int> HundredSpearmen = Force((UnitType.Spearman, 100));
    private static readonly Dictionary<UnitType, int> HundredSwordsmen = Force((UnitType.Swordsman, 100));

    // Rock-paper-scissors: the attacker's mix, not just its total, decides the result.

    [Fact]
    public void SameAttackAgainstSpearmenInfantryWinsAndCavalryLoses()
    {
        var infantry = Attack(Force((UnitType.Swordsman, 100)), HundredSpearmen);
        var cavalry = Attack(Force((UnitType.Cavalry, 75)), HundredSpearmen);

        Assert.Equal(3000, infantry.AttackPower);
        Assert.Equal(3000, cavalry.AttackPower);
        Assert.Equal(2000, infantry.DefencePower);
        Assert.Equal(4000, cavalry.DefencePower);
        Assert.True(infantry.AttackerWon);
        Assert.False(cavalry.AttackerWon);
    }

    [Fact]
    public void SameAttackAgainstSwordsmenInfantryLosesAndCavalryWins()
    {
        var infantry = Attack(Force((UnitType.Swordsman, 40)), HundredSwordsmen);
        var cavalry = Attack(Force((UnitType.Cavalry, 30)), HundredSwordsmen);

        Assert.Equal(1200, infantry.AttackPower);
        Assert.Equal(1200, cavalry.AttackPower);
        Assert.Equal(1500, infantry.DefencePower);
        Assert.Equal(1000, cavalry.DefencePower);
        Assert.False(infantry.AttackerWon);
        Assert.True(cavalry.AttackerWon);
    }

    [Fact]
    public void MixedAttackFacesDefenceWeightedByItsComposition()
    {
        // Infantry 900, cavalry 1200, against Dinf 2000 and Dcav 4000.
        var result = Attack(Force((UnitType.Swordsman, 30), (UnitType.Cavalry, 30)), HundredSpearmen);

        Assert.Equal(2100, result.AttackPower);
        Assert.Equal(2000 * 900.0 / 2100 + 4000 * 1200.0 / 2100, result.DefencePower, precision: 8);
    }

    [Fact]
    public void ChangingOnlyTheMixChangesTheOutcomeAgainstMixedDefenders()
    {
        // Same total attack (2400) against the same garrison: Dinf 1750, Dcav 2500.
        var defenders = Force((UnitType.Spearman, 50), (UnitType.Swordsman, 50));

        Assert.True(Attack(Force((UnitType.Swordsman, 80)), defenders).AttackerWon);
        Assert.False(Attack(Force((UnitType.Cavalry, 60)), defenders).AttackerWon);
    }

    [Fact]
    public void CatapultsCountAsInfantry()
    {
        var result = Attack(Force((UnitType.Catapult, 10)), HundredSpearmen);

        Assert.Equal(2000, result.DefencePower);
    }

    // The shipped data keeps the roles the rock-paper-scissors depends on.

    [Fact]
    public void SpearmanIsTheStrongestDefenderAgainstCavalry()
    {
        var units = Data.Units.Units;
        Assert.All(units.Where(u => u.Key != UnitType.Spearman), u => Assert.True(units[UnitType.Spearman].DefenceVsCavalry > u.Value.DefenceVsCavalry));
        Assert.True(units[UnitType.Spearman].DefenceVsCavalry > units[UnitType.Spearman].DefenceVsInfantry);
    }

    [Fact]
    public void SwordsmanIsTheBestInfantryAttacker()
    {
        var units = Data.Units.Units;
        var otherInfantry = units.Where(u => u.Value.Class == UnitClass.Infantry && u.Key != UnitType.Swordsman);
        Assert.All(otherInfantry, u => Assert.True(units[UnitType.Swordsman].Attack > u.Value.Attack));
    }

    [Fact]
    public void CavalryIsTheFastestUnitAndWeakDefendersAgainstItAreInfantryLight()
    {
        var units = Data.Units.Units;
        Assert.All(units.Where(u => u.Key != UnitType.Cavalry), u => Assert.True(units[UnitType.Cavalry].Speed > u.Value.Speed));
        Assert.True(units[UnitType.Swordsman].DefenceVsCavalry < units[UnitType.Swordsman].DefenceVsInfantry);
    }

    // Casualties.

    [Fact]
    public void AttackWinnerLosesXAndLoserLosesEverything()
    {
        // x = (2000 / 3000) ^ 1.5 = 0.5443
        var result = Attack(Force((UnitType.Swordsman, 100)), HundredSpearmen);

        Assert.Equal(54, result.AttackerLost[UnitType.Swordsman]);
        Assert.Equal(100, result.DefenderLost[UnitType.Spearman]);
    }

    [Fact]
    public void AttackLoserLosesEverythingAndDefenderLosesX()
    {
        // x = (3000 / 4000) ^ 1.5 = 0.6495
        var result = Attack(Force((UnitType.Cavalry, 75)), HundredSpearmen);

        Assert.Equal(75, result.AttackerLost[UnitType.Cavalry]);
        Assert.Equal(65, result.DefenderLost[UnitType.Spearman]);
    }

    [Fact]
    public void RaidLossesFollowTheRaidFormula()
    {
        // x = 0.5443: winner x / (1 + x) = 0.3525, loser 1 / (1 + x) = 0.6475
        var result = Raid(Force((UnitType.Swordsman, 100)), HundredSpearmen);

        Assert.True(result.AttackerWon);
        Assert.Equal(35, result.AttackerLost[UnitType.Swordsman]);
        Assert.Equal(65, result.DefenderLost[UnitType.Spearman]);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(50, 100)]
    [InlineData(200, 100)]
    public void RaidCostsBothSidesLessThanAttack(int swordsmen, int spearmen)
    {
        var attackers = Force((UnitType.Swordsman, swordsmen));
        var defenders = Force((UnitType.Spearman, spearmen));

        var attack = Attack(attackers, defenders);
        var raid = Raid(attackers, defenders);

        Assert.True(raid.AttackerLost[UnitType.Swordsman] <= attack.AttackerLost[UnitType.Swordsman]);
        Assert.True(raid.DefenderLost[UnitType.Spearman] <= attack.DefenderLost[UnitType.Spearman]);
    }

    [Fact]
    public void TieGoesToTheDefender()
    {
        var result = Attack(Force((UnitType.Swordsman, 100)), Force((UnitType.Spearman, 150)));

        Assert.Equal(result.AttackPower, result.DefencePower);
        Assert.False(result.AttackerWon);
    }

    [Fact]
    public void NoDefendersMeansNoAttackerLosses()
    {
        var result = Attack(Force((UnitType.Spearman, 1)), new Dictionary<UnitType, int>());

        Assert.True(result.AttackerWon);
        Assert.Equal(0, result.DefencePower);
        Assert.Equal(0, result.AttackerLost[UnitType.Spearman]);
    }

    [Fact]
    public void WallRaisesDefence()
    {
        // 2000 * (1 + 0.05 * 10) = 3000, a tie that goes to the defender.
        var result = Attack(Force((UnitType.Swordsman, 100)), HundredSpearmen, wall: 10);

        Assert.Equal(3000, result.DefencePower, precision: 8);
        Assert.False(result.AttackerWon);
    }

    [Fact]
    public void WallDoesNothingWithoutDefenders()
    {
        var result = Attack(Force((UnitType.Spearman, 1)), new Dictionary<UnitType, int>(), wall: 10);

        Assert.True(result.AttackerWon);
    }

    [Fact]
    public void NoAttackersIsAnError() =>
        Assert.Throws<ArgumentException>(() => Attack(new Dictionary<UnitType, int>(), HundredSpearmen));

    [Fact]
    public void ValuesComeFromData()
    {
        var altered = TestData.Mutated(GameData.CombatFile, root =>
        {
            root["wallBonusPerLevel"] = 1.0;
            root["casualtyExponent"] = 1.0;
        });

        var result = Attack(Force((UnitType.Swordsman, 100)), HundredSpearmen, wall: 0, data: altered);
        var walled = Attack(Force((UnitType.Swordsman, 100)), HundredSpearmen, wall: 1, data: altered);

        Assert.Equal(67, result.AttackerLost[UnitType.Swordsman]);
        Assert.Equal(4000, walled.DefencePower);
    }
}
