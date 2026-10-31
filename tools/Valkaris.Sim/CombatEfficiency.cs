using System.Text;
using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Sim;

/// <summary>What each unit and Wall level gives per resource, and who wins when both sides spend the same.</summary>
public static class CombatEfficiency
{
    public static readonly UnitType[] Fighters = { UnitType.Spearman, UnitType.Swordsman, UnitType.Cavalry };

    public static double CostOf(GameData data, UnitType unit)
    {
        var cost = data.Units.Units[unit].Cost;
        return cost.Wood + cost.Stone + cost.Food;
    }

    public static double WallCost(GameData data, int level) =>
        Enumerable.Range(1, level).Sum(l =>
        {
            var cost = BuildingRules.UpgradeCost(data, BuildingType.Wall, l);
            return cost.Wood + cost.Stone + cost.Food;
        });

    /// <summary>Troops bought with shares of a budget.</summary>
    public static Dictionary<UnitType, int> Buy(GameData data, double budget, params (UnitType Unit, double Share)[] mix) =>
        mix.ToDictionary(m => m.Unit, m => (int)Math.Floor(budget * m.Share / CostOf(data, m.Unit)));

    /// <summary>Defence over attack in a real battle. Above 1, the defender wins.</summary>
    public static double DefenceOverAttack(GameData data, IReadOnlyDictionary<UnitType, int> attackers, IReadOnlyDictionary<UnitType, int> defenders, int wallLevel)
    {
        var result = CombatResolver.Resolve(data, MovementType.Attack, attackers, defenders, wallLevel);
        return result.DefencePower / result.AttackPower;
    }

    public static string Table(GameData data)
    {
        var text = new StringBuilder();
        text.AppendLine("Per 100 resources");
        text.AppendLine($"  {"Unit",-12} {"Attack",8} {"Def inf",8} {"Def cav",8}");
        foreach (var unit in Fighters)
        {
            var config = data.Units.Units[unit];
            var per = 100 / CostOf(data, unit);
            text.AppendLine($"  {unit,-12} {config.Attack * per,8:0.0} {config.DefenceVsInfantry * per,8:0.0} {config.DefenceVsCavalry * per,8:0.0}");
        }

        const double Budget = 12000;
        (string Name, (UnitType, double)[] Mix)[] attackers =
        {
            ("Swordsmen", new[] { (UnitType.Swordsman, 1.0) }),
            ("Cavalry", new[] { (UnitType.Cavalry, 1.0) }),
            ("2:1 mix", new[] { (UnitType.Swordsman, 2.0 / 3), (UnitType.Cavalry, 1.0 / 3) }),
        };
        (string Name, (UnitType, double)[] Mix)[] defenders =
        {
            ("Spearmen", new[] { (UnitType.Spearman, 1.0) }),
            ("Half and half", new[] { (UnitType.Spearman, 0.5), (UnitType.Swordsman, 0.5) }),
            ("Swordsmen", new[] { (UnitType.Swordsman, 1.0) }),
        };

        text.AppendLine($"Defence over attack at equal spending of {Budget:0}, with Wall costs paid from the defender's budget");
        foreach (var wall in new[] { 0, 3, 5 })
        {
            foreach (var (defenderName, defenderMix) in defenders)
            {
                var defending = Buy(data, Budget - WallCost(data, wall), defenderMix);
                var cells = attackers.Select(a => $"{a.Name} {DefenceOverAttack(data, Buy(data, Budget, a.Mix), defending, wall):0.00}");
                text.AppendLine($"  {defenderName,-14} Wall {wall}: {string.Join("  ", cells)}");
            }
        }
        return text.ToString();
    }
}
