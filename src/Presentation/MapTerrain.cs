using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>
/// Paints the map once: terrain colours blended into a soft image with irregular shorelines, then scattered
/// trees and hills. Placed from the seed, so it looks the same whenever the same map is drawn.
/// </summary>
public partial class MapTerrain : Control
{
    private const int PixelsPerTile = 16;

    private static readonly Color Grass = new(0.345f, 0.42f, 0.235f);
    private static readonly Color Forest = new(0.235f, 0.33f, 0.185f);
    private static readonly Color Hills = new(0.44f, 0.405f, 0.30f);
    private static readonly Color Sand = new(0.60f, 0.54f, 0.38f);
    private static readonly Color Shallow = new(0.27f, 0.45f, 0.50f);
    private static readonly Color Deep = new(0.17f, 0.31f, 0.41f);

    private static readonly Texture2D TreeTexture = GD.Load<Texture2D>("res://assets/icons/tree.svg");
    private static readonly Texture2D HillTexture = GD.Load<Texture2D>("res://assets/icons/hill.svg");

    private ImageTexture? _ground;
    private readonly List<(Texture2D Texture, Vector2 Position, Vector2 Size)> _decorations = new();

    public void Build(GameState state)
    {
        var map = state.Map;
        Size = new Vector2(map.Width, map.Height) * WorldMapView.TileSize;
        var random = new Random((int)(state.Settings.Seed & 0x7FFFFFFF));

        _ground = ImageTexture.CreateFromImage(PaintGround(map, random));
        PlaceDecorations(map, random);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_ground is null)
            return;

        DrawTextureRect(_ground, new Rect2(Vector2.Zero, Size), tile: false);
        foreach (var (texture, position, size) in _decorations)
            DrawTextureRect(texture, new Rect2(position, size), tile: false);
    }

    private static Image PaintGround(GameMap map, Random random)
    {
        var width = map.Width * PixelsPerTile;
        var height = map.Height * PixelsPerTile;
        var image = Image.CreateEmpty(width, height, useMipmaps: false, Image.Format.Rgb8);
        var grain = Grain(map.Width + 2, map.Height + 2, random);

        for (var py = 0; py < height; py++)
        {
            for (var px = 0; px < width; px++)
            {
                var tx = (px + 0.5f) / PixelsPerTile - 0.5f;
                var ty = (py + 0.5f) / PixelsPerTile - 0.5f;
                image.SetPixel(px, py, ColourAt(map, tx, ty, grain, random));
            }
        }
        return image;
    }

    private static Color ColourAt(GameMap map, float tx, float ty, float[,] grain, Random random)
    {
        var x0 = Mathf.FloorToInt(tx);
        var y0 = Mathf.FloorToInt(ty);
        var u = Mathf.SmoothStep(0, 1, tx - x0);
        var v = Mathf.SmoothStep(0, 1, ty - y0);

        var land = new Color(0, 0, 0);
        var landWeight = 0f;
        var water = 0f;
        foreach (var (dx, dy, weight) in new[] { (0, 0, (1 - u) * (1 - v)), (1, 0, u * (1 - v)), (0, 1, (1 - u) * v), (1, 1, u * v) })
        {
            var terrain = TerrainAt(map, x0 + dx, y0 + dy);
            if (terrain == Terrain.Water)
            {
                water += weight;
            }
            else
            {
                land += TerrainColour(terrain) * weight;
                landWeight += weight;
            }
        }

        // A little noise on the boundary makes the shoreline wander instead of following the tile grid.
        var wobble = (Sample(grain, tx * 1.7f, ty * 1.7f) - 0.5f) * 0.32f;
        var edge = water + wobble;
        var speckle = (float)(random.NextDouble() - 0.5) * 0.018f;

        Color colour;
        if (water > 0 && edge >= 0.5f)
            colour = Shallow.Lerp(Deep, Mathf.Clamp((edge - 0.5f) * 2.4f, 0, 1));
        else if (water > 0 && edge >= 0.36f)
            colour = Sand;
        else
            colour = landWeight > 0 ? land / landWeight : Grass;

        var shade = 1 + (Sample(grain, tx * 0.6f, ty * 0.6f) - 0.5f) * 0.16f + speckle;
        return new Color(colour.R * shade, colour.G * shade, colour.B * shade);
    }

    private static Terrain TerrainAt(GameMap map, int x, int y) =>
        map.TileAt(new TileCoord(Math.Clamp(x, 0, map.Width - 1), Math.Clamp(y, 0, map.Height - 1))).Terrain;

    private static Color TerrainColour(Terrain terrain) => terrain switch
    {
        Terrain.Forest => Forest,
        Terrain.Hills => Hills,
        _ => Grass,
    };

    private static float[,] Grain(int width, int height, Random random)
    {
        var grain = new float[width * 2 + 4, height * 2 + 4];
        for (var y = 0; y < grain.GetLength(1); y++)
        {
            for (var x = 0; x < grain.GetLength(0); x++)
                grain[x, y] = (float)random.NextDouble();
        }
        return grain;
    }

    private static float Sample(float[,] grain, float x, float y)
    {
        x = Mathf.Clamp(x, 0, grain.GetLength(0) - 2);
        y = Mathf.Clamp(y, 0, grain.GetLength(1) - 2);
        var x0 = (int)x;
        var y0 = (int)y;
        var u = Mathf.SmoothStep(0, 1, x - x0);
        var v = Mathf.SmoothStep(0, 1, y - y0);
        var top = Mathf.Lerp(grain[x0, y0], grain[x0 + 1, y0], u);
        var bottom = Mathf.Lerp(grain[x0, y0 + 1], grain[x0 + 1, y0 + 1], u);
        return Mathf.Lerp(top, bottom, v);
    }

    private void PlaceDecorations(GameMap map, Random random)
    {
        _decorations.Clear();
        var tile = WorldMapView.TileSize;

        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var mapTile = map.TileAt(new TileCoord(x, y));
                if (mapTile.VillageId is not null)
                    continue;

                if (mapTile.Terrain == Terrain.Forest)
                    AddScattered(TreeTexture, x, y, random.Next(3, 6), new Vector2(tile * 0.42f, tile * 0.56f), random);
                else if (mapTile.Terrain == Terrain.Hills)
                    AddScattered(HillTexture, x, y, random.Next(1, 3), new Vector2(tile * 0.72f, tile * 0.48f), random);
                else if (mapTile.Terrain == Terrain.Grass && random.NextDouble() < 0.12)
                    AddScattered(TreeTexture, x, y, 1, new Vector2(tile * 0.34f, tile * 0.46f), random);
            }
        }

        // Further down the map draws on top, so nearer things overlap farther ones.
        var sorted = _decorations.OrderBy(d => d.Position.Y + d.Size.Y).ToList();
        _decorations.Clear();
        _decorations.AddRange(sorted);
    }

    private void AddScattered(Texture2D texture, int x, int y, int count, Vector2 size, Random random)
    {
        var tile = WorldMapView.TileSize;
        for (var i = 0; i < count; i++)
        {
            var scale = 0.85f + (float)random.NextDouble() * 0.3f;
            var scaled = size * scale;
            var centre = new Vector2(x + (float)random.NextDouble(), y + (float)random.NextDouble()) * tile;
            _decorations.Add((texture, centre - scaled / 2, scaled));
        }
    }
}
