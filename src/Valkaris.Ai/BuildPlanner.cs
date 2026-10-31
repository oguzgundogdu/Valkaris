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
    public Resources Plan(Village village, bool threatened = false)
    {
        var wanted = Wanted(village, threatened).ToList();
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

    /// <summary>What the village's most urgent build lacks, so another village can send it.</summary>
    public Resources Shortfall(Village village)
    {
        foreach (var building in Wanted(village, threatened: false))
        {
            var option = _view.PreviewBuild(village.Id, building);
            if (option.Rejection == CommandRejection.InsufficientResources)
            {
                return new Resources(
                    Math.Max(0, option.Cost.Wood - village.Resources.Wood),
                    Math.Max(0, option.Cost.Stone - village.Resources.Stone),
                    Math.Max(0, option.Cost.Food - village.Resources.Food));
            }

            if (option.Rejection is null or CommandRejection.QueueFull)
                return Resources.Zero;
        }
        return Resources.Zero;
    }

    /// <summary>Most urgent first.</summary>
    private IEnumerable<BuildingType> Wanted(Village village, bool threatened)
    {
        if (threatened && PlannedLevel(village, BuildingType.Wall) < _view.Profile.MaxWallLevel)
            yield return BuildingType.Wall;

        var capacity = _view.CapacityOf(village);
        var full = capacity * _view.Config.WarehouseFullShare;
        if (village.Resources.Wood >= full || village.Resources.Stone >= full || village.Resources.Food >= full)
            yield return BuildingType.Warehouse;

        if (village.Id == _view.CapitalVillageId
            && _view.OwnVillages.Count >= 2
            && PlannedLevel(village, BuildingType.Marketplace) < _view.Config.MarketplaceLevel)
            yield return BuildingType.Marketplace;

        foreach (var step in _view.Config.BuildOrder)
        {
            if (PlannedLevel(village, step.Building) >= step.Level)
                continue;
            if (step.Building == BuildingType.Wall && PlannedLevel(village, BuildingType.Wall) >= _view.Profile.MaxWallLevel)
                continue;
            if (step.Building == BuildingType.Watchtower && PlannedLevel(village, BuildingType.Watchtower) >= _view.Profile.MaxWatchtowerLevel)
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
