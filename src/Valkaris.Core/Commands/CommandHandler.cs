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
        if (!_state.Villages.TryGetValue(command.VillageId, out var village))
            return CommandResult.Rejected(CommandRejection.VillageNotFound);
        if (village.OwnerId != issuerId)
            return CommandResult.Rejected(CommandRejection.NotOwner);
        if (village.BuildQueue.Count >= _data.Game.MaxBuildQueueLength)
            return CommandResult.Rejected(CommandRejection.QueueFull);

        var queuedUpgrades = village.BuildQueue.Count(item => item.Building == command.Building);
        var targetLevel = village.BuildingLevel(command.Building) + queuedUpgrades + 1;

        if (targetLevel > _data.Buildings.Buildings[command.Building].MaxLevel)
            return CommandResult.Rejected(CommandRejection.MaxLevelReached);

        if (command.Building != BuildingType.MainBuilding)
        {
            var mainLevel = village.BuildingLevel(BuildingType.MainBuilding);
            if (targetLevel > BuildingRules.MaxOtherBuildingLevel(_data, mainLevel))
                return CommandResult.Rejected(CommandRejection.MainBuildingLevelTooLow);
        }

        var cost = BuildingRules.UpgradeCost(_data, command.Building, targetLevel);
        if (!village.Resources.Covers(cost))
            return CommandResult.Rejected(CommandRejection.InsufficientResources);

        village.Resources -= cost;

        // One builder per village: this item starts when the one before it finishes.
        var start = village.BuildQueue.Count > 0 ? village.BuildQueue[^1].FinishTime : _state.ElapsedTime;
        var duration = BuildingRules.BuildTimeSeconds(_data, command.Building, targetLevel) / _state.Settings.SpeedMultiplier;
        village.EnqueueBuild(new BuildQueueItem(command.Building, targetLevel, start + duration));

        return CommandResult.Ok;
    }
}
