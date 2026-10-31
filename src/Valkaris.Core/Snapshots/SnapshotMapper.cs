using System.Text.Json;
using System.Text.Json.Serialization;
using Valkaris.Core.Model;

namespace Valkaris.Core.Snapshots;

/// <summary>Converts between the encapsulated model and its wire format. The model is never serialized directly.</summary>
public static class SnapshotMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static string ToJson(GameStateSnapshot snapshot) => JsonSerializer.Serialize(snapshot, JsonOptions);

    public static GameStateSnapshot FromJson(string json) =>
        JsonSerializer.Deserialize<GameStateSnapshot>(json, JsonOptions)
        ?? throw new JsonException("Snapshot is empty or null.");

    public static GameStateSnapshot ToSnapshot(GameState state) => new(
        state.Settings,
        state.ElapsedTime,
        state.Players.Values.OrderBy(p => p.Id).Select(ToSnapshot).ToList(),
        state.Villages.Values.OrderBy(v => v.Id).Select(ToSnapshot).ToList(),
        new GameMapSnapshot(
            state.Map.Width,
            state.Map.Height,
            state.Map.Tiles.Select(t => new MapTileSnapshot(t.VillageId, t.Terrain)).ToList()),
        state.Movements.ToList(),
        state.Reports.ToList(),
        state.ScoutReports.ToList(),
        state.ScoutNotices.ToList(),
        state.BattleArrivals.ToList(),
        state.NextMovementId,
        state.NextReportId,
        state.RngState,
        state.Outcome);

    private static PlayerSnapshot ToSnapshot(Player player) => new(
        player.Id,
        player.Name,
        player.Nation,
        player.IsAi,
        player.IsEliminated,
        player.HasSurrendered,
        player.CapitalVillageId,
        player.CapitalChoiceDeadline,
        player.CapturedVillageIds.ToList(),
        player.Difficulty);

    private static VillageSnapshot ToSnapshot(Village village) => new(
        village.Id,
        village.OwnerId,
        village.Position,
        village.Resources,
        village.Buildings.OrderBy(b => b.Key).ToDictionary(b => b.Key, b => b.Value),
        village.BuildQueue.ToList(),
        village.Troops.OrderBy(t => t.Key).ToDictionary(t => t.Key, t => t.Value),
        village.TrainingQueue.ToList(),
        village.Name,
        village.BarbarianTier,
        village.NextTroopRegenTime,
        village.Loyalty);

    public static GameState Restore(GameStateSnapshot snapshot)
    {
        var map = new GameMap(snapshot.Map.Width, snapshot.Map.Height);
        if (snapshot.Map.Tiles.Count != map.Tiles.Count)
            throw new ArgumentException(
                $"Snapshot has {snapshot.Map.Tiles.Count} tiles for a {map.Width}x{map.Height} map.",
                nameof(snapshot));
        for (var i = 0; i < map.Tiles.Count; i++)
        {
            map.Tiles[i].VillageId = snapshot.Map.Tiles[i].VillageId;
            map.Tiles[i].Terrain = snapshot.Map.Tiles[i].Terrain;
        }

        var state = new GameState(snapshot.Settings, map)
        {
            ElapsedTime = snapshot.ElapsedTime,
            NextMovementId = snapshot.NextMovementId,
            NextReportId = snapshot.NextReportId,
            RngState = snapshot.RngState,
            Outcome = snapshot.Outcome,
        };

        foreach (var movement in snapshot.Movements)
            state.AddMovement(movement);
        foreach (var report in snapshot.Reports)
            state.AddReport(report);
        foreach (var report in snapshot.ScoutReports)
            state.AddScoutReport(report);
        foreach (var notice in snapshot.ScoutNotices)
            state.AddScoutNotice(notice);
        foreach (var arrival in snapshot.BattleArrivals)
            state.AddBattleArrival(arrival);

        foreach (var p in snapshot.Players)
        {
            var player = new Player(p.Id, p.Name, p.Nation, p.IsAi)
            {
                IsEliminated = p.IsEliminated,
                HasSurrendered = p.HasSurrendered,
                CapitalVillageId = p.CapitalVillageId,
                CapitalChoiceDeadline = p.CapitalChoiceDeadline,
                Difficulty = p.Difficulty,
            };
            foreach (var villageId in p.CapturedVillageIds)
                player.RecordCapture(villageId);
            state.AddPlayer(player);
        }

        foreach (var v in snapshot.Villages)
        {
            var village = new Village(v.Id, v.OwnerId, v.Position)
            {
                Resources = v.Resources,
                Name = v.Name,
                BarbarianTier = v.BarbarianTier,
                NextTroopRegenTime = v.NextTroopRegenTime,
                Loyalty = v.Loyalty,
            };
            foreach (var (type, level) in v.Buildings)
                village.SetBuildingLevel(type, level);
            foreach (var item in v.BuildQueue)
                village.EnqueueBuild(item);
            foreach (var (unit, count) in v.Troops)
                village.AddTroops(unit, count);
            foreach (var item in v.TrainingQueue)
                village.EnqueueTraining(item);
            state.AddVillage(village);
        }

        return state;
    }
}
