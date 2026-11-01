using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>The local player's reports and the public news. Read state is display-only, not game state. Own battles stay pinned until read.</summary>
public partial class ReportsPanel : PanelContainer
{
    private const float TimeWidth = 62;
    private const float FadedAlpha = 0.45f;
    private const string PinnedIcon = "res://assets/icons/mission_swords.svg";

    private sealed record Row(int Id, Button Button, double Time, bool IsScout);

    private readonly HashSet<int> _read = new();
    private readonly Dictionary<int, Row> _rows = new();
    private VBoxContainer _list = null!;
    private VBoxContainer _newsList = null!;
    private Button _mine = null!;
    private Button _scouting = null!;
    private Button _news = null!;
    private CheckBox _near = null!;
    private Label _none = null!;
    private string _newsKey = "";
    private string _orderKey = "";
    private IGameSession? _session;

    public event Action<BattleReport>? ReportOpened;
    public event Action<ScoutReport>? ScoutReportOpened;
    public event Action<ScoutNotice>? ScoutNoticeOpened;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        _list = GetNode<VBoxContainer>("%Rows");
        _none = GetNode<Label>("%None");
        _newsList = GetNode<VBoxContainer>("%NewsRows");
        _mine = GetNode<Button>("%Mine");
        _scouting = GetNode<Button>("%Scouting");
        _news = GetNode<Button>("%News");
        _near = GetNode<CheckBox>("%Near");

        var group = new ButtonGroup();
        _mine.ButtonGroup = group;
        _scouting.ButtonGroup = group;
        _news.ButtonGroup = group;
        GetNode<Button>("%MarkRead").Pressed += () =>
        {
            if (_session is not null)
                MarkAllRead(_session);
        };
    }

    public int UnreadCount(IGameSession session) => MyReportIds(session).Count(id => !_read.Contains(id));

    public void MarkAllRead(IGameSession session)
    {
        foreach (var id in MyReportIds(session))
            _read.Add(id);
    }

    public bool IsPinned(int id) => _rows.TryGetValue(id, out var row) && !row.IsScout && !_read.Contains(id);

    public void Refresh(IGameSession session)
    {
        AddNewRows(session);
        _session = session;

        // Looking at the scouting list counts as reading it. Battles stay pinned until opened.
        if (_scouting.ButtonPressed)
        {
            foreach (var row in _rows.Values.Where(r => r.IsScout))
                _read.Add(row.Id);
        }

        foreach (var row in _rows.Values)
        {
            var pinned = IsPinned(row.Id);
            row.Button.Icon = pinned ? GD.Load<Texture2D>(PinnedIcon) : null;
            row.Button.Visible = pinned || (row.IsScout ? _scouting.ButtonPressed : _mine.ButtonPressed);
        }
        SortRows();

        _newsList.Visible = _news.ButtonPressed;
        _near.Visible = _news.ButtonPressed;
        if (_news.ButtonPressed)
            RefreshNews(session);

        var anyRow = _rows.Values.Any(r => r.Button.Visible);
        _none.Text = _news.ButtonPressed ? L.T("ui.reports.no_news") : L.T("ui.reports.none");
        _none.Visible = !anyRow && (!_news.ButtonPressed || _newsList.GetChildCount() == 0);
    }

    private void AddNewRows(IGameSession session)
    {
        var rows = MyReports(session)
            .Select(r => (r.Id, r.Time, Text: UiContent.ReportHeadline(session, r), Bad: !UiContent.LocalWon(session, r), IsScout: false, Open: (Action)(() => ReportOpened?.Invoke(r))))
            .Concat(MyScoutReports(session).Select(r => (r.Id, r.Time, Text: UiContent.ScoutHeadline(session, r), Bad: !r.Succeeded, IsScout: true, Open: (Action)(() => ScoutReportOpened?.Invoke(r)))))
            .Concat(MyScoutNotices(session).Select(n => (n.Id, n.Time, Text: UiContent.NoticeHeadline(session, n), Bad: false, IsScout: true, Open: (Action)(() => ScoutNoticeOpened?.Invoke(n)))))
            .Where(row => !_rows.ContainsKey(row.Id));

        foreach (var row in rows)
        {
            var id = row.Id;
            var button = new Button
            {
                Name = $"Report{id}",
                Text = row.Text,
                ThemeTypeVariation = "SecondaryButton",
                Alignment = HorizontalAlignment.Left,
                FocusMode = FocusModeEnum.None,
                ExpandIcon = true,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            button.AddThemeColorOverride("font_color", row.Bad ? UiContent.Danger : UiContent.Text);
            button.AddThemeConstantOverride("icon_max_width", 18);
            button.Pressed += () =>
            {
                _read.Add(id);
                row.Open();
            };

            var time = TimeLabel(row.Time);
            time.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.RightWide);
            time.OffsetLeft = -TimeWidth - 8;
            time.OffsetRight = -8;
            button.AddChild(time);

            _list.AddChild(button);
            _rows[id] = new Row(id, button, row.Time, row.IsScout);
        }
    }

    private static Label TimeLabel(double time) => new()
    {
        Name = "Time",
        Text = Format.Time(time),
        ThemeTypeVariation = "MutedLabel",
        CustomMinimumSize = new Vector2(TimeWidth, 0),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        MouseFilter = MouseFilterEnum.Ignore,
    };

    /// <summary>Pinned rows first, then newest first. Only moves anything when the order changes.</summary>
    private void SortRows()
    {
        var order = _rows.Values.OrderByDescending(r => IsPinned(r.Id)).ThenByDescending(r => r.Id).ToList();
        var key = string.Join(',', order.Select(r => r.Id));
        if (key == _orderKey)
            return;

        _orderKey = key;
        for (var i = 0; i < order.Count; i++)
            _list.MoveChild(order[i].Button, i);
    }

    private void RefreshNews(IGameSession session)
    {
        var state = session.State;
        Func<int, bool>? near = _near.ButtonPressed
            ? id => Visibility.InSight(state, session.Data, session.LocalPlayerId, state.Villages[id].Position.X, state.Villages[id].Position.Y)
            : null;
        var lines = NewsFeed.Build(session.PublicNotices, state.ElapsedTime, near);

        var key = string.Join('|', lines.Select(l => (l.FromVillageName, l.TargetVillageId, l.AttackerWon, l.Count, l.Faded)));
        if (key == _newsKey)
            return;

        _newsKey = key;
        foreach (var child in _newsList.GetChildren())
        {
            _newsList.RemoveChild(child);
            child.QueueFree();
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var row = new HBoxContainer { Name = $"News{i}", Modulate = new Color(1, 1, 1, line.Faded ? FadedAlpha : 1) };
            var text = L.T(line.AttackerWon ? "notice.attacked_won" : "notice.attacked_lost", ("attacker", line.FromVillageName), ("target", line.TargetVillageName));
            if (line.Count > 1)
                text = L.T("notice.repeat", ("text", text), ("count", Format.Whole(line.Count)));
            row.AddChild(new Label { Name = "Text", Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true });
            row.AddChild(TimeLabel(line.LatestTime));
            _newsList.AddChild(row);
        }
    }

    private static IEnumerable<int> MyReportIds(IGameSession session) =>
        MyReports(session).Select(r => r.Id).Concat(MyScoutReports(session).Select(r => r.Id)).Concat(MyScoutNotices(session).Select(n => n.Id));

    private static List<ScoutReport> MyScoutReports(IGameSession session) => session.State.ScoutReports
        .Where(r => r.SenderId == session.LocalPlayerId)
        .ToList();

    private static List<ScoutNotice> MyScoutNotices(IGameSession session) => session.State.ScoutNotices
        .Where(n => n.DefenderId == session.LocalPlayerId)
        .ToList();

    private static List<BattleReport> MyReports(IGameSession session) => session.State.Reports
        .Where(r => r.AttackerId == session.LocalPlayerId || r.DefenderId == session.LocalPlayerId)
        .ToList();
}
