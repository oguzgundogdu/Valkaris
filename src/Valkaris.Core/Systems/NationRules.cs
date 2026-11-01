using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>Whose bonuses apply. Barbarian and abandoned villages have no nation, so theirs change nothing.</summary>
public static class NationRules
{
    public static NationConfig Of(GameData data, GameState state, int ownerId) =>
        state.Players.TryGetValue(ownerId, out var player) ? data.Nations.Nations[player.Nation] : NationConfig.Neutral;
}
