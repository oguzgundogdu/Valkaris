using Valkaris.Core.Commands;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Ai;

/// <summary>Works through the shared build order, per village, so a captured camp rebuilds what it lacks with no special code.</summary>
public sealed class BuildPlanner
{
    private readonly AiView _view;

    public BuildPlanner(AiView view) => _view = view;

    /// <summary>Queues what it can. Returns what is next wanted, so training can leave that much in stock.</summary>
    public Resources Plan(Village village)
    {
        var wanted = Wanted(village).ToList();
        foreach (var building in wanted)
        {
            var option = _view.PreviewBuild(village.Id, building);
            switch (option.Rejection)
            {
                case null:
                    _view.Send(new BuildCommand(village.Id, building));
                    return Resources.Zero;
                case CommandRejection.QueueFull:
                    return Resources.Zero;
                case CommandRejection.InsufficientResources when _view.Profile.SavesForGoals:
                    return option.Cost;
            }
        }
        return Resources.Zero;
    }

    /// <summary>Most urgent first.</summary>
    private IEnumerable<BuildingType> Wanted(Village village)
    {
        var capacity = _view.CapacityOf(village);
        var full = capacity * _view.Config.WarehouseFullShare;
        if (village.Resources.Wood >= full || village.Resources.Stone >= full || village.Resources.Food >= full)
            yield return BuildingType.Warehouse;

        foreach (var step in _view.Config.BuildOrder)
        {
            if (PlannedLevel(village, step.Building) >= step.Level)
                continue;

            var option = _view.PreviewBuild(village.Id, step.Building);
            if (option.CostExceedsCapacity)
                yield return BuildingType.Warehouse;
            if (option.Rejection == CommandRejection.MainBuildingLevelTooLow)
                yield return BuildingType.MainBuilding;
            yield return step.Building;
        }
    }

    private static int PlannedLevel(Village village, BuildingType building) =>
        village.BuildQueue.Where(q => q.Building == building).Select(q => q.TargetLevel).DefaultIfEmpty(village.BuildingLevel(building)).Max();
}
