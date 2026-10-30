using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>What a SendTroopsCommand would do right now.</summary>
public sealed record SendOption
{
    public CommandRejection? Rejection { get; init; }
    public double DurationSeconds { get; init; }
    public double ArrivalTime { get; init; }

    public bool CanSend => Rejection is null;
}

/// <summary>The single evaluation of the sending rules, shared by CommandHandler and the UI. Read-only.</summary>
public static class SendPreview
{
    public static SendOption Evaluate(GameState state, GameData data, int playerId, SendTroopsCommand command)
    {
        if (state.IsMatchOver)
            return new SendOption { Rejection = CommandRejection.MatchOver };
        if (!state.Villages.TryGetValue(command.FromVillageId, out var from))
            return new SendOption { Rejection = CommandRejection.VillageNotFound };
        if (from.OwnerId != playerId)
            return new SendOption { Rejection = CommandRejection.NotOwner };
        if (!state.Villages.TryGetValue(command.ToVillageId, out var to))
            return new SendOption { Rejection = CommandRejection.VillageNotFound };

        var rejection = FindRejection(from, to, command);
        if (rejection is not null)
            return new SendOption { Rejection = rejection };

        var duration = UnitRules.TravelSeconds(data, state, from.Position, to.Position, command.Troops);
        return new SendOption { DurationSeconds = duration, ArrivalTime = state.ElapsedTime + duration };
    }

    private static CommandRejection? FindRejection(Village from, Village to, SendTroopsCommand command)
    {
        switch (command.Type)
        {
            case MovementType.Attack or MovementType.Raid when to.OwnerId == from.OwnerId:
            case MovementType.Reinforce when to.OwnerId != from.OwnerId || to.Id == from.Id:
            case MovementType.Settle:
            case MovementType.Return:
                return CommandRejection.InvalidMission;
        }

        if (command.Troops.Values.Any(count => count < 0))
            return CommandRejection.InvalidCount;
        if (command.Troops.Values.All(count => count == 0))
            return CommandRejection.NoTroopsSelected;
        if (command.Troops.Any(t => t.Value > from.TroopCount(t.Key)))
            return CommandRejection.NotEnoughTroops;

        if (command.Type is MovementType.Attack or MovementType.Raid && command.Troops.GetValueOrDefault(UnitType.Settler) > 0)
            return CommandRejection.SettlersCannotFight;
        if (command.Type == MovementType.Raid && command.Troops.GetValueOrDefault(UnitType.Lord) > 0)
            return CommandRejection.LordsCannotRaid;

        var catapults = command.Troops.GetValueOrDefault(UnitType.Catapult);
        if (command.Type == MovementType.Raid && catapults > 0)
            return CommandRejection.CatapultsCannotRaid;
        if (command.CatapultTarget is not null && (catapults == 0 || command.Type != MovementType.Attack))
            return CommandRejection.InvalidMission;

        return null;
    }
}
