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
            TrainCommand train => HandleTrain(issuerId, train),
            SendTroopsCommand send => HandleSend(issuerId, send),
            FoundVillageCommand found => HandleFound(issuerId, found),
            SetCapitalCommand capital => HandleSetCapital(issuerId, capital),
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

    private CommandResult HandleTrain(int issuerId, TrainCommand command)
    {
        var option = TrainPreview.Evaluate(_state, _data, issuerId, command.VillageId, command.Unit, command.Count);
        if (option.Rejection is { } rejection)
            return CommandResult.Rejected(rejection);

        var village = _state.Villages[command.VillageId];
        village.Resources -= option.Cost;
        village.EnqueueTraining(new TrainingQueueItem(
            command.Unit,
            command.Count,
            option.StartTime,
            option.StartTime + option.DurationSeconds));

        return CommandResult.Ok;
    }

    private CommandResult HandleSend(int issuerId, SendTroopsCommand command)
    {
        var option = SendPreview.Evaluate(_state, _data, issuerId, command);
        if (option.Rejection is { } rejection)
            return CommandResult.Rejected(rejection);

        var from = _state.Villages[command.FromVillageId];
        var troops = command.Troops.Where(t => t.Value > 0).ToDictionary(t => t.Key, t => t.Value);
        foreach (var (unit, count) in troops)
            from.RemoveTroops(unit, count);

        _state.AddMovement(new Movement(
            _state.NextMovementId++,
            issuerId,
            command.FromVillageId,
            command.ToVillageId,
            command.Type,
            troops,
            Resources.Zero,
            _state.ElapsedTime,
            option.ArrivalTime,
            command.CatapultTarget));

        return CommandResult.Ok;
    }

    private CommandResult HandleSetCapital(int issuerId, SetCapitalCommand command)
    {
        var player = _state.Players[issuerId];
        if (player.CapitalChoiceDeadline is null)
            return CommandResult.Rejected(CommandRejection.NoCapitalChoicePending);
        if (!_state.Villages.TryGetValue(command.VillageId, out var village))
            return CommandResult.Rejected(CommandRejection.VillageNotFound);
        if (village.OwnerId != issuerId)
            return CommandResult.Rejected(CommandRejection.NotOwner);

        CapitalRules.Assign(_data, player, village);
        return CommandResult.Ok;
    }

    private CommandResult HandleFound(int issuerId, FoundVillageCommand command)
    {
        var option = FoundPreview.Evaluate(_state, _data, issuerId, command.FromVillageId, command.Tile);
        if (option.Rejection is { } rejection)
            return CommandResult.Rejected(rejection);

        _state.Villages[command.FromVillageId].RemoveTroops(UnitType.Settler, 1);
        _state.AddMovement(new Movement(
            _state.NextMovementId++,
            issuerId,
            command.FromVillageId,
            null,
            MovementType.Settle,
            FoundPreview.OneSettler,
            Resources.Zero,
            _state.ElapsedTime,
            option.ArrivalTime,
            null,
            command.Tile));

        return CommandResult.Ok;
    }
}
