using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>Loyalty formulas, driven by data.</summary>
public static class LoyaltyRules
{
    /// <summary>At game speed 1.</summary>
    public static double RegenPerSecond(GameData data, Village village) =>
        data.Villages.Loyalty.RegenPerSecond
        + data.Villages.Loyalty.RegenPerSecondPerMainLevel * village.BuildingLevel(BuildingType.MainBuilding);

    public static bool IsCapital(GameState state, Village village) =>
        !village.IsBarbarian && state.Players[village.OwnerId].CapitalVillageId == village.Id;

    /// <summary>A capital's loyalty cannot fall while its Main Building is above the threshold.</summary>
    public static bool IsLocked(GameState state, GameData data, Village village) =>
        IsCapital(state, village)
        && village.BuildingLevel(BuildingType.MainBuilding) > data.Villages.Capital.LockedAboveMainLevel;
}
