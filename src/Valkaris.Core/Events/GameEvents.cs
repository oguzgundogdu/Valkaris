using Valkaris.Core.Model;

namespace Valkaris.Core.Events;

/// <summary>Something that happened during simulation, for the UI and for clients to react to.</summary>
public abstract record GameEvent(double Time);

public sealed record BuildingCompleted(double Time, int VillageId, BuildingType Building, int NewLevel) : GameEvent(Time);
