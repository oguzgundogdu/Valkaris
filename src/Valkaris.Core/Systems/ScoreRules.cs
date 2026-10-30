using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>The score: villages, population and captured villages, with weights in data.</summary>
public static class ScoreRules
{
    public static int Population(GameData data, Village village) =>
        village.Buildings.Sum(b => b.Value * data.Scoring.PopulationPerLevel[b.Key]);

    public static PlayerScore For(GameState state, GameData data, Player player)
    {
        var villages = state.Villages.Values.Where(v => v.OwnerId == player.Id).ToList();
        var population = villages.Sum(v => Population(data, v));
        var captures = player.CapturedVillageIds.Count;
        var scoring = data.Scoring;

        return new PlayerScore(
            player.Id,
            villages.Count,
            population,
            captures,
            villages.Count * scoring.VillageWeight + population * scoring.PopulationWeight + captures * scoring.CaptureWeight);
    }

    /// <summary>Ranked for the results: surviving players first, then by score, then by id.</summary>
    public static IReadOnlyList<PlayerScore> Ranked(GameState state, GameData data) =>
        state.Players.Values
            .OrderBy(p => p.IsEliminated)
            .ThenByDescending(p => For(state, data, p).Score)
            .ThenBy(p => p.Id)
            .Select(p => For(state, data, p))
            .ToList();
}
