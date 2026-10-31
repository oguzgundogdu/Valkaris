using Valkaris.Core.Commands;
using Valkaris.Core.Model;

namespace Valkaris.Core.Events;

/// <summary>Something that happened during simulation, for the UI and for clients to react to.</summary>
public abstract record GameEvent(double Time);

public sealed record BuildingCompleted(double Time, int VillageId, BuildingType Building, int NewLevel) : GameEvent(Time);

public sealed record TroopsTrained(double Time, int VillageId, UnitType Unit, int Count) : GameEvent(Time);

public sealed record TroopsReturned(double Time, int VillageId, Resources Loot) : GameEvent(Time);

public sealed record ReinforcementsArrived(double Time, int VillageId) : GameEvent(Time);

public sealed record BattleResolved(double Time, int ReportId) : GameEvent(Time);

/// <summary>Look at ScoutReport.VisibleTo before telling anyone: a success is known only to the sender.</summary>
public sealed record ScoutResolved(double Time, int ReportId) : GameEvent(Time);

public sealed record VillageCaptured(double Time, int VillageId, int PreviousOwnerId, int NewOwnerId) : GameEvent(Time);

public sealed record CapitalChoiceRequired(double Time, int PlayerId, double Deadline) : GameEvent(Time);

/// <summary>A capital picked automatically. A player's own choice takes effect at once, through a command.</summary>
public sealed record CapitalChanged(double Time, int PlayerId, int VillageId) : GameEvent(Time);

public sealed record PlayerEliminated(double Time, int PlayerId) : GameEvent(Time);

public sealed record PlayerSurrendered(double Time, int PlayerId) : GameEvent(Time);

public sealed record MatchEnded(double Time) : GameEvent(Time);

public sealed record ResourcesDelivered(double Time, int VillageId, Resources Delivered, Resources Lost) : GameEvent(Time);

/// <summary>Carriers back home. They hold a load only if their target was lost on the way.</summary>
public sealed record CarriersReturned(double Time, int VillageId, Resources Delivered, Resources Lost) : GameEvent(Time);

public sealed record VillageFounded(double Time, int VillageId) : GameEvent(Time);

/// <summary>The tile or the limit no longer allowed it, so the Settler is walking home.</summary>
public sealed record SettlingFailed(double Time, int OwnerId, CommandRejection Reason) : GameEvent(Time);
