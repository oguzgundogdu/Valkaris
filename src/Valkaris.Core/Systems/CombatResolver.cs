using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

public sealed record CombatResult(
    double AttackPower,
    double DefencePower,
    bool AttackerWon,
    IReadOnlyDictionary<UnitType, int> AttackerLost,
    IReadOnlyDictionary<UnitType, int> DefenderLost);

/// <summary>Resolves a battle with one calculation. Pure: no state and no randomness.</summary>
public static class CombatResolver
{
    public static CombatResult Resolve(
        GameData data,
        MovementType type,
        IReadOnlyDictionary<UnitType, int> attackers,
        IReadOnlyDictionary<UnitType, int> defenders,
        int wallLevel,
        double wallBonus = 1)
    {
        var infantryAttack = AttackOfClass(data, attackers, UnitClass.Infantry);
        var cavalryAttack = AttackOfClass(data, attackers, UnitClass.Cavalry);
        var attack = infantryAttack + cavalryAttack;
        if (attack <= 0)
            throw new ArgumentException("A battle needs attacking troops.", nameof(attackers));

        var defenceVsInfantry = defenders.Sum(t => t.Value * data.Units.Units[t.Key].DefenceVsInfantry);
        var defenceVsCavalry = defenders.Sum(t => t.Value * data.Units.Units[t.Key].DefenceVsCavalry);

        // Defence is weighted by what is actually attacking, so the attacker's mix matters.
        var weighted = (defenceVsInfantry * infantryAttack + defenceVsCavalry * cavalryAttack) / attack;
        var defence = weighted * (1 + data.Combat.WallBonusPerLevel * wallBonus * wallLevel);

        var attackerWon = attack > defence;
        var ratio = attackerWon ? defence / attack : attack / defence;
        var x = Math.Pow(ratio, data.Combat.CasualtyExponent);

        var (winnerLoss, loserLoss) = type == MovementType.Raid
            ? (x / (1 + x), 1 / (1 + x))
            : (x, 1.0);

        return new CombatResult(
            attack,
            defence,
            attackerWon,
            Losses(attackers, attackerWon ? winnerLoss : loserLoss),
            Losses(defenders, attackerWon ? loserLoss : winnerLoss));
    }

    private static double AttackOfClass(GameData data, IReadOnlyDictionary<UnitType, int> troops, UnitClass unitClass) =>
        troops.Where(t => data.Units.Units[t.Key].Class == unitClass)
            .Sum(t => t.Value * data.Units.Units[t.Key].Attack);

    private static Dictionary<UnitType, int> Losses(IReadOnlyDictionary<UnitType, int> troops, double share) =>
        troops.ToDictionary(t => t.Key, t => (int)Math.Round(t.Value * share, MidpointRounding.AwayFromZero));
}
