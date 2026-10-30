using Valkaris.Core.Commands;
using Valkaris.Core.Model;

namespace Valkaris.Ai;

/// <summary>Sees attacks coming. If the profile allows, safe villages send help that can arrive in time.</summary>
public sealed class DefencePlanner
{
    private readonly AiView _view;
    private readonly HashSet<(int Target, double Arrival)> _answered = new();

    public DefencePlanner(AiView view) => _view = view;

    public bool IsThreatened(Village village) => _view.Incoming.Any(i => i.TargetVillageId == village.Id);

    public void Plan()
    {
        if (!_view.Profile.Reinforces)
            return;

        var villages = _view.OwnVillages;
        foreach (var attack in _view.Incoming.OrderBy(i => i.ArrivalTime))
        {
            if (_answered.Add((attack.TargetVillageId, attack.ArrivalTime)))
                SendHelp(attack, villages);
        }
    }

    private void SendHelp(IncomingAttack attack, IReadOnlyList<Village> villages)
    {
        foreach (var helper in villages.Where(v => v.Id != attack.TargetVillageId && !IsThreatened(v)))
        {
            var troops = new Dictionary<UnitType, int>();
            foreach (var unit in new[] { UnitType.Spearman, UnitType.Swordsman, UnitType.Cavalry })
            {
                var count = (int)(helper.TroopCount(unit) * _view.Config.ReinforceShare);
                if (count > 0)
                    troops[unit] = count;
            }

            if (troops.Count == 0)
                continue;

            var command = new SendTroopsCommand(helper.Id, attack.TargetVillageId, MovementType.Reinforce, troops);
            var option = _view.PreviewSend(command);
            if (option.CanSend && option.ArrivalTime < attack.ArrivalTime)
                _view.Send(command);
        }
    }
}
