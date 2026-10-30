using System.Collections.Generic;
using System.Linq;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>Routes and moving markers for the local player's movements and anything heading for their villages.</summary>
public partial class MovementLayer : Control
{
    private const float LineWidth = 2.5f;
    private const float DashLength = 9;
    private const float MarkerRadius = 13;

    private static readonly Color Own = new(0.851f, 0.643f, 0.255f);
    private static readonly Color Hostile = new(0.90f, 0.30f, 0.25f);
    private static readonly Color Returning = new(0.72f, 0.70f, 0.64f);

    public sealed record Entry(int MovementId, Vector2 From, Vector2 To, Vector2 Position, Color Colour, string IconPath, string Description);

    private readonly List<Entry> _entries = new();
    private float _screenScale = 1;

    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>Where a movement is along its route at a given time.</summary>
    public static Vector2 PositionAt(Vector2 from, Vector2 to, Movement movement, double now)
    {
        var progress = (now - movement.DepartureTime) / (movement.ArrivalTime - movement.DepartureTime);
        return from.Lerp(to, Mathf.Clamp((float)progress, 0, 1));
    }

    public void Refresh(IGameSession session, float zoom)
    {
        var hadEntries = _entries.Count > 0;
        _entries.Clear();
        _screenScale = 1 / zoom;

        var now = session.State.ElapsedTime;
        foreach (var movement in session.State.Movements.Where(m => UiContent.IsVisibleMovement(session, m)))
        {
            var from = WorldMapView.VillageWorldPosition(session.State.Villages[movement.FromVillageId]);
            var to = movement.ToVillageId is { } target
                ? WorldMapView.VillageWorldPosition(session.State.Villages[target])
                : WorldMapView.TileWorldPosition(movement.TargetTile!.Value);
            var colour = movement.OwnerId != session.LocalPlayerId ? Hostile
                : movement.Type == MovementType.Return ? Returning
                : Own;
            var slowest = movement.Troops.Keys.OrderBy(u => session.Data.Units.Units[u].Speed).First();
            var description = $"{UiContent.DescribeMovement(session, movement)}  ·  {Format.Time(movement.ArrivalTime - now)}";

            _entries.Add(new Entry(movement.Id, from, to, PositionAt(from, to, movement, now), colour, UiContent.Units[slowest].IconPath, description));
        }

        if (hadEntries || _entries.Count > 0)
            QueueRedraw();
    }

    /// <summary>The marker within reach of a world position, or null.</summary>
    public Entry? EntryAt(Vector2 world, float zoom)
    {
        var reach = MarkerRadius * 1.5f / zoom;
        return _entries
            .Where(e => e.Position.DistanceTo(world) <= reach)
            .OrderBy(e => e.Position.DistanceTo(world))
            .FirstOrDefault();
    }

    public override void _Draw()
    {
        foreach (var entry in _entries)
            DrawDashedLine(entry.From, entry.To, new Color(entry.Colour, 0.8f), LineWidth * _screenScale, DashLength * _screenScale, aligned: false, antialiased: true);

        foreach (var entry in _entries)
        {
            var radius = MarkerRadius * _screenScale;
            DrawCircle(entry.Position, radius + 2 * _screenScale, new Color(0.08f, 0.07f, 0.05f, 0.9f));
            DrawCircle(entry.Position, radius, entry.Colour);

            var icon = GD.Load<Texture2D>(entry.IconPath);
            var size = Vector2.One * radius * 1.5f;
            DrawTextureRect(icon, new Rect2(entry.Position - size / 2, size), tile: false);
        }
    }
}
