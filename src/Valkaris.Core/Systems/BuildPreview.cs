using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>What a BuildCommand would do right now. Cost and duration stay filled in when it is blocked, for display.</summary>
public sealed record BuildOption
{
    public CommandRejection? Rejection { get; init; }
    public int TargetLevel { get; init; }
    public Resources Cost { get; init; }
    public double StartTime { get; init; }
    public double DurationSeconds { get; init; }

    /// <summary>The cost is above the village's warehouse capacity, so saving up alone can never pay for it.</summary>
    public bool CostExceedsCapacity { get; init; }

    public bool CanBuild => Rejection is null;
}

/// <summary>
/// The single evaluation of the build rules, shared by CommandHandler and the UI so they cannot disagree.
/// Read-only.
/// </summary>
public static class BuildPreview
{
    public static BuildOption Evaluate(GameState state, GameData data, int playerId, int villageId, BuildingType building)
    {
        if (state.IsMatchOver)
            return new BuildOption { Rejection = CommandRejection.MatchOver };
        if (!state.Villages.TryGetValue(villageId, out var village))
            return new BuildOption { Rejection = CommandRejection.VillageNotFound };
        if (village.OwnerId != playerId)
            return new BuildOption { Rejection = CommandRejection.NotOwner };

        var god = GodRules.Applies(state, playerId);
        var queuedUpgrades = village.BuildQueue.Count(item => item.Building == building);
        var targetLevel = village.BuildingLevel(building) + queuedUpgrades + 1;
        var queueFull = !god && village.BuildQueue.Count >= data.Game.MaxBuildQueueLength;

        if (targetLevel > data.Buildings.Buildings[building].MaxLevel)
        {
            return new BuildOption
            {
                Rejection = queueFull ? CommandRejection.QueueFull : CommandRejection.MaxLevelReached,
                TargetLevel = targetLevel,
            };
        }

        var cost = god ? Resources.Zero : BuildingRules.UpgradeCost(data, building, targetLevel);
        var capacity = BuildingRules.VillageCapacity(data, village);

        return new BuildOption
        {
            Rejection = FindRejection(data, village, building, targetLevel, cost, queueFull),
            TargetLevel = targetLevel,
            Cost = cost,

            // One builder per village: this item starts when the one before it finishes.
            StartTime = village.BuildQueue.Count > 0 ? village.BuildQueue[^1].FinishTime : state.ElapsedTime,
            DurationSeconds = god ? 0 : BuildingRules.BuildTimeSeconds(data, building, targetLevel) / state.Settings.SpeedMultiplier,
            CostExceedsCapacity = cost.Wood > capacity || cost.Stone > capacity || cost.Food > capacity,
        };
    }

    private static CommandRejection? FindRejection(
        GameData data, Village village, BuildingType building, int targetLevel, Resources cost, bool queueFull)
    {
        if (queueFull)
            return CommandRejection.QueueFull;

        if (building != BuildingType.MainBuilding)
        {
            var mainLevel = village.BuildingLevel(BuildingType.MainBuilding);
            if (targetLevel > BuildingRules.MaxOtherBuildingLevel(data, mainLevel))
                return CommandRejection.MainBuildingLevelTooLow;
        }

        return village.Resources.Covers(cost) ? null : CommandRejection.InsufficientResources;
    }
}
