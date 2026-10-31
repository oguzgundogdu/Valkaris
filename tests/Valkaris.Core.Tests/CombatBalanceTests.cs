using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Sim;

namespace Valkaris.Core.Tests;

/// <summary>The balance rules from the design, checked against the shipped data through the real combat resolver.</summary>
public class CombatBalanceTests
{
    private const double ClearlyMore = 1.25;

    private static readonly GameData Data = TestData.Shipped;

    private static readonly (UnitType, double)[] Swords = { (UnitType.Swordsman, 1.0) };
    private static readonly (UnitType, double)[] Cavalry = { (UnitType.Cavalry, 1.0) };
    private static readonly (UnitType, double)[] TwoToOne = { (UnitType.Swordsman, 2.0 / 3), (UnitType.Cavalry, 1.0 / 3) };
    private static readonly (UnitType, double)[] Spears = { (UnitType.Spearman, 1.0) };
    private static readonly (UnitType, double)[] Mixed = { (UnitType.Spearman, 0.5), (UnitType.Swordsman, 0.5) };

    private static double Ratio(double attackBudget, (UnitType, double)[] attackMix, double defenceBudget, (UnitType, double)[] defenceMix, int wall) =>
        CombatEfficiency.DefenceOverAttack(
            Data,
            CombatEfficiency.Buy(Data, attackBudget, attackMix),
            CombatEfficiency.Buy(Data, defenceBudget - CombatEfficiency.WallCost(Data, wall), defenceMix),
            wall);

    private static double PerResource(UnitType unit, Func<UnitDefinition, double> stat) =>
        stat(Data.Units.Units[unit]) / CombatEfficiency.CostOf(Data, unit);

    public static IEnumerable<object[]> Budgets()
    {
        foreach (var budget in new[] { 6000.0, 12000, 24000 })
        {
            foreach (var wall in new[] { 0, 1, 2, 3, 4, 5 })
                yield return new object[] { budget, wall };
        }
    }

    [Theory]
    [MemberData(nameof(Budgets))]
    public void ClearlyMoreResourcesBeatEveryDefenceButTheIntendedCounter(double budget, int wall)
    {
        foreach (var attack in new[] { Swords, Cavalry, TwoToOne })
        {
            foreach (var defence in new[] { Spears, Mixed, Swords })
            {
                if (attack == Cavalry && defence == Spears)
                    continue;

                var ratio = Ratio(budget * ClearlyMore, attack, budget, defence, wall);
                Assert.True(ratio < 1, $"{attack[0].Item1} vs {defence[0].Item1}, wall {wall}, budget {budget}: {ratio:0.00}");
            }
        }
    }

    [Theory]
    [InlineData(6000)]
    [InlineData(12000)]
    [InlineData(24000)]
    public void AtEqualSpendingAMixedDefenderWinsOnlySlightly(double budget)
    {
        foreach (var attack in new[] { Swords, Cavalry, TwoToOne })
            Assert.InRange(Ratio(budget, attack, budget, Mixed, 0), 1.0, 1.1);
    }

    [Fact]
    public void CavalryRunningIntoSpearsIsStillACostlyMistake() =>
        Assert.True(Ratio(12000 * ClearlyMore, Cavalry, 12000, Spears, 0) > 1);

    [Fact]
    public void SpearmenAreTheBestAntiCavalryDefender()
    {
        var best = CombatEfficiency.Fighters.MaxBy(u => PerResource(u, c => c.DefenceVsCavalry));
        Assert.Equal(UnitType.Spearman, best);
        Assert.True(Data.Units.Units[UnitType.Spearman].DefenceVsCavalry > Data.Units.Units[UnitType.Swordsman].DefenceVsCavalry);
    }

    [Fact]
    public void SpearmenAreNotTheBestDefenderAgainstEverything() =>
        Assert.NotEqual(UnitType.Spearman, CombatEfficiency.Fighters.MaxBy(u => PerResource(u, c => c.DefenceVsInfantry)));

    [Fact]
    public void TheTableShowsEveryUnit()
    {
        var table = CombatEfficiency.Table(Data);

        foreach (var unit in CombatEfficiency.Fighters)
            Assert.Contains(unit.ToString(), table);
        Assert.Contains("Wall 5", table);
    }
}
