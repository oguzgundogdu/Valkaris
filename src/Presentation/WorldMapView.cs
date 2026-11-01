using System;
using System.Collections.Generic;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>The bird's-eye view of the map: terrain, villages and what is moving between them.</summary>
public partial class WorldMapView : Control
{
    public const float TileSize = 64;

    private const float MaxZoomOverFit = 2;
    private const float ZoomButtonSize = 40;
    private const float ZoomButtonInset = 10;
    private const float ZoomStep = 1.15f;
    private const float DragThreshold = 4;
    private const float PopupGap = 44;
    private static readonly Vector2 MarkerHalfSize = new(60, 60);

    private static readonly Color Sea = new(0.173f, 0.314f, 0.416f);
    private static readonly Color Shore = new(0.72f, 0.64f, 0.47f);
    private const float ShoreWidth = 3;

    private readonly Dictionary<int, VillageMarker> _markers = new();
    private Control _world = null!;
    private MapTerrain _terrain = null!;
    private Control _markerLayer = null!;
    private MovementLayer _movements = null!;
    private ClashLayer _clashes = null!;
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
    private (float Zoom, Vector2 Position)? _beforeWholeMap;
    private readonly HoverTimer<HoverTarget> _hover = new();

    /// <summary>What a hover card can be about: a village, or land a Settler could go to.</summary>
    private readonly record struct HoverTarget(int? VillageId, TileCoord? Tile);

    public event Action<int, MovementType>? SendRequested;
    public event Action<int>? EnterRequested;
    public event Action<TileCoord>? FoundRequested;
    public event Action<int>? ReinforceRequested;
    public event Action<int>? SendResourcesRequested;

    public int? SelectedVillageId => _selectedId;

    /// <summary>The village the keyboard shortcuts act on: under the cursor, or on the open card.</summary>
    public int? HoveredVillageId => _hoverPoint is { } point && VillageAt(point) is { } under ? under : CardVillageId;

    public int? CardVillageId => _selectedId ?? _hover.Shown?.VillageId;

    /// <summary>Lets the UI test stand in for a cursor resting on the card, which a headless run can't do.</summary>
    public bool TestCursorOnCard { get; set; }

    /// <summary>A village's marker with its name, in window coordinates: what a popup must not cover.</summary>
    public Rect2 VillageScreenRect(int villageId)
    {
        var centre = GlobalPosition + WorldToView(VillageWorldPosition(_session!.State.Villages[villageId]));
        return new Rect2(centre - MarkerHalfSize, MarkerHalfSize * 2);
    }
    public TileCoord? SelectedTile => _selectedTile;
    public float Zoom => _zoom;
    public Vector2 CameraPosition => _world.Position;

    private float MinZoom => Mathf.Min(Size.X / _worldSize.X, Size.Y / _worldSize.Y);

    /// <summary>From the whole map to twice as close, at any window size.</summary>
    public float MaxZoom => MinZoom * MaxZoomOverFit;
    public float FitZoom => MinZoom;

    public void ZoomIn() => ZoomAround(Size / 2, ZoomStep);

    public void ZoomOut() => ZoomAround(Size / 2, 1 / ZoomStep);

    public Vector2 WorldToView(Vector2 world) => _world.Position + world * _zoom;

    public Vector2 ViewToWorld(Vector2 view) => (view - _world.Position) / _zoom;

    public static Vector2 VillageWorldPosition(Village village) => TileWorldPosition(village.Position);

    public static Vector2 TileWorldPosition(TileCoord tile) =>
        (new Vector2(tile.X, tile.Y) + new Vector2(0.5f, 0.5f)) * TileSize;

    private VBoxContainer BuildZoomButtons()
    {
        var box = new VBoxContainer { Name = "Zoom" };
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.Minsize);
        box.GrowHorizontal = GrowDirection.Begin;
        box.OffsetRight = -ZoomButtonInset;
        box.OffsetTop = ZoomButtonInset;

        Button Make(string name, string symbol, string tip, System.Action act)
        {
            var button = new Button { Name = name, Text = symbol, TooltipText = L.T(tip), ThemeTypeVariation = "SecondaryButton", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(ZoomButtonSize, ZoomButtonSize) };
            button.Pressed += act;
            box.AddChild(button);
            return button;
        }

        Make("In", "+", "map.zoom_in_tip", ZoomIn);
        Make("Out", "\u2212", "map.zoom_out_tip", ZoomOut);
        return box;
    }

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

        _clashes = new ClashLayer { Name = "Clashes", MouseFilter = MouseFilterEnum.Ignore };
        _world.AddChild(_clashes);

        AddChild(BuildZoomButtons());

        _popup = GD.Load<PackedScene>("res://scenes/VillagePopup.tscn").Instantiate<VillagePopup>();
        _popup.Name = "Popup";
        AddChild(_popup);
        _hoverLabel = new Label { Name = "HoverLabel", ThemeTypeVariation = "StrongLabel", MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _hoverLabel.AddThemeConstantOverride("outline_size", 6);
        _hoverLabel.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.07f, 0.05f, 0.9f));
        AddChild(_hoverLabel);

        _popup.SendRequested += (id, mission) => SendRequested?.Invoke(id, mission);
        _popup.EnterRequested += id => EnterRequested?.Invoke(id);
        _popup.ReinforceRequested += id => ReinforceRequested?.Invoke(id);
        _popup.SendResourcesRequested += id => SendResourcesRequested?.Invoke(id);
        _popup.FoundRequested += tile =>
        {
            ClearSelection();
            FoundRequested?.Invoke(tile);
        };

        Resized += OnResized;
    }

    /// <summary>The sea beyond the map's edges, which shows when the whole map is in view.</summary>
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Sea);
        if (_built)
            DrawShore();
    }

    /// <summary>Sand only where land reaches the map's edge. Water there runs straight into the sea around it.</summary>
    private void DrawShore()
    {
        var map = _session!.State.Map;
        var tile = TileSize * _zoom;
        for (var x = 0; x < map.Width; x++)
        {
            ShoreAt(new TileCoord(x, 0), new Rect2(_world.Position + new Vector2(x * tile, -ShoreWidth), tile, ShoreWidth));
            ShoreAt(new TileCoord(x, map.Height - 1), new Rect2(_world.Position + new Vector2(x * tile, map.Height * tile), tile, ShoreWidth));
        }
        for (var y = 0; y < map.Height; y++)
        {
            ShoreAt(new TileCoord(0, y), new Rect2(_world.Position + new Vector2(-ShoreWidth, y * tile), ShoreWidth, tile));
            ShoreAt(new TileCoord(map.Width - 1, y), new Rect2(_world.Position + new Vector2(map.Width * tile, y * tile), ShoreWidth, tile));
        }

        void ShoreAt(TileCoord coord, Rect2 rect)
        {
            if (map.TileAt(coord).Terrain != Terrain.Water)
                DrawRect(rect, Shore);
        }
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
        _clashes.Refresh(session, _zoom);
        RefreshHoverLabel();

        if (currentVillage is null || currentVillage.TroopCount(UnitType.Settler) == 0)
            _selectedTile = null;

        UpdateHover();
        var cardVillage = _selectedId ?? (_selectedTile is null ? _hover.Shown?.VillageId : null);
        var cardTile = _selectedTile ?? (_selectedId is null ? _hover.Shown?.Tile : null);
        _popup.Visible = cardVillage is not null || (cardTile is not null && currentVillage is not null);
        if (cardVillage is { } shown)
        {
            var village = session.State.Villages[shown];
            _popup.Refresh(session, village, currentVillage);
            PlacePopup(VillageWorldPosition(village));
        }
        else if (cardTile is { } tile && currentVillage is not null)
        {
            _popup.RefreshLand(session, tile, currentVillage);
            PlacePopup(TileWorldPosition(tile));
        }
    }

    private void UpdateHover()
    {
        var overCard = TestCursorOnCard || (_popup.Visible && _popup.GetGlobalRect().HasPoint(GetGlobalMousePosition()));
        HoverTarget? under = null;
        if (!_pressed && !overCard && _hoverPoint is { } point)
        {
            if (VillageAt(point) is { } id)
                under = new HoverTarget(id, null);
            else if (LandForSettlerAt(point) is { } tile)
                under = new HoverTarget(null, tile);
        }
        _hover.Update(under, overCard, GetProcessDeltaTime());
    }

    public void ClearSelection()
    {
        _selectedId = null;
        _selectedTile = null;
        _hover.Clear();
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
        FillView(session);
    }

    /// <summary>How the map opens: filling the view, centred on the capital.</summary>
    private void FillView(IGameSession session)
    {
        if (!_built || Size.X <= 0 || Size.Y <= 0)
            return;

        _userMovedCamera = false;
        _zoom = Mathf.Clamp(Mathf.Max(Size.X / _worldSize.X, Size.Y / _worldSize.Y), MinZoom, MaxZoom);
        ApplyZoom();
        var player = session.State.Players[session.LocalPlayerId];
        var home = player.CapitalVillageId is { } capital ? capital : _currentVillage?.Id;
        _world.Position = home is { } id
            ? Size / 2 - VillageWorldPosition(session.State.Villages[id]) * _zoom
            : (Size - _worldSize * _zoom) / 2;
        ClampCamera();
        QueueRedraw();
    }

    /// <summary>Space: shows the whole map, and goes back to the view before.</summary>
    public void ToggleWholeMap()
    {
        if (!_built)
            return;

        if (_beforeWholeMap is { } before)
        {
            _beforeWholeMap = null;
            _zoom = before.Zoom;
            _world.Position = before.Position;
            ApplyZoom();
            QueueRedraw();
            return;
        }

        _beforeWholeMap = (_zoom, _world.Position);
        var moved = _userMovedCamera;
        FitToView();
        _userMovedCamera = moved;
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
        _beforeWholeMap = null;
        if (!_userMovedCamera && _session is not null)
        {
            FillView(_session);
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

        _userMovedCamera = false;
        _zoom = MinZoom;
        _world.Position = (Size - _worldSize * _zoom) / 2;
        ApplyZoom();
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit)
            _hoverPoint = null;
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

        var hovered = VillageAt(viewPoint);
        var overVillage = hovered is not null;
        MouseDefaultCursorShape = hovered switch
        {
            null => CursorShape.Arrow,
            { } id when _session!.State.Villages[id].OwnerId != _session.LocalPlayerId && !_session.State.Players[_session.LocalPlayerId].IsEliminated => CursorShape.Cross,
            _ => CursorShape.PointingHand,
        };

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
        QueueRedraw();
    }

    private static float ClampAxis(float position, float scaled, float view) =>
        scaled <= view ? (view - scaled) / 2 : Mathf.Clamp(position, view - scaled, 0);

    /// <summary>Markers keep their size on screen, shrinking a little when zoomed far out so names don't collide.</summary>
    private float MarkerScale => Mathf.Clamp(_zoom * 1.6f, 0.72f, 1);

    private void ApplyZoom()
    {
        QueueRedraw();
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
