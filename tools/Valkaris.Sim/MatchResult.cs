using Valkaris.Core.Model;

namespace Valkaris.Sim;

/// <summary>What a seat spent on defence and what an attacker would have met. Home defence is per village, and mid-match values are null if the match ended first.</summary>
public sealed record SeatDefence(
    double Spent,
    double? WallAtMid,
    double WallAtEnd,
    double? HomeDefenceAtMid,
    double HomeDefenceAtEnd,
    int AttacksWon);

/// <summary>Attacks on players, and how many were won.</summary>
public sealed record AttackRecord(int Attacks, int Won);

/// <summary>
/// A seat's scouting. Attacks with intel are those on players after a success still within the profile's age limit:
/// exact from a Clear or Overwhelming report, ranged from a Narrow one. Against players counts only missions at other players' villages.
/// </summary>
public sealed record SeatScouting(
    int Sent,
    int Succeeded,
    int TroopsLost,
    int AttacksWithIntel,
    int SentAtPlayers = 0,
    int SucceededAtPlayers = 0,
    int Narrow = 0,
    int Clear = 0,
    int Overwhelming = 0,
    int ScoutsTrained = 0,
    double WatchtowerAtEnd = 0,
    AttackRecord? WithExact = null,
    AttackRecord? WithRange = null,
    AttackRecord? WithNone = null);

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
    IReadOnlyDictionary<int, int> AttacksAtPlayers,
    SeatDefence? Defence = null,
    SeatScouting? Scouting = null,
    IReadOnlyDictionary<UnitType, double>? UnitSpending = null,
    Nation Nation = Nation.Valdren,
    double LootTaken = 0,
    double MeanAttackTravelSeconds = 0);

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
    IReadOnlyDictionary<string, int> Rejections,
    double AttackerResourcesLost = 0,
    double DefenderResourcesLost = 0,
    bool Unfinished = false,
    int Surrenders = 0);
