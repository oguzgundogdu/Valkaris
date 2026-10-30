using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>The local player's battle reports, newest first. Read state is display-only, not game state.</summary>
public partial class ReportsPanel : PanelContainer
{
    private readonly HashSet<int> _read = new();
    private readonly Dictionary<int, Button> _rows = new();
    private VBoxContainer _list = null!;
    private Label _none = null!;

    public event Action<BattleReport>? ReportOpened;

    public override void _Ready()
    {
        _list = GetNode<VBoxContainer>("%Rows");
        _none = GetNode<Label>("%None");
    }

    public int UnreadCount(IGameSession session) => MyReports(session).Count(r => !_read.Contains(r.Id));

    public void MarkAllRead(IGameSession session)
    {
        foreach (var report in MyReports(session))
            _read.Add(report.Id);
    }

    public void Refresh(IGameSession session)
    {
        var reports = MyReports(session);
        _none.Visible = reports.Count == 0;

        foreach (var report in reports.Where(r => !_rows.ContainsKey(r.Id)))
        {
            var button = new Button
            {
                Name = $"Report{report.Id}",
                Text = $"{UiContent.ReportHeadline(session, report)}  ·  {Format.Time(report.Time)}",
                ThemeTypeVariation = "SecondaryButton",
                Alignment = HorizontalAlignment.Left,
                FocusMode = FocusModeEnum.None,
            };
            button.AddThemeColorOverride("font_color", UiContent.LocalWon(session, report) ? UiContent.Text : UiContent.Danger);
            button.Pressed += () => ReportOpened?.Invoke(report);
            _list.AddChild(button);
            _list.MoveChild(button, 0);
            _rows[report.Id] = button;
        }
    }

    private static List<BattleReport> MyReports(IGameSession session) => session.State.Reports
        .Where(r => r.AttackerId == session.LocalPlayerId || r.DefenderId == session.LocalPlayerId)
        .ToList();
}
