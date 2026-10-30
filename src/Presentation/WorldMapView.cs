using System;
using System.Collections.Generic;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>The bird's-eye view of the map: terrain, villages and what is moving between them.</summary>
public partial class WorldMapView : Control
{
    public const float TileSize = 64;

    private const float MaxZoom = 1.6f;
    private const float ZoomStep = 1.15f;
    private const float DragThreshold = 4;
    private const float PopupGap = 44;

    private static readonly Color Surround = new(0.09f, 0.14f, 0.18f);

    private readonly Dictionary<int, VillageMarker> _markers = new();
    private Control _world = null!;
    private MapTerrain _terrain = null!;
    private Control _markerLayer = null!;
    private MovementLayer _movements = null!;
    private Label _hoverLabel = null!;
    private bool _built;
    private float _zoom = 1;
    private Vector2 _worldSize;
    private bool _userMovedCamera;
    private bool _pressed;
    private bool _dragging;
    private Vector2 _pressPosition;
    private VillagePopup _popup = null!;
    private TileHighlight _highlight = null!;
    private IGameSession? _session;
    private int? _selectedId;
    private TileCoord? _selectedTile;
    private Village? _currentVillage;
    private Vector2? _hoverPoint;

    public event Action<int>? SendRequested;
    public event Action<int>? EnterRequested;
    public event Action<TileCoord>? FoundRequested;
    public event Action<int>? ReinforceRequested;
    public event Action<int>? SendResourcesRequested;

    public int? SelectedVillageId => _selectedId;
    public TileCoord? SelectedTile => _selectedTile;
    public float Zoom => _zoom;
    public Vector2 CameraPosition => _world.Position;

    private float MinZoom => Mathf.Min(Size.X / _worldSize.X, Size.Y / _worldSize.Y);

    public Vector2 WorldToView(Vector2 world) => _world.Position + world * _zoom;

    public Vector2 ViewToWorld(Vector2 view) => (view - _world.Position) / _zoom;

    public static Vector2 VillageWorldPosition(Village village) => TileWorldPosition(village.Position);

    public static Vector2 TileWorldPosition(TileCoord tile) =>
        (new Vector2(tile.X, tile.Y) + new Vector2(0.5f, 0.5f)) * TileSize;

    public override void _Ready()
    {
        ClipContents = true;

        _world = new Control { Name = "World", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_world);

        _terrain = new MapTerrain { Name = "Terrain", MouseFilter = MouseFilterEnum.Ignore };
        _world.AddChild(_terrain);

        _highlight = new TileHighlight { Name = "Highlight", MouseFilter = MouseFilterEnum.Ignore };
        _world.AddChild(_highlight);

        _movements = new MovementLayer { Name = "Movements", MouseFilter = MouseFilterEnum.Ignore };
        _world.AddChild(_movements);

        _markerLayer = new Control { Name = "Markers", MouseFilter = MouseFilterEnum.Ignore };
        _world.AddChild(_markerLayer);

        _popup = GD.Load<PackedScene>("res://scenes/VillagePopup.tscn").Instantiate<VillagePopup>();
        _popup.Name = "Popup";
        AddChild(_popup);
        _hoverLabel = new Label { Name = "HoverLabel", ThemeTypeVariation = "StrongLabel", MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _hoverLabel.AddThemeConstantOverride("outline_size", 6);
        _hoverLabel.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.07f, 0.05f, 0.9f));
        AddChild(_hoverLabel);

        _popup.SendRequested += id => SendRequested?.Invoke(id);
        _popup.EnterRequested += id => EnterRequested?.Invoke(id);
        _popup.ReinforceRequested += id => ReinforceRequested?.Invoke(id);
        _popup.SendResourcesRequested += id => SendResourcesRequested?.Invoke(id);
        _popup.FoundRequested += tile =>
        {
            _selectedTile = null;
            FoundRequested?.Invoke(tile);
        };

        Resized += OnResized;
    }

    public override void _Draw()
    {
        var surround = new StyleBoxFlat { BgColor = Surround };
        surround.SetCornerRadiusAll(12);
        DrawStyleBox(surround, new Rect2(Vector2.Zero, Size));
    }

    public void Refresh(IGameSession session, Village? currentVillage)
    {
        _session = session;
        _currentVillage = currentVillage;
        if (!_built)
            Build(session);

        AddNewMarkers(session);
        foreach (var (id, marker) in _markers)
            marker.Refresh(session, session.State.Villages[id], id == _selectedId, id == currentVillage?.Id);

        _movements.Refresh(session, _zoom);
        RefreshHoverLabel();

        if (currentVillage is null || currentVillage.TroopCount(UnitType.Settler) == 0)
            _selectedTile = null;

        _popup.Visible = _selectedId is not null || _selectedTile is not null;
        if (_selectedId is { } selected)
        {
            var village = session.State.Villages[selected];
            _popup.Refresh(session, village, currentVillage);
            PlacePopup(VillageWorldPosition(village));
        }
        else if (_selectedTile is { } tile)
        {
            _popup.RefreshLand(session, tile, currentVillage!);
            PlacePopup(TileWorldPosition(tile));
        }
    }

    public void ClearSelection()
    {
        _selectedId = null;
        _selectedTile = null;
    }

    /// <summary>Puts a village in the middle of the view, as far as the map's edges allow.</summary>
    public void CentreOn(Village village)
    {
        if (!_built)
            return;

        _userMovedCamera = true;
        _world.Position = Size / 2 - VillageWorldPosition(village) * _zoom;
        ClampCamera();
    }

    private void RefreshHoverLabel()
    {
        var entry = _hoverPoint is { } point ? _movements.EntryAt(ViewToWorld(point), _zoom) : null;
        _hoverLabel.Visible = entry is not null;
        if (entry is null)
            return;

        _hoverLabel.Text = entry.Description;
        _hoverLabel.Size = _hoverLabel.GetCombinedMinimumSize();
        _hoverLabel.Position = new Vector2(
            Mathf.Clamp(_hoverPoint!.Value.X + 14, 0, Mathf.Max(0, Size.X - _hoverLabel.Size.X)),
            Mathf.Clamp(_hoverPoint.Value.Y - 30, 0, Mathf.Max(0, Size.Y - _hoverLabel.Size.Y)));
    }

    /// <summary>Beside the marker, kept inside the view.</summary>
    private void PlacePopup(Vector2 world)
    {
        var anchor = WorldToView(world);
        var size = _popup.GetCombinedMinimumSize();
        var position = anchor + new Vector2(PopupGap, -size.Y / 2);
        if (position.X + size.X > Size.X)
            position.X = anchor.X - PopupGap - size.X;

        _popup.Position = new Vector2(
            Mathf.Clamp(position.X, 0, Mathf.Max(0, Size.X - size.X)),
            Mathf.Clamp(position.Y, 0, Mathf.Max(0, Size.Y - size.Y)));
        _popup.Size = size;
    }

    /// <summary>The marker at a view position, closest first. Markers keep their size on screen, so this works in view space.</summary>
    private int? VillageAt(Vector2 viewPoint)
    {
        if (_session is null)
            return null;

        int? best = null;
        var bestDistance = VillageMarker.HitRadius * MarkerScale;
        foreach (var village in _session.State.Villages.Values)
        {
            var distance = WorldToView(VillageWorldPosition(village)).DistanceTo(viewPoint);
            if (distance <= bestDistance)
            {
                best = village.Id;
                bestDistance = distance;
            }
        }
        return best;
    }

    private void Build(IGameSession session)
    {
        _built = true;
        var state = session.State;
        _worldSize = new Vector2(state.Map.Width, state.Map.Height) * TileSize;
        _terrain.Build(state);

        AddNewMarkers(session);
        FitToView();
    }

    /// <summary>Villages appear during a match when a Settler arrives.</summary>
    private void AddNewMarkers(IGameSession session)
    {
        foreach (var village in session.State.Villages.Values)
        {
            if (_markers.ContainsKey(village.Id))
                continue;

            var marker = new VillageMarker { Name = $"Village{village.Id}", VillageId = village.Id };
            marker.Position = VillageWorldPosition(village);
            marker.Scale = Vector2.One * MarkerScale / _zoom;
            _markerLayer.AddChild(marker);
            _markers[village.Id] = marker;
        }
    }

    private void OnResized()
    {
        if (!_userMovedCamera)
        {
            FitToView();
        }
        else if (_built)
        {
            _zoom = Mathf.Clamp(_zoom, MinZoom, MaxZoom);
            ApplyZoom();
            ClampCamera();
        }
    }

    public void FitToView()
    {
        if (!_built || Size.X <= 0 || Size.Y <= 0)
            return;

        _zoom = MinZoom;
        _world.Position = (Size - _worldSize * _zoom) / 2;
        ApplyZoom();
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } wheel:
                ZoomAround(wheel.Position, ZoomStep);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } wheel:
                ZoomAround(wheel.Position, 1 / ZoomStep);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                HandleLeftButton(button);
                break;
            case InputEventMouseMotion motion when _pressed:
                HandleDrag(motion);
                break;
            case InputEventMouseMotion hover:
                HandleHover(hover.Position);
                break;
            case InputEventPanGesture pan:
                PanBy(-pan.Delta * 12);
                AcceptEvent();
                break;
            case InputEventMagnifyGesture magnify:
                ZoomAround(magnify.Position, magnify.Factor);
                AcceptEvent();
                break;
        }
    }

    private void HandleLeftButton(InputEventMouseButton button)
    {
        if (button.Pressed)
        {
            _pressed = true;
            _dragging = false;
            _pressPosition = button.Position;

            // A double-click on your own village walks into it.
            if (button.DoubleClick && VillageAt(button.Position) is { } id && _session!.State.Villages[id].OwnerId == _session.LocalPlayerId)
            {
                _pressed = false;
                EnterRequested?.Invoke(id);
            }
            return;
        }

        if (_pressed && !_dragging)
        {
            _selectedId = VillageAt(button.Position);
            _selectedTile = _selectedId is null ? LandForSettlerAt(button.Position) : null;
        }
        _pressed = false;
    }

    /// <summary>Empty land only responds to a click when the current village has a Settler to send there.</summary>
    private TileCoord? LandForSettlerAt(Vector2 viewPoint)
    {
        if (_session is null || _currentVillage is null || _currentVillage.TroopCount(UnitType.Settler) == 0)
            return null;

        var tile = (ViewToWorld(viewPoint) / TileSize).Floor();
        var coord = new TileCoord((int)tile.X, (int)tile.Y);
        return _session.State.Map.Contains(coord) ? coord : null;
    }

    private void HandleHover(Vector2 viewPoint)
    {
        if (!_built)
            return;

        _hoverPoint = viewPoint;

        var overVillage = VillageAt(viewPoint) is not null;
        MouseDefaultCursorShape = overVillage ? CursorShape.PointingHand : CursorShape.Arrow;

        var world = ViewToWorld(viewPoint);
        var inside = !overVillage && world.X >= 0 && world.Y >= 0 && world.X < _worldSize.X && world.Y < _worldSize.Y;
        _highlight.Show(inside ? (world / TileSize).Floor() * TileSize : null);
    }

    private void HandleDrag(InputEventMouseMotion motion)
    {
        if (!_dragging && motion.Position.DistanceTo(_pressPosition) < DragThreshold)
            return;

        _dragging = true;
        PanBy(motion.Relative);
    }

    private void PanBy(Vector2 delta)
    {
        if (!_built)
            return;

        _userMovedCamera = true;
        _world.Position += delta;
        ClampCamera();
    }

    private void ZoomAround(Vector2 viewPoint, float factor)
    {
        if (!_built)
            return;

        var worldPoint = ViewToWorld(viewPoint);
        _zoom = Mathf.Clamp(_zoom * factor, MinZoom, MaxZoom);
        _userMovedCamera = true;
        _world.Position = viewPoint - worldPoint * _zoom;
        ApplyZoom();
        ClampCamera();
    }

    /// <summary>The map can't leave the view. An axis smaller than the view stays centred.</summary>
    private void ClampCamera()
    {
        var scaled = _worldSize * _zoom;
        _world.Position = new Vector2(ClampAxis(_world.Position.X, scaled.X, Size.X), ClampAxis(_world.Position.Y, scaled.Y, Size.Y));
    }

    private static float ClampAxis(float position, float scaled, float view) =>
        scaled <= view ? (view - scaled) / 2 : Mathf.Clamp(position, view - scaled, 0);

    /// <summary>Markers keep their size on screen, shrinking a little when zoomed far out so names don't collide.</summary>
    private float MarkerScale => Mathf.Clamp(_zoom * 1.6f, 0.72f, 1);

    private void ApplyZoom()
    {
        _world.Scale = new Vector2(_zoom, _zoom);
        foreach (var marker in _markers.Values)
            marker.Scale = Vector2.One * MarkerScale / _zoom;
    }

    /// <summary>The one place the tile grid shows: a soft square under the cursor.</summary>
    private partial class TileHighlight : Control
    {
        private Vector2? _corner;

        public void Show(Vector2? corner)
        {
            if (corner == _corner)
                return;

            _corner = corner;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_corner is { } corner)
                DrawRect(new Rect2(corner, new Vector2(TileSize, TileSize)), new Color(1, 1, 1, 0.1f));
        }
    }
}
