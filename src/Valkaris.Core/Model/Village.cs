using System.Collections.ObjectModel;

namespace Valkaris.Core.Model;

public sealed class Village
{
    private readonly Dictionary<BuildingType, int> _buildings = new();
    private readonly List<BuildQueueItem> _buildQueue = new();

    internal Village(int id, int ownerId, TileCoord position)
    {
        Id = id;
        OwnerId = ownerId;
        Position = position;
        Buildings = new ReadOnlyDictionary<BuildingType, int>(_buildings);
        BuildQueue = _buildQueue.AsReadOnly();
    }

    public int Id { get; }
    public int OwnerId { get; internal set; }
    public TileCoord Position { get; }
    public Resources Resources { get; internal set; }
    public IReadOnlyDictionary<BuildingType, int> Buildings { get; }
    public IReadOnlyList<BuildQueueItem> BuildQueue { get; }

    public int BuildingLevel(BuildingType type) => _buildings.GetValueOrDefault(type);

    internal void SetBuildingLevel(BuildingType type, int level) => _buildings[type] = level;

    internal void EnqueueBuild(BuildQueueItem item) => _buildQueue.Add(item);

    internal void DequeueBuild() => _buildQueue.RemoveAt(0);
}
