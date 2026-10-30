using Valkaris.Core.Model;

namespace Valkaris.Sim;

/// <summary>What one seat did in one match.</summary>
public sealed record SeatResult(
    int PlayerId,
    AiDifficulty Difficulty,
    bool Won,
    bool Eliminated,
    int Villages,
    double Score,
    double? FirstBarracks,
    double? FirstSettler,
    double? FirstLord,
    double? FirstFounded,
    double? FirstCapture,
    int AttacksSent,
    int AttacksReceived,
    IReadOnlyDictionary<int, int> AttacksAtPlayers);

/// <summary>Everything recorded about one match. Only numbers from the match, so the same seed always gives the same record.</summary>
public sealed record MatchResult(
    ulong Seed,
    MatchEndReason Reason,
    double LengthSeconds,
    bool IsDraw,
    IReadOnlyList<SeatResult> Seats,
    double ResourceSamples,
    double SamplesAtCap,
    double ProductionLost,
    double ProductionTotal,
    int Battles,
    double LootFromBarbarians,
    double LootFromPlayers,
    int VillagesFounded,
    int BarbarianCaptures,
    int PlayerCaptures,
    int CapitalCaptures,
    int Eliminations,
    int Transfers,
    double ResourcesMoved,
    double ResourcesLostInTransfer,
    int Commands,
    IReadOnlyDictionary<string, int> Rejections);
