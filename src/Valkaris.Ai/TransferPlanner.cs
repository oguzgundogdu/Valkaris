using Valkaris.Core.Commands;
using Valkaris.Core.Model;

namespace Valkaris.Ai;

/// <summary>Moves spare resources to villages that can't pay for their next build, such as new or captured ones.</summary>
public sealed class TransferPlanner
{
    private readonly AiView _view;
    private readonly BuildPlanner _builds;

    public TransferPlanner(AiView view, BuildPlanner builds)
    {
        _view = view;
        _builds = builds;
    }

    public void Plan()
    {
        var villages = _view.OwnVillages;
        if (villages.Count < 2)
            return;

        var supplied = _view.OwnMovements.Where(m => m.Type == MovementType.Transfer).Select(m => m.ToVillageId).ToHashSet();
        var threatened = _view.Incoming.Select(i => i.TargetVillageId).ToHashSet();

        foreach (var target in villages.Where(v => !supplied.Contains(v.Id) && !threatened.Contains(v.Id)))
        {
            var shortfall = _builds.Shortfall(target);
            if (shortfall == Resources.Zero)
                continue;

            foreach (var donor in villages.Where(v => v.Id != target.Id).OrderByDescending(Spare))
            {
                if (TrySend(donor, target, shortfall))
                    break;
            }
        }
    }

    private double Spare(Village village)
    {
        var keep = _view.CapacityOf(village) * _view.Config.DonorKeepShare;
        return Math.Max(0, village.Resources.Wood - keep) + Math.Max(0, village.Resources.Stone - keep) + Math.Max(0, village.Resources.Food - keep);
    }

    private bool TrySend(Village donor, Village target, Resources shortfall)
    {
        var carry = _view.FreeCarriers(donor) * _view.VillageRules.Carriers.Capacity;
        if (carry <= 0)
            return false;

        var keep = _view.CapacityOf(donor) * _view.Config.DonorKeepShare;
        int Give(double wanted, double stock) => (int)Math.Ceiling(Math.Min(wanted, Math.Max(0, Math.Floor(stock - keep))));

        var wood = Give(shortfall.Wood, donor.Resources.Wood);
        var stone = Give(shortfall.Stone, donor.Resources.Stone);
        var food = Give(shortfall.Food, donor.Resources.Food);
        var total = wood + stone + food;
        if (total == 0)
            return false;

        // More than the carriers can hold: everything is cut down in step.
        if (total > carry)
        {
            var scale = (double)carry / total;
            wood = (int)(wood * scale);
            stone = (int)(stone * scale);
            food = (int)(food * scale);
        }

        var command = new SendResourcesCommand(donor.Id, target.Id, wood, stone, food);
        var option = _view.PreviewSendResources(command);
        if (option.ExpectedLoss != Resources.Zero)
        {
            command = command with
            {
                Wood = Math.Max(0, wood - (int)Math.Ceiling(option.ExpectedLoss.Wood)),
                Stone = Math.Max(0, stone - (int)Math.Ceiling(option.ExpectedLoss.Stone)),
                Food = Math.Max(0, food - (int)Math.Ceiling(option.ExpectedLoss.Food)),
            };
            option = _view.PreviewSendResources(command);
        }

        return option.CanSend && _view.Send(command);
    }
}
