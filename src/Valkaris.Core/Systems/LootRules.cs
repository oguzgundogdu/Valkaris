using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

public static class LootRules
{
    /// <summary>
    /// What winning survivors carry off. A share of warehouse capacity is protected per resource. If they can't
    /// carry everything else, each resource is taken in proportion to what was lootable.
    /// </summary>
    public static Resources Calculate(GameData data, Village village, IReadOnlyDictionary<UnitType, int> survivors)
    {
        var carry = survivors.Sum(t => t.Value * data.Units.Units[t.Key].LootCapacity);
        var protectedAmount = data.Combat.ProtectedResourceShare * BuildingRules.VillageCapacity(data, village);

        var wood = Math.Max(0, village.Resources.Wood - protectedAmount);
        var stone = Math.Max(0, village.Resources.Stone - protectedAmount);
        var food = Math.Max(0, village.Resources.Food - protectedAmount);

        var total = wood + stone + food;
        if (total <= 0)
            return Resources.Zero;

        var share = Math.Min(1, carry / total);
        return new Resources(Math.Floor(wood * share), Math.Floor(stone * share), Math.Floor(food * share));
    }
}
