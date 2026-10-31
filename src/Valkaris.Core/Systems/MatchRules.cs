using Valkaris.Core.Data;
using Valkaris.Core.Events;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>Taking a player out of the match, and ending it. Shared by the simulation and by commands.</summary>
public static class MatchRules
{
    /// <summary>Their movements go too, so a player who is out can never regain a village from an attack still travelling.</summary>
    public static void TakeOut(GameState state, GameData data, Player player, double now, GameEvent reason, List<GameEvent> events)
    {
        player.IsEliminated = true;
        player.CapitalVillageId = null;
        player.CapitalChoiceDeadline = null;
        foreach (var movement in state.Movements.Where(m => m.OwnerId == player.Id).ToList())
            state.RemoveMovement(movement);

        events.Add(reason);
        if (state.Players.Values.Count(p => !p.IsEliminated) == 1)
            EndMatch(state, data, MatchEndReason.LastPlayerStanding, now, events);
    }

    /// <summary>Everything the player had stays where it is as abandoned barbarian villages.</summary>
    public static void Surrender(GameState state, GameData data, Player player, List<GameEvent> events)
    {
        foreach (var village in state.Villages.Values.Where(v => v.OwnerId == player.Id))
        {
            village.OwnerId = Village.BarbarianOwnerId;
            village.BarbarianTier = null;
            village.NextTroopRegenTime = null;
            village.Loyalty = Village.MaxLoyalty;
            village.ClearQueues();
        }

        player.HasSurrendered = true;
        var now = state.ElapsedTime;
        TakeOut(state, data, player, now, new PlayerSurrendered(now, player.Id), events);
    }

    public static void EndMatch(GameState state, GameData data, MatchEndReason reason, double now, List<GameEvent> events)
    {
        var scores = ScoreRules.Ranked(state, data);
        var alive = scores.Where(s => !state.Players[s.PlayerId].IsEliminated).ToList();
        var best = alive.Max(s => s.Score);
        var winners = alive.Where(s => s.Score == best).Select(s => s.PlayerId).OrderBy(id => id).ToList();

        state.Outcome = new MatchOutcome(reason, now, winners, scores);
        events.Add(new MatchEnded(now));
    }
}
