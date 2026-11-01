using Valkaris.Core.Data;
using Valkaris.Core.Events;
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
        var spectating = Visibility.Spectates(state, playerId);
        var filtered = full with
        {
            Settings = state.Settings with { Seed = HashSeed(state.Settings.Seed) },
            Players = full.Players.Select(p => Filter(p, playerId)).ToList(),
            Villages = full.Villages.Select(v => v.OwnerId == playerId ? v : Public(v)).ToList(),
            Movements = spectating ? full.Movements : state.Movements.Select(m => Filter(state, m, playerId)).OfType<Movement>().ToList(),
            Reports = spectating ? full.Reports : full.Reports.Where(r => r.AttackerId == playerId || r.DefenderId == playerId).ToList(),
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

    /// <summary>Would this player be told of this event? Anything naming a report goes only to those who hold that report.</summary>
    public static bool Hears(GameState state, GameEvent gameEvent, int playerId)
    {
        bool Owns(int villageId) => state.Villages[villageId].OwnerId == playerId;

        return gameEvent switch
        {
            BuildingCompleted e => Owns(e.VillageId),
            TroopsTrained e => Owns(e.VillageId),
            TroopsReturned e => Owns(e.VillageId),
            ReinforcementsArrived e => Owns(e.VillageId),
            ResourcesDelivered e => Owns(e.VillageId),
            CarriersReturned e => Owns(e.VillageId),
            VillageFounded e => Owns(e.VillageId),
            BattleResolved e => Visibility.Spectates(state, playerId) || state.Reports.Any(r => r.Id == e.ReportId && (r.AttackerId == playerId || r.DefenderId == playerId)),
            ScoutResolved e => state.ScoutReports.Any(r => r.Id == e.ReportId && r.SenderId == playerId),
            ScoutNoticed e => state.ScoutNotices.Any(n => n.Id == e.NoticeId && n.DefenderId == playerId),
            VillageCaptured e => e.PreviousOwnerId == playerId || e.NewOwnerId == playerId,
            CapitalChoiceRequired e => e.PlayerId == playerId,
            CapitalChanged e => e.PlayerId == playerId,
            SettlingFailed e => e.OwnerId == playerId,
            PlayerEliminated or PlayerSurrendered or PlayerDisconnected or MatchEnded => true,
            _ => false,
        };
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
