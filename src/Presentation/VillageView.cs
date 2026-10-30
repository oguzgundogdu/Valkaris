using System;
using System.Collections.Generic;
using Godot;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>The village ground: a drawn placeholder landscape with one tile per building.</summary>
public partial class VillageView : Control
{
    private static readonly PackedScene TileScene = GD.Load<PackedScene>("res://scenes/BuildingTile.tscn");
    private static readonly Color Ground = new(0.216f, 0.251f, 0.165f);
    private static readonly Color GroundDark = new(0.204f, 0.239f, 0.157f);
    private static readonly Color PathColor = new(0.373f, 0.306f, 0.216f);

    // Tile centres as fractions of the ground, so the layout follows the window size.
    private static readonly Dictionary<BuildingType, Vector2> Layout = new()
    {
        [BuildingType.MainBuilding] = new(0.50f, 0.27f),
        [BuildingType.Woodcutter] = new(0.17f, 0.27f),
        [BuildingType.Quarry] = new(0.83f, 0.27f),
        [BuildingType.Farm] = new(0.125f, 0.74f),
        [BuildingType.Warehouse] = new(0.375f, 0.74f),
        [BuildingType.Barracks] = new(0.625f, 0.74f),
        [BuildingType.Wall] = new(0.875f, 0.74f),
    };

    private readonly Dictionary<BuildingType, BuildingTile> _tiles = new();

    public event Action<BuildingType>? BuildingSelected;

    public override void _Ready()
    {
        foreach (var building in Enum.GetValues<BuildingType>())
        {
            var tile = TileScene.Instantiate<BuildingTile>();
            tile.Name = building.ToString();
            tile.Init(building);
            tile.Pressed += () => BuildingSelected?.Invoke(building);
            AddChild(tile);
            _tiles[building] = tile;
        }

        Resized += PlaceTiles;
        PlaceTiles();
    }

    public void Refresh(Village village, double now, BuildingType selected)
    {
        foreach (var (building, tile) in _tiles)
            tile.Refresh(village, now, building == selected);
    }

    private void PlaceTiles()
    {
        foreach (var (building, tile) in _tiles)
            tile.Position = Layout[building] * Size - tile.Size / 2;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var ground = new StyleBoxFlat { BgColor = Ground };
        ground.SetCornerRadiusAll(12);
        DrawStyleBox(ground, new Rect2(Vector2.Zero, Size));

        // Fixed seed: the same scattered tufts every time, not a flicker on resize.
        var random = new RandomNumberGenerator { Seed = 7 };
        for (var i = 0; i < 90; i++)
        {
            var spot = new Vector2(random.Randf(), random.Randf()) * Size;
            DrawCircle(spot, random.RandfRange(3, 11), GroundDark);
        }

        var centre = Layout[BuildingType.MainBuilding] * Size;
        foreach (var (building, position) in Layout)
        {
            if (building != BuildingType.MainBuilding)
                DrawLine(centre, position * Size, PathColor, 12, antialiased: true);
        }
    }
}
