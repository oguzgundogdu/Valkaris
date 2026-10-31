using System;
using System.Collections.Generic;
using Godot;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>Paints a village's ground once, in the world map's colours, with soft paths from the Main Building to every plot.</summary>
public static class VillageGround
{
    private const int PixelsPerPoint = 3;
    private const float PathWidth = 5;
    private const float PathEdge = 12;
    private const int MostCached = 32;

    private static readonly Color Dirt = MapTerrain.Hills.Lerp(MapTerrain.Sand, 0.25f).Darkened(0.12f);
    private static readonly Dictionary<(int Seed, int Width, int Height), ImageTexture> Cache = new();

    /// <summary>How long the last paint took, for the frame report.</summary>
    public static double LastPaintMilliseconds { get; private set; }

    public static ImageTexture For(int seed, Vector2 size)
    {
        var width = Math.Max(1, (int)size.X / PixelsPerPoint);
        var height = Math.Max(1, (int)size.Y / PixelsPerPoint);
        if (Cache.TryGetValue((seed, width, height), out var texture))
            return texture;

        if (Cache.Count >= MostCached)
            Cache.Clear();

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        texture = ImageTexture.CreateFromImage(Paint(seed, width, height));
        LastPaintMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Cache[(seed, width, height)] = texture;
        return texture;
    }

    private static Image Paint(int seed, int width, int height)
    {
        var random = new Random(seed);
        var large = MapTerrain.Grain(width / 40 + 2, height / 40 + 2, random);
        var fine = MapTerrain.Grain(width / 6 + 2, height / 6 + 2, random);

        var centre = PlotPoint(BuildingType.MainBuilding, width, height);
        var paths = new List<(Vector2 Along, float Length)>();
        foreach (var building in VillageArt.Plots.Keys)
        {
            if (building != BuildingType.MainBuilding)
            {
                var end = PlotPoint(building, width, height);
                paths.Add((centre.DirectionTo(end), centre.DistanceTo(end)));
            }
        }

        var bytes = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var point = new Vector2(x, y);
                var woods = MapTerrain.Sample(large, x / 20f, y / 20f);
                var shade = 0.96f + (MapTerrain.Sample(fine, x / 3f, y / 3f) - 0.5f) * 0.14f + (float)(random.NextDouble() - 0.5) * 0.02f;
                var colour = MapTerrain.Grass.Lerp(MapTerrain.Forest, Mathf.Clamp((woods - 0.42f) * 2.2f, 0, 0.75f));

                var wander = (MapTerrain.Sample(fine, x / 10f + 7, y / 10f + 3) - 0.5f) * PathEdge / PixelsPerPoint;
                var path = Mathf.Clamp((PathWidth + wander - DistanceToPaths(point, centre, paths)) * PixelsPerPoint / PathEdge, 0, 1);
                colour = colour.Lerp(Dirt, path * 0.92f);

                var at = (y * width + x) * 3;
                bytes[at] = (byte)(Mathf.Clamp(colour.R * shade, 0, 1) * 255);
                bytes[at + 1] = (byte)(Mathf.Clamp(colour.G * shade, 0, 1) * 255);
                bytes[at + 2] = (byte)(Mathf.Clamp(colour.B * shade, 0, 1) * 255);
            }
        }
        return Image.CreateFromData(width, height, false, Image.Format.Rgb8, bytes);
    }

    private static Vector2 PlotPoint(BuildingType building, int width, int height)
    {
        var (x, y) = VillageArt.Plots[building];
        return new Vector2(x * width, y * height);
    }

    private static float DistanceToPaths(Vector2 point, Vector2 centre, List<(Vector2 Along, float Length)> paths)
    {
        var nearest = float.MaxValue;
        var offset = point - centre;
        foreach (var (along, length) in paths)
        {
            var closest = along * Mathf.Clamp(offset.Dot(along), 0, length);
            nearest = Math.Min(nearest, (offset - closest).LengthSquared());
        }
        return Mathf.Sqrt(nearest);
    }
}
