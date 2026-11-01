using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

public static class LootRules
{
    /// <summary>
    /// What winning survivors carry off. A share of warehouse capacity is protected per resource. If they can't
    /// carry everything else, each resource is taken in proportion to what was lootable.
    /// </summary>
    public static Resources Calculate(GameData data, GameState state, Village village, IReadOnlyDictionary<UnitType, int> survivors, int attackerId) =>
        Calculate(data, village, BuildingRules.VillageCapacity(data, state, village), survivors, NationRules.Of(data, state, attackerId));

    internal static Resources Calculate(GameData data, Village village, IReadOnlyDictionary<UnitType, int> survivors) =>
        Calculate(data, village, BuildingRules.VillageCapacity(data, village), survivors, NationConfig.Neutral);

    private static Resources Calculate(GameData data, Village village, double capacity, IReadOnlyDictionary<UnitType, int> survivors, NationConfig attacker)
    {
        var carry = survivors.Sum(t => t.Value * data.Units.Units[t.Key].LootCapacity) * attacker.LootCapacity;
        var protectedAmount = data.Combat.ProtectedResourceShare * capacity;

        var wood = Math.Max(0, village.Resources.Wood - protectedAmount);
        var stone = Math.Max(0, village.Resources.Stone - protectedAmount);
        var food = Math.Max(0, village.Resources.Food - protectedAmount);

        var total = wood + stone + food;
        if (total <= 0)
            return Resources.Zero;

        var share = Math.Min(1, carry / total);
        return new Resources(Whole.Down(wood * share), Whole.Down(stone * share), Whole.Down(food * share));
    }
}
