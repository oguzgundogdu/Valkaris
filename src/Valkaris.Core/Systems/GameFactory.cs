using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

public sealed record PlayerSlot(string Name, Nation Nation, bool IsAi);

public static class GameFactory
{
    public static GameState CreateMatch(GameSettings settings, IReadOnlyList<PlayerSlot> slots, GameData data)
    {
        if (settings.MatchDurationSeconds <= 0)
            throw new ArgumentException("Match duration must be positive.", nameof(settings));
        if (settings.SpeedMultiplier <= 0)
            throw new ArgumentException("Speed multiplier must be positive.", nameof(settings));
        if (slots.Count < data.Game.MinPlayers || slots.Count > data.Game.MaxPlayers)
            throw new ArgumentException(
                $"A match needs {data.Game.MinPlayers} to {data.Game.MaxPlayers} player slots, got {slots.Count}.",
                nameof(slots));

        var size = data.Game.MapSizes[settings.MapSize];
        var state = new GameState(settings, new GameMap(size.Width, size.Height));

        for (var i = 0; i < slots.Count; i++)
        {
            var id = i + 1;
            var slot = slots[i];
            var player = new Player(id, slot.Name, slot.Nation, slot.IsAi) { CapitalVillageId = id };
            state.AddPlayer(player);
            state.AddVillage(CreateStartingVillage(state, id, player.Id, StartPosition(i, state.Map), data));
        }

        return state;
    }

    private static Village CreateStartingVillage(GameState state, int id, int ownerId, TileCoord position, GameData data)
    {
        var tile = state.Map.TileAt(position);
        if (tile.VillageId is not null)
            throw new ArgumentException($"Map is too small to place every player: {position} is already taken.");

        var village = new Village(id, ownerId, position) { Resources = data.Game.StartingResources };
        foreach (var (type, level) in data.Game.StartingBuildings)
            village.SetBuildingLevel(type, level);
        foreach (var (unit, count) in data.Game.StartingTroops)
            village.AddTroops(unit, count);

        tile.VillageId = village.Id;
        return village;
    }

    // Placeholder placement: one player per map quadrant. Real map generation comes with the world map.
    private static TileCoord StartPosition(int index, GameMap map)
    {
        var left = map.Width / 4;
        var right = map.Width * 3 / 4;
        var top = map.Height / 4;
        var bottom = map.Height * 3 / 4;
        return index switch
        {
            0 => new TileCoord(left, top),
            1 => new TileCoord(right, bottom),
            2 => new TileCoord(right, top),
            _ => new TileCoord(left, bottom),
        };
    }
}
