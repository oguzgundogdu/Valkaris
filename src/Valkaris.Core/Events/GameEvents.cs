using Valkaris.Core.Model;

namespace Valkaris.Core.Events;

/// <summary>Something that happened during simulation, for the UI and for clients to react to.</summary>
public abstract record GameEvent(double Time);

public sealed record BuildingCompleted(double Time, int VillageId, BuildingType Building, int NewLevel) : GameEvent(Time);

public sealed record TroopsTrained(double Time, int VillageId, UnitType Unit, int Count) : GameEvent(Time);

public sealed record TroopsReturned(double Time, int VillageId, Resources Loot) : GameEvent(Time);

public sealed record ReinforcementsArrived(double Time, int VillageId) : GameEvent(Time);

public sealed record BattleResolved(double Time, int ReportId) : GameEvent(Time);
