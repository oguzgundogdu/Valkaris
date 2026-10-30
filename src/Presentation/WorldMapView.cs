using System.Collections.Generic;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>The bird's-eye view of the map: terrain, villages and what is moving between them.</summary>
public partial class WorldMapView : Control
{
    public const float TileSize = 64;

    private static readonly Color Surround = new(0.09f, 0.14f, 0.18f);

    private readonly Dictionary<int, VillageMarker> _markers = new();
    private Control _world = null!;
    private MapTerrain _terrain = null!;
    private Control _markerLayer = null!;
    private bool _built;
    private float _zoom = 1;
    private Vector2 _worldSize;

    public override void _Ready()
    {
        ClipContents = true;

        _world = new Control { Name = "World", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_world);

        _terrain = new MapTerrain { Name = "Terrain", MouseFilter = MouseFilterEnum.Ignore };
        _world.AddChild(_terrain);

        _markerLayer = new Control { Name = "Markers", MouseFilter = MouseFilterEnum.Ignore };
        _world.AddChild(_markerLayer);

        Resized += FitToView;
    }

    public override void _Draw()
    {
        var surround = new StyleBoxFlat { BgColor = Surround };
        surround.SetCornerRadiusAll(12);
        DrawStyleBox(surround, new Rect2(Vector2.Zero, Size));
    }

    public void Refresh(IGameSession session)
    {
        if (!_built)
            Build(session);

        foreach (var (id, marker) in _markers)
            marker.Refresh(session, session.State.Villages[id], selected: false);
    }

    private void Build(IGameSession session)
    {
        _built = true;
        var state = session.State;
        _worldSize = new Vector2(state.Map.Width, state.Map.Height) * TileSize;
        _terrain.Build(state);

        foreach (var village in state.Villages.Values)
        {
            var marker = new VillageMarker { Name = $"Village{village.Id}", VillageId = village.Id };
            marker.Position = (new Vector2(village.Position.X, village.Position.Y) + new Vector2(0.5f, 0.5f)) * TileSize;
            _markerLayer.AddChild(marker);
            _markers[village.Id] = marker;
        }

        FitToView();
    }

    private void FitToView()
    {
        if (!_built || Size.X <= 0 || Size.Y <= 0)
            return;

        _zoom = Mathf.Min(Size.X / _worldSize.X, Size.Y / _worldSize.Y);
        _world.Position = (Size - _worldSize * _zoom) / 2;
        ApplyZoom();
    }

    /// <summary>Markers keep their size on screen, shrinking a little when zoomed far out so names don't collide.</summary>
    private float MarkerScale => Mathf.Clamp(_zoom * 1.6f, 0.72f, 1);

    private void ApplyZoom()
    {
        _world.Scale = new Vector2(_zoom, _zoom);
        foreach (var marker in _markers.Values)
            marker.Scale = Vector2.One * MarkerScale / _zoom;
    }
}
