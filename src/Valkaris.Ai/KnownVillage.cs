using Valkaris.Core.Model;

namespace Valkaris.Ai;

/// <summary>What anyone can see of a village on the map. Garrison, buildings and loyalty are not here: only reports tell those.</summary>
public sealed record KnownVillage(int Id, int OwnerId, TileCoord Position, string Name, string? BarbarianTier, bool IsCapital)
{
    public bool IsBarbarian => OwnerId == Village.BarbarianOwnerId;
}

/// <summary>An attack or raid heading for one of the AI's villages. Like a human, it can't see how many are coming.</summary>
public sealed record IncomingAttack(int OwnerId, int FromVillageId, int TargetVillageId, MovementType Type, double ArrivalTime);
