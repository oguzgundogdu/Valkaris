namespace Valkaris.Core.Model;

public readonly record struct TileCoord(int X, int Y);

public readonly record struct Resources(double Wood, double Stone, double Food)
{
    public static Resources Zero => default;

    public double Get(ResourceType type) => type switch
    {
        ResourceType.Wood => Wood,
        ResourceType.Stone => Stone,
        ResourceType.Food => Food,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static Resources Of(ResourceType type, double amount) => type switch
    {
        ResourceType.Wood => new Resources(amount, 0, 0),
        ResourceType.Stone => new Resources(0, amount, 0),
        ResourceType.Food => new Resources(0, 0, amount),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public bool Covers(Resources cost) =>
        Wood >= cost.Wood && Stone >= cost.Stone && Food >= cost.Food;

    public Resources CappedAt(double cap) =>
        new(Math.Min(Wood, cap), Math.Min(Stone, cap), Math.Min(Food, cap));

    public static Resources operator +(Resources a, Resources b) =>
        new(a.Wood + b.Wood, a.Stone + b.Stone, a.Food + b.Food);

    public static Resources operator -(Resources a, Resources b) =>
        new(a.Wood - b.Wood, a.Stone - b.Stone, a.Food - b.Food);

    public static Resources operator *(Resources a, double factor) =>
        new(a.Wood * factor, a.Stone * factor, a.Food * factor);
}

/// <param name="MatchDurationSeconds">Null for an Unlimited match, which ends only when one player remains.</param>
public sealed record GameSettings(double? MatchDurationSeconds, double SpeedMultiplier, MapSize MapSize, ulong Seed = 0);

public sealed record BuildQueueItem(BuildingType Building, int TargetLevel, double StartTime, double FinishTime);

public sealed record TrainingQueueItem(UnitType Unit, int Count, double StartTime, double FinishTime);

/// <summary>Troops or carriers on the way. Loot is what they hold: plunder, or a transfer's load.</summary>
public sealed record Movement(
    int Id,
    int OwnerId,
    int FromVillageId,
    int? ToVillageId,
    MovementType Type,
    IReadOnlyDictionary<UnitType, int> Troops,
    Resources Loot,
    double DepartureTime,
    double ArrivalTime,
    BuildingType? CatapultTarget,
    TileCoord? TargetTile = null,
    TileCoord? FromTile = null,
    int Carriers = 0);

public sealed record LoyaltyChange(double From, double To, bool Captured);

public sealed record CatapultDamage(BuildingType Building, int FromLevel, int ToLevel);

/// <summary>A resolved battle. Both sides see the same report: v1 has no fog of war.</summary>
public sealed record BattleReport(
    int Id,
    double Time,
    MovementType Type,
    int AttackerId,
    int DefenderId,
    int AttackerVillageId,
    int DefenderVillageId,
    IReadOnlyDictionary<UnitType, int> AttackerSent,
    IReadOnlyDictionary<UnitType, int> AttackerLost,
    IReadOnlyDictionary<UnitType, int> DefenderPresent,
    IReadOnlyDictionary<UnitType, int> DefenderLost,
    int WallLevel,
    double AttackPower,
    double DefencePower,
    bool AttackerWon,
    Resources Loot,
    CatapultDamage? CatapultDamage,
    LoyaltyChange? Loyalty = null,
    bool CapitalProtected = false);

/// <summary>
/// What the sender's Scouts found, known only to the sender. Garrison, wall and stock are set only on success.
/// What the defender may know is in a separate ScoutNotice, so neither side can read the other's.
/// </summary>
public sealed record ScoutReport(
    int Id,
    double Time,
    int SenderId,
    int DefenderId,
    int SenderVillageId,
    int TargetVillageId,
    IReadOnlyDictionary<UnitType, int> Sent,
    bool Succeeded,
    IReadOnlyDictionary<UnitType, int>? Garrison,
    int? WallLevel,
    Resources? Stock,
    int ScoutsLost = 0);

/// <summary>
/// What the defender learned, known only to the defender. Shares ids with reports.
/// The count sent is known only when the Scouts failed: a Watchtower sees a success happen, but not its size.
/// </summary>
public sealed record ScoutNotice(
    int Id,
    double Time,
    int SenderId,
    int DefenderId,
    int TargetVillageId,
    bool Caught,
    int? ScoutsSent,
    int ScoutsLost);

public sealed record PlayerScore(int PlayerId, int Villages, int Population, int Captures, double Score);

/// <summary>How the match ended. A draw has more than one winner.</summary>
public sealed record MatchOutcome(MatchEndReason Reason, double Time, IReadOnlyList<int> WinnerIds, IReadOnlyList<PlayerScore> Scores);
