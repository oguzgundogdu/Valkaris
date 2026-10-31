using Valkaris.Core.Model;

namespace Valkaris.Core.Snapshots;

// Wire format for state sync. Immutable model records (GameSettings, Resources, TileCoord,
// queue items, Movement, BattleReport, ScoutReport) are used directly; model classes each have a snapshot here.

public sealed record GameStateSnapshot(
    GameSettings Settings,
    double ElapsedTime,
    IReadOnlyList<PlayerSnapshot> Players,
    IReadOnlyList<VillageSnapshot> Villages,
    GameMapSnapshot Map,
    IReadOnlyList<Movement> Movements,
    IReadOnlyList<BattleReport> Reports,
    IReadOnlyList<ScoutReport> ScoutReports,
    IReadOnlyList<ScoutNotice> ScoutNotices,
    IReadOnlyList<BattleArrival> BattleArrivals,
    int NextMovementId,
    int NextReportId,
    ulong RngState,
    MatchOutcome? Outcome);

public sealed record PlayerSnapshot(
    int Id,
    string Name,
    Nation Nation,
    bool IsAi,
    bool IsEliminated,
    bool HasSurrendered,
    int? CapitalVillageId,
    double? CapitalChoiceDeadline,
    IReadOnlyList<int> CapturedVillageIds,
    AiDifficulty? Difficulty);

public sealed record VillageSnapshot(
    int Id,
    int OwnerId,
    TileCoord Position,
    Resources Resources,
    IReadOnlyDictionary<BuildingType, int> Buildings,
    IReadOnlyList<BuildQueueItem> BuildQueue,
    IReadOnlyDictionary<UnitType, int> Troops,
    IReadOnlyList<TrainingQueueItem> TrainingQueue,
    string Name,
    int? BarbarianTier,
    double? NextTroopRegenTime,
    double Loyalty);

public sealed record GameMapSnapshot(int Width, int Height, IReadOnlyList<MapTileSnapshot> Tiles);

public sealed record MapTileSnapshot(int? VillageId, Terrain Terrain);
