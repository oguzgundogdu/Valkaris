using System.Collections.Generic;
using Godot;
using Valkaris.Core.Hosting;

namespace Valkaris.Presentation;

/// <summary>Brief marks over the village markers: a flash and crossed swords for a battle seen, a small mark for news of one.</summary>
public partial class ClashLayer : Control
{
    private const double ClashSeconds = 2;
    private const double NoticeSeconds = 15;
    private const float ClashRadius = 26;
    private const float NoticeRadius = 11;
    private const float NoticePulseRadius = 5;
    private const float NoticePulsePerSecond = 0.7f;
    private const float OutlineWidth = 1.5f;
    private static readonly Color Flash = new(1f, 0.82f, 0.45f);
    private static readonly Color Dark = new(0.08f, 0.07f, 0.05f);
    private static readonly Vector2[] OutlineSides = { new(-1, 0), new(1, 0), new(0, -1), new(0, 1), new(-0.7f, -0.7f), new(0.7f, -0.7f), new(-0.7f, 0.7f), new(0.7f, 0.7f) };

    private readonly List<(Vector2 Position, float Age)> _clashes = new();
    private readonly List<Vector2> _notices = new();
    private float _screenScale = 1;

    public int ClashesShowing => _clashes.Count;
    public int NoticesShowing => _notices.Count;

    /// <summary>The news mark's width in screen pixels, whatever the map's zoom.</summary>
    public float NoticeWidthOnScreen => NoticeRadius * 2 * _screenScale * GetGlobalTransform().Scale.X;

    public void Refresh(IGameSession session, float zoom)
    {
        var hadMarks = _clashes.Count + _notices.Count > 0;
        _clashes.Clear();
        _notices.Clear();
        _screenScale = 1 / zoom;

        var now = session.State.ElapsedTime;
        foreach (var clash in session.Clashes(now - ClashSeconds))
            _clashes.Add((WorldMapView.VillageWorldPosition(session.State.Villages[clash.VillageId]), (float)((now - clash.Time) / ClashSeconds)));

        var notices = session.PublicNotices;
        for (var i = notices.Count - 1; i >= 0 && now - notices[i].Time < NoticeSeconds; i--)
            _notices.Add(WorldMapView.VillageWorldPosition(session.State.Villages[notices[i].TargetVillageId]));

        if (hadMarks || _clashes.Count + _notices.Count > 0)
            QueueRedraw();
    }

    public override void _Draw()
    {
        var swords = GD.Load<Texture2D>(MovementLayer.SwordsIconPath);

        // Keeps time with the clock, so game speed and pausing leave it gentle.
        var pulse = (Mathf.Sin(Time.GetTicksMsec() / 1000f * Mathf.Tau * NoticePulsePerSecond) + 1) / 2;
        foreach (var position in _notices)
        {
            var radius = NoticeRadius * _screenScale;
            DrawCircle(position, radius + NoticePulseRadius * _screenScale * pulse, new Color(Flash, 0.45f * (1 - pulse)));
            DrawCircle(position, radius, new Color(Dark, 0.8f));
            DrawTextureRect(swords, new Rect2(position - Vector2.One * radius, Vector2.One * radius * 2), tile: false);
        }

        foreach (var (position, age) in _clashes)
        {
            // Stays strong for the first half, then fades.
            var fade = Mathf.Min(1, (1 - age) * 2);
            var radius = ClashRadius * _screenScale;
            DrawCircle(position, radius * (0.7f + age * 0.6f), new Color(Flash, 0.5f * fade));
            var rect = new Rect2(position - Vector2.One * radius, Vector2.One * radius * 2);

            // The dark copies around the swords keep them readable on light ground.
            foreach (var side in OutlineSides)
                DrawTextureRect(swords, new Rect2(rect.Position + side * OutlineWidth * _screenScale, rect.Size), tile: false, new Color(Dark, fade));
            DrawTextureRect(swords, rect, tile: false, new Color(1, 1, 1, fade));
        }
    }
}
