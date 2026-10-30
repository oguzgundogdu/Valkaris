using Valkaris.Core.Data;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Commands;

/// <summary>Validates and applies commands. A rejected command leaves the state untouched.</summary>
public sealed class CommandHandler
{
    private readonly GameState _state;
    private readonly GameData _data;

    public CommandHandler(GameState state, GameData data)
    {
        _state = state;
        _data = data;
    }

    public CommandResult Handle(int issuerId, ICommand command)
    {
        if (_state.IsMatchOver)
            return CommandResult.Rejected(CommandRejection.MatchOver);

        return command switch
        {
            BuildCommand build => HandleBuild(issuerId, build),
            _ => CommandResult.Rejected(CommandRejection.UnknownCommand),
        };
    }

    private CommandResult HandleBuild(int issuerId, BuildCommand command)
    {
        var option = BuildPreview.Evaluate(_state, _data, issuerId, command.VillageId, command.Building);
        if (option.Rejection is { } rejection)
            return CommandResult.Rejected(rejection);

        var village = _state.Villages[command.VillageId];
        village.Resources -= option.Cost;
        village.EnqueueBuild(new BuildQueueItem(
            command.Building,
            option.TargetLevel,
            option.StartTime,
            option.StartTime + option.DurationSeconds));

        return CommandResult.Ok;
    }
}
