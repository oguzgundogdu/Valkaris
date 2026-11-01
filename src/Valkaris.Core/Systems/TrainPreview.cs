using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>What a TrainCommand would do right now. Cost and duration stay filled in when it is blocked, for display.</summary>
public sealed record TrainOption
{
    public CommandRejection? Rejection { get; init; }
    public Resources Cost { get; init; }
    public double StartTime { get; init; }
    public double DurationSeconds { get; init; }

    /// <summary>The largest batch the village can pay for now.</summary>
    public int MaxCount { get; init; }

    public bool CanTrain => Rejection is null;
}

/// <summary>The single evaluation of the training rules, shared by CommandHandler and the UI. Read-only.</summary>
public static class TrainPreview
{
    public static TrainOption Evaluate(GameState state, GameData data, int playerId, int villageId, UnitType unit, int count)
    {
        if (state.IsMatchOver)
            return new TrainOption { Rejection = CommandRejection.MatchOver };
        if (!state.Villages.TryGetValue(villageId, out var village))
            return new TrainOption { Rejection = CommandRejection.VillageNotFound };
        if (village.OwnerId != playerId)
            return new TrainOption { Rejection = CommandRejection.NotOwner };

        var god = GodRules.Applies(state, playerId);
        var definition = data.Units.Units[unit];
        var barracksLevel = village.BuildingLevel(BuildingType.Barracks);
        var nation = NationRules.Of(data, state, playerId);
        var cost = god ? Resources.Zero : UnitRules.Cost(data, unit, nation) * Math.Max(0, count);
        var seconds = god ? 0 : UnitRules.SecondsPerUnit(data, unit, barracksLevel, nation) * Math.Max(0, count);

        return new TrainOption
        {
            Rejection = FindRejection(data, village, definition, barracksLevel, count, cost, god),
            Cost = cost,

            // One batch at a time: this one starts when the one before it finishes.
            StartTime = village.TrainingQueue.Count > 0 ? village.TrainingQueue[^1].FinishTime : state.ElapsedTime,
            DurationSeconds = seconds / state.Settings.SpeedMultiplier,
            MaxCount = god ? int.MaxValue : UnitRules.MaxAffordable(data, unit, village.Resources, nation),
        };
    }

    private static CommandRejection? FindRejection(
        GameData data, Village village, UnitDefinition definition, int barracksLevel, int count, Resources cost, bool god)
    {
        if (barracksLevel < definition.RequiredBarracksLevel)
            return CommandRejection.BarracksLevelTooLow;
        if (count <= 0)
            return CommandRejection.InvalidCount;
        if (!god && village.TrainingQueue.Count >= data.Game.MaxTrainingQueueLength)
            return CommandRejection.QueueFull;
        return village.Resources.Covers(cost) ? null : CommandRejection.InsufficientResources;
    }
}
