using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>What a player may know about others while the match runs. The UI, AiView and client snapshots all ask here.</summary>
public static class Visibility
{
    public static double SightRadius(GameData data, Village village) =>
        data.Map.SightRadius + data.Map.SightPerWatchtowerLevel * village.BuildingLevel(BuildingType.Watchtower);

    public static bool InSight(GameState state, GameData data, int playerId, double x, double y)
    {
        foreach (var village in state.Villages.Values)
        {
            if (village.OwnerId != playerId)
                continue;

            var (dx, dy) = (x - village.Position.X, y - village.Position.Y);
            var radius = SightRadius(data, village);
            if (dx * dx + dy * dy <= radius * radius)
                return true;
        }
        return false;
    }

    /// <summary>An Attack or Raid on this player's village, shown from any distance, without its troops or loot.</summary>
    public static bool IsIncoming(GameState state, int playerId, Movement movement) =>
        movement.OwnerId != playerId
        && movement.Type is MovementType.Attack or MovementType.Raid
        && movement.ToVillageId is { } target
        && state.Villages[target].OwnerId == playerId;

    /// <summary>Other players' movements now within sight. Scouts are never seen coming (GDD 7.2), and incoming attacks are already shown in full.</summary>
    public static IReadOnlyList<SightedMovement> SightedMovements(GameState state, GameData data, int playerId)
    {
        var seen = new List<SightedMovement>();
        foreach (var movement in state.Movements)
        {
            if (movement.OwnerId == playerId || movement.Type == MovementType.Scout || IsIncoming(state, playerId, movement))
                continue;

            var from = movement.FromTile ?? state.Villages[movement.FromVillageId].Position;
            var to = movement.ToVillageId is { } target ? state.Villages[target].Position : movement.TargetTile!.Value;
            var duration = movement.ArrivalTime - movement.DepartureTime;
            var (velocityX, velocityY) = duration > 0 ? ((to.X - from.X) / duration, (to.Y - from.Y) / duration) : (0.0, 0.0);
            var travelled = state.ElapsedTime - movement.DepartureTime;
            var (x, y) = (from.X + velocityX * travelled, from.Y + velocityY * travelled);

            if (InSight(state, data, playerId, x, y))
                seen.Add(new SightedMovement(movement.Id, movement.Type, x, y, velocityX, velocityY, state.ElapsedTime));
        }
        return seen;
    }

    /// <summary>Battles between two other players, oldest first. Attacks on barbarian or abandoned villages aren't news.</summary>
    public static IEnumerable<PublicNotice> PublicNotices(GameState state, int playerId) =>
        state.BattleArrivals
            .Where(a => a.DefenderId != Village.BarbarianOwnerId && a.AttackerId != playerId && a.DefenderId != playerId)
            .Select(a => new PublicNotice(a.Time, a.FromVillageName, a.TargetVillageId, a.TargetVillageName));

    /// <summary>Battles this player saw happen after a given time, oldest first.</summary>
    public static IReadOnlyList<Clash> Clashes(GameState state, GameData data, int playerId, double after)
    {
        // Runs every frame on the map, so it walks back from the newest and stops early.
        var seen = new List<Clash>();
        for (var i = state.BattleArrivals.Count - 1; i >= 0 && state.BattleArrivals[i].Time > after; i--)
        {
            if (SeesClash(state, data, playerId, state.BattleArrivals[i]))
                seen.Add(new Clash(state.BattleArrivals[i].Time, state.BattleArrivals[i].TargetVillageId));
        }
        seen.Reverse();
        return seen;
    }

    /// <summary>Would this player see this battle happen: their own, or at a village in their sight.</summary>
    public static bool SeesClash(GameState state, GameData data, int playerId, BattleArrival arrival)
    {
        if (arrival.AttackerId == playerId || arrival.DefenderId == playerId)
            return true;

        var position = state.Villages[arrival.TargetVillageId].Position;
        return InSight(state, data, playerId, position.X, position.Y);
    }
}
