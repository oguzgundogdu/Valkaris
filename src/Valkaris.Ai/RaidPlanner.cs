using Valkaris.Core.Commands;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Ai;

/// <summary>Sends spare troops after barbarian loot, where the expected loot is worth the walk and the margin holds.</summary>
public sealed class RaidPlanner
{
    private readonly AiView _view;
    private readonly AiMemory _memory;
    private readonly Forces _forces;

    public RaidPlanner(AiView view, AiMemory memory)
    {
        _view = view;
        _memory = memory;
        _forces = new Forces(view);
    }

    public void Plan(Village village)
    {
        var raiding = _view.OwnMovements.Count(m => m.Type == MovementType.Raid && m.FromVillageId == village.Id);
        if (raiding >= _view.Profile.MaxRaidsPerVillage)
            return;

        var spare = _forces.Spare(village);
        if (spare.Count == 0)
            return;

        foreach (var target in Targets(village))
        {
            var group = _forces.Pick(spare, _memory.EstimateDefence(target) * _view.Profile.RaidMargin);
            if (group is null)
                continue;

            var command = new SendTroopsCommand(village.Id, target.Id, MovementType.Raid, group);
            if (_view.PreviewSend(command).CanSend)
                _view.Send(command);
            return;
        }
    }

    /// <summary>The best loot for the distance first. Skips targets just raided or already on the way to.</summary>
    private IEnumerable<KnownVillage> Targets(Village village)
    {
        var heading = _view.OwnMovements
            .Where(m => m.Type is MovementType.Raid or MovementType.Attack)
            .Select(m => m.ToVillageId)
            .ToHashSet();

        return _view.KnownVillages
            .Where(v => v.IsBarbarian && !heading.Contains(v.Id))
            .Where(v => !RaidedRecently(v))
            .Select(v => (Village: v, Loot: _memory.ExpectedBarbarianLoot(v)))
            .Where(c => c.Loot >= _view.Config.RaidMinLoot)
            .OrderByDescending(c => c.Loot / UnitRules.Distance(village.Position, c.Village.Position))
            .ThenBy(c => c.Village.Id)
            .Select(c => c.Village);
    }

    private bool RaidedRecently(KnownVillage village) =>
        _memory.SightingOf(village.Id) is { LastBattleTime: { } fought }
        && (_view.Now - fought) * _view.GameSpeed < _view.Profile.RaidCooldownSeconds;
}
