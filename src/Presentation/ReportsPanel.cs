using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>The local player's battle and scout reports, newest first. Read state is display-only, not game state.</summary>
public partial class ReportsPanel : PanelContainer
{
    private readonly HashSet<int> _read = new();
    private readonly Dictionary<int, Button> _rows = new();
    private VBoxContainer _list = null!;
    private Label _none = null!;

    public event Action<BattleReport>? ReportOpened;
    public event Action<ScoutReport>? ScoutReportOpened;

    public override void _Ready()
    {
        _list = GetNode<VBoxContainer>("%Rows");
        _none = GetNode<Label>("%None");
    }

    public int UnreadCount(IGameSession session) => MyReportIds(session).Count(id => !_read.Contains(id));

    public void MarkAllRead(IGameSession session)
    {
        foreach (var id in MyReportIds(session))
            _read.Add(id);
    }

    public void Refresh(IGameSession session)
    {
        var battles = MyReports(session);
        var scouts = MyScoutReports(session);
        _none.Visible = battles.Count + scouts.Count == 0;

        // Ids run in time order across both kinds, so each new row goes on top of the older ones.
        var rows = battles
            .Select(r => (r.Id, r.Time, Text: UiContent.ReportHeadline(session, r), Bad: !UiContent.LocalWon(session, r), Open: (Action)(() => ReportOpened?.Invoke(r))))
            .Concat(scouts.Select(r => (r.Id, r.Time, Text: UiContent.ScoutHeadline(session, r), Bad: UiContent.LocalLostScouts(session, r), Open: (Action)(() => ScoutReportOpened?.Invoke(r)))))
            .Where(row => !_rows.ContainsKey(row.Id))
            .OrderBy(row => row.Id);

        foreach (var row in rows)
        {
            var button = new Button
            {
                Name = $"Report{row.Id}",
                Text = $"{row.Text}  ·  {Format.Time(row.Time)}",
                ThemeTypeVariation = "SecondaryButton",
                Alignment = HorizontalAlignment.Left,
                FocusMode = FocusModeEnum.None,
            };
            button.AddThemeColorOverride("font_color", row.Bad ? UiContent.Danger : UiContent.Text);
            button.Pressed += row.Open;
            _list.AddChild(button);
            _list.MoveChild(button, 0);
            _rows[row.Id] = button;
        }
    }

    private static IEnumerable<int> MyReportIds(IGameSession session) =>
        MyReports(session).Select(r => r.Id).Concat(MyScoutReports(session).Select(r => r.Id));

    private static List<ScoutReport> MyScoutReports(IGameSession session) => session.State.ScoutReports
        .Where(r => r.VisibleTo(session.LocalPlayerId))
        .ToList();

    private static List<BattleReport> MyReports(IGameSession session) => session.State.Reports
        .Where(r => r.AttackerId == session.LocalPlayerId || r.DefenderId == session.LocalPlayerId)
        .ToList();
}
