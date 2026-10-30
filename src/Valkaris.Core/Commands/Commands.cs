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
}

public sealed record CommandResult(CommandRejection? Rejection)
{
    public static readonly CommandResult Ok = new((CommandRejection?)null);

    public bool Success => Rejection is null;

    public static CommandResult Rejected(CommandRejection reason) => new(reason);
}
