using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>What a SendResourcesCommand would do right now. Carrier counts are filled in even when it is rejected.</summary>
public sealed record SendResourcesOption
{
    public CommandRejection? Rejection { get; init; }
    public int CarriersUsed { get; init; }
    public int CarriersFree { get; init; }
    public int CarriersTotal { get; init; }
    public double DurationSeconds { get; init; }
    public double ArrivalTime { get; init; }

    /// <summary>An estimate of what the target's warehouse will not hold. Later events can change the result.</summary>
    public Resources ExpectedLoss { get; init; }

    public bool CanSend => Rejection is null;
}

/// <summary>The single evaluation of the transfer rules, shared by CommandHandler and the UI. Read-only.</summary>
public static class SendResourcesPreview
{
    public static SendResourcesOption Evaluate(GameState state, GameData data, int playerId, SendResourcesCommand command)
    {
        if (state.IsMatchOver)
            return new SendResourcesOption { Rejection = CommandRejection.MatchOver };
        if (!state.Villages.TryGetValue(command.FromVillageId, out var from))
            return new SendResourcesOption { Rejection = CommandRejection.VillageNotFound };
        if (from.OwnerId != playerId)
            return new SendResourcesOption { Rejection = CommandRejection.NotOwner };

        var total = command.Wood + command.Stone + command.Food;
        var option = new SendResourcesOption
        {
            CarriersUsed = CarrierRules.Needed(data, Math.Max(0, total)),
            CarriersFree = CarrierRules.Free(state, data, from),
            CarriersTotal = CarrierRules.Total(data, from),
        };

        if (!state.Villages.TryGetValue(command.ToVillageId, out var to))
            return option with { Rejection = CommandRejection.VillageNotFound };

        var rejection = FindRejection(from, to, command, option);
        if (rejection is not null)
            return option with { Rejection = rejection };

        var duration = CarrierRules.TravelSeconds(data, state, from.Position, to.Position);
        var arrival = state.ElapsedTime + duration;
        var load = new Resources(command.Wood, command.Stone, command.Food);
        return option with
        {
            DurationSeconds = duration,
            ArrivalTime = arrival,
            ExpectedLoss = ExpectedLoss(state, data, to, load, duration, arrival),
        };
    }

    private static CommandRejection? FindRejection(Village from, Village to, SendResourcesCommand command, SendResourcesOption option)
    {
        if (to.OwnerId != from.OwnerId)
            return CommandRejection.TargetNotYours;
        if (to.Id == from.Id)
            return CommandRejection.SameVillage;
        if (command.Wood < 0 || command.Stone < 0 || command.Food < 0)
            return CommandRejection.InvalidCount;
        if (command.Wood + command.Stone + command.Food == 0)
            return CommandRejection.NoResourcesSelected;
        if (from.BuildingLevel(BuildingType.Marketplace) == 0)
            return CommandRejection.NoMarketplace;
        if (!from.Resources.Covers(new Resources(command.Wood, command.Stone, command.Food)))
            return CommandRejection.InsufficientResources;
        if (option.CarriersUsed > option.CarriersFree)
            return CommandRejection.NotEnoughCarriers;
        return null;
    }

    /// <summary>The target's stock, what it produces until arrival, and transfers arriving first, against its capacity.</summary>
    private static Resources ExpectedLoss(GameState state, GameData data, Village to, Resources load, double duration, double arrival)
    {
        var capacity = BuildingRules.VillageCapacity(data, to);
        var produced = BuildingRules.VillageProduction(data, to) * (duration * state.Settings.SpeedMultiplier);
        var stock = (to.Resources + produced).CappedAt(capacity);

        foreach (var movement in state.Movements.Where(m => m.Type == MovementType.Transfer && m.ToVillageId == to.Id && m.ArrivalTime <= arrival))
            stock += movement.Loot;

        double Lost(ResourceType type) =>
            Math.Clamp(stock.Get(type) + load.Get(type) - capacity, 0, load.Get(type));

        return new Resources(Lost(ResourceType.Wood), Lost(ResourceType.Stone), Lost(ResourceType.Food));
    }
}
