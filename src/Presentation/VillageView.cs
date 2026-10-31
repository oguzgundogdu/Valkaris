using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>The village ground, with one tile per building.</summary>
public partial class VillageView : Control
{
    private static readonly PackedScene TileScene = GD.Load<PackedScene>("res://scenes/BuildingTile.tscn");
    private readonly Dictionary<BuildingType, BuildingTile> _tiles = new();
    private readonly List<VillageScenery> _layers = new();
    private List<Placed> _scenery = new();
    private (int Seed, int Total, int Farm) _sceneryFor;
    private int _seed;

    /// <summary>How many things stand on the ground, so tests can watch the village grow.</summary>
    public IReadOnlyList<Placed> Scenery => _scenery;

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

        // A layer sits before each tile and one after them all, so scenery sorts between the buildings.
        for (var i = 0; i <= _tiles.Count; i++)
        {
            var layer = new VillageScenery { MouseFilter = MouseFilterEnum.Ignore };
            AddChild(layer);
            _layers.Add(layer);
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

        var total = village.Buildings.Values.Sum();
        var farm = VillageArt.Stage(village.BuildingLevel(BuildingType.Farm), session.Data.Buildings.Buildings[BuildingType.Farm].MaxLevel);
        if ((seed, total, farm) != _sceneryFor)
        {
            _sceneryFor = (seed, total, farm);
            _scenery = VillageArt.Scenery(seed, total, farm);
            SortScenery();
        }

        var now = session.State.ElapsedTime;
        var showPlates = Input.IsKeyPressed(Key.Alt);
        foreach (var (building, tile) in _tiles)
            tile.Refresh(village, now, building == selected, session.Data.Buildings.Buildings[building].MaxLevel, showPlates);
    }

    public void Pulse(BuildingType building) => _tiles[building].Pulse();

    private void PlaceTiles()
    {
        foreach (var (building, tile) in _tiles)
            tile.Position = new Vector2(VillageArt.Plots[building].X, VillageArt.Plots[building].Y) * Size - tile.Size / 2;

        SortScenery();
        QueueRedraw();
    }

    /// <summary>Further down the ground draws on top, so nearer things overlap farther ones.</summary>
    private void SortScenery()
    {
        if (_layers.Count == 0)
            return;

        var order = 0;
        var left = _scenery.OrderBy(item => item.Y).ToList();
        var from = 0;
        var plots = VillageArt.Plots.OrderBy(p => p.Value.Y).ToList();
        for (var i = 0; i <= plots.Count; i++)
        {
            var limit = i < plots.Count ? plots[i].Value.Y + VillageArt.TileGroundBelowCentre / Size.Y : float.MaxValue;
            var count = 0;
            while (from + count < left.Count && left[from + count].Y < limit)
                count++;
            _layers[i].Show(left.GetRange(from, count));
            from += count;

            MoveChild(_layers[i], order++);
            if (i < plots.Count)
                MoveChild(_tiles[plots[i].Key], order++);
        }
    }

    public override void _Draw() => DrawTextureRect(Ground, new Rect2(Vector2.Zero, Size), tile: false);
}
