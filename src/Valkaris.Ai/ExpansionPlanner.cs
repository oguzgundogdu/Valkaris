using Valkaris.Core.Commands;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Ai;

/// <summary>Founds new villages and replaces a lost capital.</summary>
public sealed class ExpansionPlanner
{
    private readonly AiView _view;

    public ExpansionPlanner(AiView view) => _view = view;

    /// <summary>The penalty resets the Main Building, so a low one loses least. Safety from enemies matters too.</summary>
    public void ChooseCapital()
    {
        if (!_view.MustChooseCapital)
            return;

        var threatened = _view.Incoming.Select(i => i.TargetVillageId).ToHashSet();
        var choice = _view.OwnVillages
            .OrderBy(v => threatened.Contains(v.Id))
            .ThenByDescending(v => NearestEnemy(v.Position) - v.BuildingLevel(BuildingType.MainBuilding) * _view.Config.CapitalMainLevelWeight)
            .ThenBy(v => v.Id)
            .First();

        _view.Send(new SetCapitalCommand(choice.Id));
    }

    /// <summary>Sends a Settler it has to the best free land: near its own villages and away from enemies.</summary>
    public void Found(Village village)
    {
        if (village.TroopCount(UnitType.Settler) == 0)
            return;

        var taken = _view.OwnMovements.Where(m => m.Type == MovementType.Settle).Select(m => m.TargetTile!.Value).ToList();
        var best = AllTiles()
            .Where(t => taken.All(other => UnitRules.Distance(t, other) >= _view.MinFoundingDistance))
            .Where(t => _view.PreviewFound(village.Id, t).CanFound)
            .OrderByDescending(Score)
            .ThenBy(t => t.Y)
            .ThenBy(t => t.X)
            .Select(t => (TileCoord?)t)
            .FirstOrDefault();

        if (best is { } tile)
            _view.Send(new FoundVillageCommand(village.Id, tile));
    }

    private double Score(TileCoord tile)
    {
        var nearestOwn = _view.OwnVillages.Min(v => UnitRules.Distance(v.Position, tile));
        return NearestEnemy(tile) - nearestOwn;
    }

    private double NearestEnemy(TileCoord tile)
    {
        var enemies = _view.KnownVillages.Where(v => v.OwnerId != _view.PlayerId && !v.IsBarbarian).ToList();
        return enemies.Count == 0 ? 0 : enemies.Min(v => UnitRules.Distance(v.Position, tile));
    }

    private IEnumerable<TileCoord> AllTiles() =>
        from y in Enumerable.Range(0, _view.MapHeight)
        from x in Enumerable.Range(0, _view.MapWidth)
        select new TileCoord(x, y);
}
