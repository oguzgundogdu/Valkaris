using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>Replacing a captured capital. The player's choice and the automatic pick use the same rules.</summary>
public static class CapitalRules
{
    /// <summary>The village picked for a player who did not choose: the highest Main Building, then the lowest id.</summary>
    public static Village AutomaticChoice(GameState state, int playerId) =>
        state.Villages.Values
            .Where(v => v.OwnerId == playerId)
            .OrderByDescending(v => v.BuildingLevel(BuildingType.MainBuilding))
            .ThenBy(v => v.Id)
            .First();

    /// <summary>The penalty lowers queued upgrades the same way catapult damage does.</summary>
    internal static void Assign(GameData data, Player player, Village village)
    {
        var from = village.BuildingLevel(BuildingType.MainBuilding);
        var to = Math.Min(from, data.Villages.Capital.PenaltyMainLevel);
        village.SetBuildingLevel(BuildingType.MainBuilding, to);
        village.LowerQueuedBuildTargets(BuildingType.MainBuilding, from - to);

        player.CapitalVillageId = village.Id;
        player.CapitalChoiceDeadline = null;
    }
}
