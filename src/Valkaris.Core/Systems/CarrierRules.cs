using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>Carrier formulas, driven by data. Carriers are not units: a Marketplace's level sets how many exist.</summary>
public static class CarrierRules
{
    public static int Total(GameData data, Village village) =>
        data.Buildings.CarriersByMarketplaceLevel[village.BuildingLevel(BuildingType.Marketplace)];

    /// <summary>Worked out from the movements, so it cannot drift. Never below 0, even after the Marketplace loses levels.</summary>
    public static int Free(GameState state, GameData data, Village village)
    {
        var busy = state.Movements
            .Where(m => m.OwnerId == village.OwnerId)
            .Where(m => (m.Type == MovementType.Transfer && m.FromVillageId == village.Id)
                || (m.Type == MovementType.CarrierReturn && m.ToVillageId == village.Id))
            .Sum(m => m.Carriers);
        return Math.Max(0, Total(data, village) - busy);
    }

    public static int Needed(GameData data, int amount) =>
        (int)Math.Ceiling((double)amount / data.Villages.Carriers.Capacity);

    /// <summary>Scaled by game speed.</summary>
    public static double TravelSeconds(GameData data, GameState state, TileCoord from, TileCoord to) =>
        UnitRules.Distance(from, to) / data.Villages.Carriers.Speed / state.Settings.SpeedMultiplier;
}
