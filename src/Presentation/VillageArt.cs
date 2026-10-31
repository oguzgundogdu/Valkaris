using System;
using System.Collections.Generic;
using System.Linq;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>One thing standing on the village ground, at a fraction of its size.</summary>
public readonly record struct Placed(string Sprite, float X, float Y, bool Flip);

/// <summary>What the village looks like at a given size. Visual choices, not balance, and free of Godot so tests can run them.</summary>
public static class VillageArt
{
    public const int StageCount = 3;

    // Where the render tool puts the middle of a building's ground on its canvas. See tools/art.
    public const float SpriteCanvas = 512;
    public const float SpriteGroundX = 256;
    public const float SpriteGroundY = 373;
    public const float SpriteScale = 0.66f;

    public const string PlotSprite = "res://assets/village/plot.png";
    public const string ScaffoldSprite = "res://assets/village/scaffold.png";

    private static readonly IReadOnlyDictionary<BuildingType, string[]> Sprites = new Dictionary<BuildingType, string[]>
    {
        [BuildingType.MainBuilding] = new[] { "res://assets/village/main_1.png", "res://assets/village/main_2.png", "res://assets/village/main_3.png" },
        [BuildingType.Woodcutter] = new[] { "res://assets/village/woodcutter_1.png", "res://assets/village/woodcutter_2.png", "res://assets/village/woodcutter_3.png" },
        [BuildingType.Quarry] = new[] { "res://assets/village/quarry_1.png", "res://assets/village/quarry_2.png", "res://assets/village/quarry_3.png" },
        [BuildingType.Farm] = new[] { "res://assets/village/farm_1.png", "res://assets/village/farm_2.png", "res://assets/village/farm_3.png" },
        [BuildingType.Warehouse] = new[] { "res://assets/village/warehouse_1.png", "res://assets/village/warehouse_2.png", "res://assets/village/warehouse_3.png" },
        [BuildingType.Barracks] = new[] { "res://assets/village/barracks_1.png", "res://assets/village/barracks_2.png", "res://assets/village/barracks_3.png" },
        [BuildingType.Wall] = new[] { "res://assets/village/wall_1.png", "res://assets/village/wall_2.png", "res://assets/village/wall_3.png" },
        [BuildingType.Marketplace] = new[] { "res://assets/village/marketplace_1.png", "res://assets/village/marketplace_2.png", "res://assets/village/marketplace_3.png" },
        [BuildingType.Watchtower] = new[] { "res://assets/village/watchtower_1.png", "res://assets/village/watchtower_2.png", "res://assets/village/watchtower_3.png" },
    };

    public static string Sprite(BuildingType building, int stage) => stage == 0 ? PlotSprite : Sprites[building][stage - 1];

    /// <summary>Plot centres as fractions of the ground, so the layout follows the window size. The same in every village.</summary>
    public static readonly IReadOnlyDictionary<BuildingType, (float X, float Y)> Plots = new Dictionary<BuildingType, (float X, float Y)>
    {
        [BuildingType.MainBuilding] = (0.5f, 0.47f),
        [BuildingType.Woodcutter] = (0.16f, 0.22f),
        [BuildingType.Marketplace] = (0.5f, 0.16f),
        [BuildingType.Quarry] = (0.84f, 0.22f),
        [BuildingType.Farm] = (0.19f, 0.52f),
        [BuildingType.Watchtower] = (0.85f, 0.56f),
        [BuildingType.Warehouse] = (0.27f, 0.8f),
        [BuildingType.Barracks] = (0.73f, 0.8f),
        [BuildingType.Wall] = (0.5f, 0.84f),
    };

    // The smallest logical view, and where a tile's ground sits below its middle.
    public const float SmallestWidth = 832;
    public const float SmallestHeight = 633;
    public const float TileSize = 150;
    public const float TileGroundBelowCentre = 30;

    private const int LevelsPerHouse = 5;
    private const int LevelsPerExtra = 7;
    private const int Attempts = 400;
    private const float PathClearance = 26;
    private const float SceneryGap = 44;

    /// <summary>Where houses go, nearest the Main Building first, so new ones appear further out and old ones never move.</summary>
    public static readonly IReadOnlyList<(float X, float Y)> HouseSpots = new (float X, float Y)[]
    {
        (0.35f, 0.44f), (0.65f, 0.42f), (0.35f, 0.61f), (0.66f, 0.62f), (0.36f, 0.22f), (0.64f, 0.22f),
        (0.13f, 0.37f), (0.87f, 0.38f), (0.1f, 0.76f), (0.9f, 0.76f), (0.1f, 0.86f), (0.9f, 0.86f),
    };

    /// <summary>Crops by the Farm: one more patch for each Farm stage.</summary>
    public static readonly IReadOnlyList<(float X, float Y)> CropSpots = new (float X, float Y)[]
    {
        (0.085f, 0.625f), (0.3f, 0.665f), (0.085f, 0.7f),
    };

    private static readonly string[] Trees = { "res://assets/village/tree_1.png", "res://assets/village/tree_2.png", "res://assets/village/tree_3.png", "res://assets/village/tree_4.png" };
    private static readonly string[] Ground = { "res://assets/village/bush_1.png", "res://assets/village/bush_2.png", "res://assets/village/rock_1.png", "res://assets/village/rock_2.png" };
    private static readonly string[] Houses = { "res://assets/village/house_1.png", "res://assets/village/house_2.png", "res://assets/village/house_3.png", "res://assets/village/house_4.png" };
    private static readonly string[] Crops = { "res://assets/village/crops_1.png", "res://assets/village/crops_2.png" };
    private static readonly string[] Extras = { "res://assets/village/flowers_1.png", "res://assets/village/flowers_2.png", "res://assets/village/fence_1.png" };

    // How the ring lies on the ground, as fractions of it, and how far apart its pieces go on screen.
    private const float RingLeft = 0.03f;
    private const float RingRight = 0.97f;
    private const float RingBack = 0.12f;
    private const float PieceAcross = 37;
    private const float PieceAlong = 19;
    private static readonly string[] RingAcross = { "res://assets/village/ring_across_1.png", "res://assets/village/ring_across_2.png", "res://assets/village/ring_across_3.png" };
    private static readonly string[] RingAlong = { "res://assets/village/ring_along_1.png", "res://assets/village/ring_along_2.png", "res://assets/village/ring_along_3.png" };
    private static readonly string[] RingPost = { "res://assets/village/ring_post_1.png", "res://assets/village/ring_post_2.png", "res://assets/village/ring_post_3.png" };
    private static readonly float[] GateHalfWidth = { 42, 62, 92 };

    /// <summary>The Wall's ring, back to front, leaving a gap for the gatehouse. Nothing until the Wall is built.</summary>
    public static List<Placed> Ring(int wallStage, float width, float height)
    {
        var pieces = new List<Placed>();
        if (wallStage <= 0)
            return pieces;

        var tier = wallStage - 1;
        var front = Plots[BuildingType.Wall].Y + TileGroundBelowCentre / height;
        var gate = GateHalfWidth[tier] / width;

        void Across(float from, float to, float y)
        {
            var count = (int)MathF.Ceiling((to - from) * width / PieceAcross);
            for (var i = 0; i < count; i++)
                pieces.Add(new Placed(RingAcross[tier], from + (to - from) * (i + 0.5f) / count, y, false));
        }

        Across(RingLeft, RingRight, RingBack);
        var steps = (int)MathF.Ceiling((front - RingBack) * height / PieceAlong);
        for (var i = 0; i < steps; i++)
        {
            var y = RingBack + (front - RingBack) * (i + 0.5f) / steps;
            pieces.Add(new Placed(RingAlong[tier], RingLeft, y, false));
            pieces.Add(new Placed(RingAlong[tier], RingRight, y, false));
        }
        Across(RingLeft, 0.5f - gate, front);
        Across(0.5f + gate, RingRight, front);

        foreach (var (x, y) in new[] { (RingLeft, RingBack), (RingRight, RingBack), (RingLeft, front), (RingRight, front) })
            pieces.Add(new Placed(RingPost[tier], x, y, false));
        return pieces.OrderBy(piece => piece.Y).ToList();
    }

    public static int HouseCount(int totalLevels) => Math.Min(HouseSpots.Count, totalLevels / LevelsPerHouse);

    /// <summary>Everything standing on the ground apart from the buildings. Growing only ever adds to the list.</summary>
    public static List<Placed> Scenery(int seed, int totalLevels, int farmStage)
    {
        var random = new Random(seed);
        var taken = new List<(float X, float Y)>(HouseSpots);
        taken.AddRange(CropSpots);
        var result = new List<Placed>();

        foreach (var spot in Scatter(random, taken, 9))
            result.Add(new Placed(Pick(random, Trees), spot.X, spot.Y, random.Next(2) == 0));
        foreach (var spot in Scatter(random, taken, 8))
            result.Add(new Placed(Pick(random, Ground), spot.X, spot.Y, random.Next(2) == 0));

        // Decided before the level is looked at, so a village's extras and houses are fixed from the start.
        var extras = Scatter(random, taken, 10).Select(spot => new Placed(Pick(random, Extras), spot.X, spot.Y, random.Next(2) == 0)).ToList();
        var houses = HouseSpots.Select(spot => new Placed(Pick(random, Houses), spot.X, spot.Y, random.Next(2) == 0)).ToList();

        result.AddRange(houses.Take(HouseCount(totalLevels)));
        result.AddRange(extras.Take(totalLevels / LevelsPerExtra));
        for (var i = 0; i < Math.Min(farmStage, CropSpots.Count); i++)
            result.Add(new Placed(Crops[farmStage == StageCount ? 1 : 0], CropSpots[i].X, CropSpots[i].Y, false));
        return result;
    }

    /// <summary>True when a spot is clear of every building's tile and every path, measured at the smallest view.</summary>
    public static bool IsClear(float x, float y)
    {
        var point = (X: x * SmallestWidth, Y: y * SmallestHeight);
        var centre = Pixels(Plots[BuildingType.MainBuilding]);
        foreach (var (building, plot) in Plots)
        {
            var at = Pixels(plot);
            if (Math.Abs(point.X - at.X) < TileSize / 2 && Math.Abs(point.Y - at.Y) < TileSize / 2)
                return false;
            if (building != BuildingType.MainBuilding && DistanceToLine(point, centre, at) < PathClearance)
                return false;
        }
        return true;
    }

    private static List<(float X, float Y)> Scatter(Random random, List<(float X, float Y)> taken, int count)
    {
        var found = new List<(float X, float Y)>();
        for (var i = 0; i < Attempts && found.Count < count; i++)
        {
            var spot = (X: 0.07f + (float)random.NextDouble() * 0.86f, Y: 0.16f + (float)random.NextDouble() * 0.7f);
            if (!IsClear(spot.X, spot.Y) || taken.Any(other => Distance(spot, other) < SceneryGap))
                continue;
            taken.Add(spot);
            found.Add(spot);
        }
        return found;
    }

    private static string Pick(Random random, string[] from) => from[random.Next(from.Length)];

    private static (float X, float Y) Pixels((float X, float Y) plot) => (plot.X * SmallestWidth, plot.Y * SmallestHeight);

    private static float Distance((float X, float Y) a, (float X, float Y) b)
    {
        var (dx, dy) = ((a.X - b.X) * SmallestWidth, (a.Y - b.Y) * SmallestHeight);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static float DistanceToLine((float X, float Y) point, (float X, float Y) from, (float X, float Y) to)
    {
        var (ax, ay) = (to.X - from.X, to.Y - from.Y);
        var along = Math.Clamp(((point.X - from.X) * ax + (point.Y - from.Y) * ay) / (ax * ax + ay * ay), 0, 1);
        var (dx, dy) = (point.X - from.X - ax * along, point.Y - from.Y - ay * along);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>0 is an empty plot. Otherwise a third of the levels each way: for 10 levels, 1-3, 4-7 and 8-10.</summary>
    public static int Stage(int level, int maxLevel)
    {
        if (level <= 0)
            return 0;

        var third = Math.Max(1, maxLevel / StageCount);
        return level <= third ? 1 : level > maxLevel - third ? StageCount : 2;
    }

    /// <summary>The same for a village every time it is drawn, and separate from the match's own random numbers.</summary>
    public static int Seed(ulong matchSeed, int villageId) => unchecked((int)(matchSeed & 0x7FFFFFFF) ^ (villageId * 7919));
}
