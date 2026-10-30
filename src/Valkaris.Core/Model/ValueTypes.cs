namespace Valkaris.Core.Model;

public readonly record struct TileCoord(int X, int Y);

public readonly record struct Resources(double Wood, double Stone, double Food)
{
    public static Resources Zero => default;

    public double Get(ResourceType type) => type switch
    {
        ResourceType.Wood => Wood,
        ResourceType.Stone => Stone,
        ResourceType.Food => Food,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static Resources Of(ResourceType type, double amount) => type switch
    {
        ResourceType.Wood => new Resources(amount, 0, 0),
        ResourceType.Stone => new Resources(0, amount, 0),
        ResourceType.Food => new Resources(0, 0, amount),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public bool Covers(Resources cost) =>
        Wood >= cost.Wood && Stone >= cost.Stone && Food >= cost.Food;

    public Resources CappedAt(double cap) =>
        new(Math.Min(Wood, cap), Math.Min(Stone, cap), Math.Min(Food, cap));

    public static Resources operator +(Resources a, Resources b) =>
        new(a.Wood + b.Wood, a.Stone + b.Stone, a.Food + b.Food);

    public static Resources operator -(Resources a, Resources b) =>
        new(a.Wood - b.Wood, a.Stone - b.Stone, a.Food - b.Food);

    public static Resources operator *(Resources a, double factor) =>
        new(a.Wood * factor, a.Stone * factor, a.Food * factor);
}

public sealed record GameSettings(double MatchDurationSeconds, double SpeedMultiplier, MapSize MapSize);

public sealed record BuildQueueItem(BuildingType Building, int TargetLevel, double StartTime, double FinishTime);
