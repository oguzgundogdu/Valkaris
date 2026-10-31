using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Snapshots;

/// <summary>Everything one player may know at one moment. A client never receives more than this.</summary>
public sealed record PlayerViewSnapshot(
    GameStateSnapshot State,
    IReadOnlyList<SightedMovement> Sightings,
    IReadOnlyList<PublicNotice> Notices,
    IReadOnlyList<Clash> Clashes);

/// <summary>Cuts the full state down to what a player may know. See Visibility for the rules on third parties.</summary>
public static class PlayerView
{
    /// <summary>Long enough to outlast any effect on screen and a late packet.</summary>
    public const double ClashMemorySeconds = 10;

    public static PlayerViewSnapshot Of(GameState state, GameData data, int playerId)
    {
        var full = SnapshotMapper.ToSnapshot(state);
        var filtered = full with
        {
            Settings = state.Settings with { Seed = HashSeed(state.Settings.Seed) },
            Players = full.Players.Select(p => Filter(p, playerId)).ToList(),
            Villages = full.Villages.Select(v => v.OwnerId == playerId ? v : Public(v)).ToList(),
            Movements = state.Movements.Select(m => Filter(state, m, playerId)).OfType<Movement>().ToList(),
            Reports = full.Reports.Where(r => r.AttackerId == playerId || r.DefenderId == playerId).ToList(),
            ScoutReports = full.ScoutReports.Where(r => r.SenderId == playerId).ToList(),
            ScoutNotices = full.ScoutNotices.Where(n => n.DefenderId == playerId).ToList(),
            BattleArrivals = Array.Empty<BattleArrival>(),
            NextMovementId = 0,
            NextReportId = 0,
            RngState = 0,
        };

        return new PlayerViewSnapshot(
            filtered,
            Visibility.SightedMovements(state, data, playerId),
            Visibility.PublicNotices(state, playerId).ToList(),
            Visibility.Clashes(state, data, playerId, state.ElapsedTime - ClashMemorySeconds));
    }

    /// <summary>Paints every player's map the same, without giving away the seed that drives the match's random numbers.</summary>
    public static ulong HashSeed(ulong seed)
    {
        var z = seed + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static PlayerSnapshot Filter(PlayerSnapshot player, int playerId) =>
        player.Id == playerId
            ? player with { Difficulty = null }
            : player with { Difficulty = null, CapitalChoiceDeadline = null, CapturedVillageIds = Array.Empty<int>() };

    private static VillageSnapshot Public(VillageSnapshot village) => village with
    {
        Resources = Resources.Zero,
        Buildings = new Dictionary<BuildingType, int>(),
        BuildQueue = Array.Empty<BuildQueueItem>(),
        Troops = new Dictionary<UnitType, int>(),
        TrainingQueue = Array.Empty<TrainingQueueItem>(),
        NextTroopRegenTime = null,
        Loyalty = Village.MaxLoyalty,
    };

    private static Movement? Filter(GameState state, Movement movement, int playerId)
    {
        if (movement.OwnerId == playerId)
            return movement;

        return Visibility.IsIncoming(state, playerId, movement)
            ? movement with { Troops = new Dictionary<UnitType, int>(), Loot = Resources.Zero, CatapultTarget = null, Carriers = 0 }
            : null;
    }
}
