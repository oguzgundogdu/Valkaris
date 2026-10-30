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

    private static readonly Color Surround = new(0.09f, 0.14f, 0.18f);

    private readonly Dictionary<int, VillageMarker> _markers = new();
    private Control _world = null!;
    private MapTerrain _terrain = null!;
    private Control _markerLayer = null!;
    private bool _built;
    private float _zoom = 1;
    private Vector2 _worldSize;
    private bool _userMovedCamera;
    private bool _pressed;
    private bool _dragging;
    private Vector2 _pressPosition;

    public float Zoom => _zoom;
    public Vector2 CameraPosition => _world.Position;

    private float MinZoom => Mathf.Min(Size.X / _worldSize.X, Size.Y / _worldSize.Y);

    public Vector2 WorldToView(Vector2 world) => _world.Position + world * _zoom;

    public Vector2 ViewToWorld(Vector2 view) => (view - _world.Position) / _zoom;

    public static Vector2 VillageWorldPosition(Village village) =>
        (new Vector2(village.Position.X, village.Position.Y) + new Vector2(0.5f, 0.5f)) * TileSize;

    public override void _Ready()
    {
        ClipContents = true;

        _world = new Control { Name = "World", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_world);

        _terrain = new MapTerrain { Name = "Terrain", MouseFilter = MouseFilterEnum.Ignore };
        _world.AddChild(_terrain);

        _markerLayer = new Control { Name = "Markers", MouseFilter = MouseFilterEnum.Ignore };
        _world.AddChild(_markerLayer);

        Resized += OnResized;
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
            marker.Position = VillageWorldPosition(village);
            _markerLayer.AddChild(marker);
            _markers[village.Id] = marker;
        }

        FitToView();
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
            return;
        }

        _pressed = false;
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
}
