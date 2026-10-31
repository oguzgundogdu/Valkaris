using System.Collections.ObjectModel;

namespace Valkaris.Core.Model;

public sealed class Village
{
    public const int BarbarianOwnerId = -1;
    public const double MaxLoyalty = 100;

    private readonly Dictionary<BuildingType, int> _buildings = new();
    private readonly List<BuildQueueItem> _buildQueue = new();
    private readonly Dictionary<UnitType, int> _troops = new();
    private readonly List<TrainingQueueItem> _trainingQueue = new();

    internal Village(int id, int ownerId, TileCoord position)
    {
        Id = id;
        OwnerId = ownerId;
        Position = position;
        Buildings = new ReadOnlyDictionary<BuildingType, int>(_buildings);
        BuildQueue = _buildQueue.AsReadOnly();
        Troops = new ReadOnlyDictionary<UnitType, int>(_troops);
        TrainingQueue = _trainingQueue.AsReadOnly();
    }

    public int Id { get; }
    public string Name { get; internal set; } = "";
    public int OwnerId { get; internal set; }
    public TileCoord Position { get; }
    public Resources Resources { get; internal set; }
    public double Loyalty { get; internal set; } = MaxLoyalty;
    public IReadOnlyDictionary<BuildingType, int> Buildings { get; }
    public IReadOnlyList<BuildQueueItem> BuildQueue { get; }
    public IReadOnlyDictionary<UnitType, int> Troops { get; }
    public IReadOnlyList<TrainingQueueItem> TrainingQueue { get; }

    /// <summary>Index into the barbarian tiers in data. Null for player villages.</summary>
    public int? BarbarianTier { get; internal set; }

    /// <summary>When a barbarian garrison below its cap regains its next troop.</summary>
    public double? NextTroopRegenTime { get; internal set; }

    public bool IsBarbarian => OwnerId == BarbarianOwnerId;

    /// <summary>Left behind by a surrender: barbarian, but with no tier, so nothing regenerates.</summary>
    public bool IsAbandoned => IsBarbarian && BarbarianTier is null;

    public int BuildingLevel(BuildingType type) => _buildings.GetValueOrDefault(type);

    internal void SetBuildingLevel(BuildingType type, int level) => _buildings[type] = level;

    internal void EnqueueBuild(BuildQueueItem item) => _buildQueue.Add(item);

    internal void DequeueBuild() => _buildQueue.RemoveAt(0);

    public int TroopCount(UnitType unit) => _troops.GetValueOrDefault(unit);

    internal void AddTroops(UnitType unit, int count) => _troops[unit] = TroopCount(unit) + count;

    internal void RemoveTroops(UnitType unit, int count) => _troops[unit] = TroopCount(unit) - count;

    internal void EnqueueTraining(TrainingQueueItem item) => _trainingQueue.Add(item);

    internal void DequeueTraining() => _trainingQueue.RemoveAt(0);

    internal void ClearQueues()
    {
        _buildQueue.Clear();
        _trainingQueue.Clear();
    }

    /// <summary>Keeps queued upgrades consistent after the building lost levels.</summary>
    internal void LowerQueuedBuildTargets(BuildingType building, int levels)
    {
        for (var i = 0; i < _buildQueue.Count; i++)
        {
            if (_buildQueue[i].Building == building)
                _buildQueue[i] = _buildQueue[i] with { TargetLevel = _buildQueue[i].TargetLevel - levels };
        }
    }
}
