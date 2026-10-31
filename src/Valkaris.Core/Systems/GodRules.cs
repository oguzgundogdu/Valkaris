using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>
/// The dev-only testing modifier, as the rule classes see it. Only LocalGameSession can turn it on, for its local player.
/// In release builds nothing can turn it on, so it never applies.
/// </summary>
public static class GodRules
{
    public static bool Applies(GameState state, int playerId)
    {
#if VALKARIS_DEV
        return state.GodPlayerId == playerId;
#else
        return false;
#endif
    }
}
