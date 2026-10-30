using Valkaris.Core.Model;

namespace Valkaris.Core.Snapshots;

// Wire format for state sync. Immutable model records (GameSettings, Resources, TileCoord,
// BuildQueueItem) are used directly; model classes each have a snapshot here.

public sealed record GameStateSnapshot(
    GameSettings Settings,
    double ElapsedTime,
    IReadOnlyList<PlayerSnapshot> Players,
    IReadOnlyList<VillageSnapshot> Villages,
    GameMapSnapshot Map);

public sealed record PlayerSnapshot(
    int Id,
    string Name,
    Nation Nation,
    bool IsAi,
    bool IsEliminated,
    int CapitalVillageId);

public sealed record VillageSnapshot(
    int Id,
    int OwnerId,
    TileCoord Position,
    Resources Resources,
    IReadOnlyDictionary<BuildingType, int> Buildings,
    IReadOnlyList<BuildQueueItem> BuildQueue);

public sealed record GameMapSnapshot(int Width, int Height, IReadOnlyList<MapTileSnapshot> Tiles);

public sealed record MapTileSnapshot(int? VillageId);
