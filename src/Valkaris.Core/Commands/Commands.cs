using Valkaris.Core.Model;

namespace Valkaris.Core.Commands;

/// <summary>
/// A player's intent. The issuer is passed to CommandHandler separately rather than stored here,
/// so a remote client cannot claim to be another player.
/// </summary>
public interface ICommand
{
}

/// <summary>Upgrades a building by one level, or builds it if it is at level 0.</summary>
public sealed record BuildCommand(int VillageId, BuildingType Building) : ICommand;

/// <summary>Trains a batch of units. The whole batch arrives together when it finishes.</summary>
public sealed record TrainCommand(int VillageId, UnitType Unit, int Count) : ICommand;

/// <summary>Sends troops from a village. They leave at once and do not defend while away.</summary>
public sealed record SendTroopsCommand(
    int FromVillageId,
    int ToVillageId,
    MovementType Type,
    IReadOnlyDictionary<UnitType, int> Troops,
    BuildingType? CatapultTarget = null) : ICommand;

/// <summary>Sends one Settler to found a village on an empty tile.</summary>
public sealed record FoundVillageCommand(int FromVillageId, TileCoord Tile) : ICommand;

/// <summary>Picks a new capital after the old one was captured. Not valid at any other time.</summary>
public sealed record SetCapitalCommand(int VillageId) : ICommand;

/// <summary>The issuer leaves the match, and their villages are abandoned. See MatchRules.Surrender.</summary>
public sealed record SurrenderCommand : ICommand;

/// <summary>Sends resources by carrier to another of the player's villages.</summary>
public sealed record SendResourcesCommand(int FromVillageId, int ToVillageId, int Wood, int Stone, int Food) : ICommand;

/// <summary>Renames one of the issuer's own villages. See VillageNameRules.</summary>
public sealed record RenameVillageCommand(int VillageId, string Name) : ICommand;

public enum CommandRejection
{
    UnknownCommand,
    MatchOver,
    VillageNotFound,
    NotOwner,
    QueueFull,
    MaxLevelReached,
    MainBuildingLevelTooLow,
    InsufficientResources,
    GamePaused,
    InvalidCount,
    BarracksLevelTooLow,
    InvalidMission,
    NoTroopsSelected,
    NotEnoughTroops,
    CatapultsCannotRaid,
    SettlersCannotFight,
    ScoutsCannotFight,
    LordsCannotRaid,
    NoSettlerHere,
    TileOutsideMap,
    TileIsWater,
    TileTaken,
    TooCloseToVillage,
    VillageLimitReached,
    NoCapital,
    NoCapitalChoicePending,
    TargetNotYours,
    SameVillage,
    NoResourcesSelected,
    NoMarketplace,
    NotEnoughCarriers,
    OnlyScoutsCanScout,
    AlreadyOutOfTheMatch,
    VillageNameTooShort,
    VillageNameTooLong,
    VillageNameInvalid,
    VillageNameTaken,
}

public sealed record CommandResult(CommandRejection? Rejection)
{
    public static readonly CommandResult Ok = new((CommandRejection?)null);

    public bool Success => Rejection is null;

    public static CommandResult Rejected(CommandRejection reason) => new(reason);
}
