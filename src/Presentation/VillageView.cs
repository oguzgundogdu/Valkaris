using System;
using System.Collections.Generic;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>The village ground, with one tile per building.</summary>
public partial class VillageView : Control
{
    private static readonly PackedScene TileScene = GD.Load<PackedScene>("res://scenes/BuildingTile.tscn");
    private readonly Dictionary<BuildingType, BuildingTile> _tiles = new();
    private int _seed;

    /// <summary>The ground in use, so tests can see it stays the same.</summary>
    public Texture2D Ground => VillageGround.For(_seed, Size);

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

    public void Refresh(IGameSession session, Village village, BuildingType selected)
    {
        var seed = VillageArt.Seed(session.State.Settings.Seed, village.Id);
        if (seed != _seed)
        {
            _seed = seed;
            QueueRedraw();
        }

        var now = session.State.ElapsedTime;
        foreach (var (building, tile) in _tiles)
            tile.Refresh(village, now, building == selected);
    }

    public void Pulse(BuildingType building) => _tiles[building].Pulse();

    private void PlaceTiles()
    {
        foreach (var (building, tile) in _tiles)
            tile.Position = new Vector2(VillageArt.Plots[building].X, VillageArt.Plots[building].Y) * Size - tile.Size / 2;
        QueueRedraw();
    }

    public override void _Draw() => DrawTextureRect(Ground, new Rect2(Vector2.Zero, Size), tile: false);
}
