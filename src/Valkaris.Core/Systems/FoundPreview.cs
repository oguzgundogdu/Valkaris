using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>What a FoundVillageCommand would do right now.</summary>
public sealed record FoundOption
{
    public CommandRejection? Rejection { get; init; }
    public double DurationSeconds { get; init; }
    public double ArrivalTime { get; init; }

    public bool CanFound => Rejection is null;
}

/// <summary>The single evaluation of the founding rules, shared by CommandHandler, Simulation and the UI. Read-only.</summary>
public static class FoundPreview
{
    internal static readonly IReadOnlyDictionary<UnitType, int> OneSettler = new Dictionary<UnitType, int> { [UnitType.Settler] = 1 };

    public static FoundOption Evaluate(GameState state, GameData data, int playerId, int fromVillageId, TileCoord tile)
    {
        if (state.IsMatchOver)
            return new FoundOption { Rejection = CommandRejection.MatchOver };
        if (!state.Villages.TryGetValue(fromVillageId, out var from))
            return new FoundOption { Rejection = CommandRejection.VillageNotFound };
        if (from.OwnerId != playerId)
            return new FoundOption { Rejection = CommandRejection.NotOwner };

        var rejection = TileRejection(state, data, tile) ?? LimitRejection(state, data, playerId);
        if (rejection is null && from.TroopCount(UnitType.Settler) < 1)
            rejection = CommandRejection.NoSettlerHere;
        if (rejection is not null)
            return new FoundOption { Rejection = rejection };

        var duration = GodRules.TravelSeconds(state, playerId, UnitRules.TravelSeconds(data, state, from.Position, tile, OneSettler, NationRules.Of(data, state, playerId)));
        return new FoundOption { DurationSeconds = duration, ArrivalTime = state.ElapsedTime + duration };
    }

    /// <summary>Checked when sending and again on arrival, since another village may have appeared in between.</summary>
    internal static CommandRejection? TileRejection(GameState state, GameData data, TileCoord tile)
    {
        if (!state.Map.Contains(tile))
            return CommandRejection.TileOutsideMap;

        var mapTile = state.Map.TileAt(tile);
        if (mapTile.VillageId is not null)
            return CommandRejection.TileTaken;
        if (mapTile.Terrain == Terrain.Water)
            return CommandRejection.TileIsWater;
        if (state.Villages.Values.Any(v => UnitRules.Distance(v.Position, tile) < data.Map.MinFoundingDistance))
            return CommandRejection.TooCloseToVillage;

        return null;
    }

    /// <summary>Settlers on the way count, so several cannot be sent for one free place. Captured villages use up places too, but are never refused.</summary>
    internal static CommandRejection? LimitRejection(GameState state, GameData data, int playerId)
    {
        if (state.Players[playerId].CapitalVillageId is not { } capitalId)
            return CommandRejection.NoCapital;

        var mainLevel = state.Villages[capitalId].BuildingLevel(BuildingType.MainBuilding);
        var limit = data.Buildings.MaxVillagesByCapitalMainLevel[mainLevel];
        var owned = state.Villages.Values.Count(v => v.OwnerId == playerId);
        var settling = state.Movements.Count(m => m.OwnerId == playerId && m.Type == MovementType.Settle);

        return owned + settling >= limit ? CommandRejection.VillageLimitReached : null;
    }
}
