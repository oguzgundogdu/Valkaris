using System.Collections.Generic;
using Godot;
using Valkaris.Core.Hosting;

namespace Valkaris.Presentation;

/// <summary>Brief marks over the village markers: a flash and crossed swords for a battle seen, a small mark for news of one.</summary>
public partial class ClashLayer : Control
{
    private const double ClashSeconds = 1.5;
    private const double NoticeSeconds = 8;
    private const float ClashRadius = 26;
    private const float NoticeRadius = 11;
    private static readonly Color Flash = new(1f, 0.82f, 0.45f);

    private readonly List<(Vector2 Position, float Age)> _clashes = new();
    private readonly List<Vector2> _notices = new();
    private float _screenScale = 1;

    public int ClashesShowing => _clashes.Count;
    public int NoticesShowing => _notices.Count;

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
        foreach (var position in _notices)
        {
            var radius = NoticeRadius * _screenScale;
            DrawCircle(position, radius, new Color(0.08f, 0.07f, 0.05f, 0.8f));
            DrawTextureRect(swords, new Rect2(position - Vector2.One * radius, Vector2.One * radius * 2), tile: false);
        }

        foreach (var (position, age) in _clashes)
        {
            // Stays strong for the first half, then fades.
            var fade = Mathf.Min(1, (1 - age) * 2);
            var radius = ClashRadius * _screenScale;
            DrawCircle(position, radius * (0.7f + age * 0.6f), new Color(Flash, 0.5f * fade));
            DrawTextureRect(swords, new Rect2(position - Vector2.One * radius, Vector2.One * radius * 2), tile: false, new Color(1, 1, 1, fade));
        }
    }
}
