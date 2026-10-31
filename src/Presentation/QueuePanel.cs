using System.Collections.Generic;
using Godot;

namespace Valkaris.Presentation;

public sealed record QueueRow(string IconPath, string Title, double StartTime, double FinishTime);

/// <summary>A queue: one running item with progress, then waiting items and free slots.</summary>
public partial class QueuePanel : PanelContainer
{
    private sealed record Row(TextureRect Icon, Label Title, Label Time, ProgressBar Bar);

    private readonly List<Row> _rows = new();
    private Label _title = null!;
    private Label _count = null!;
    private VBoxContainer _rowsBox = null!;

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _count = GetNode<Label>("%Count");
        _rowsBox = GetNode<VBoxContainer>("%Rows");
    }

    public void Refresh(string title, int slots, double now, IReadOnlyList<QueueRow> items)
    {
        while (_rows.Count < slots)
            _rows.Add(AddRow(_rows.Count));

        _title.Text = title;
        _count.Text = Format.OutOf(items.Count, slots);

        for (var i = 0; i < _rows.Count; i++)
        {
            if (i < items.Count)
                ShowItem(_rows[i], items[i], now);
            else
                ShowEmpty(_rows[i]);
        }
    }

    private static void ShowItem(Row row, QueueRow item, double now)
    {
        var running = now >= item.StartTime;

        row.Icon.Texture = GD.Load<Texture2D>(item.IconPath);
        row.Icon.Modulate = Colors.White;
        row.Title.Text = item.Title;
        row.Title.ThemeTypeVariation = "StrongLabel";
        row.Time.Text = running ? Format.Time(item.FinishTime - now) : L.T("queue.starts_in", ("time", Format.Time(item.StartTime - now)));
        row.Bar.Value = running ? (now - item.StartTime) / (item.FinishTime - item.StartTime) : 0;
        row.Bar.Modulate = running ? Colors.White : new Color(1, 1, 1, 0.35f);
    }

    private static void ShowEmpty(Row row)
    {
        row.Icon.Texture = null;
        row.Title.Text = L.T("queue.empty");
        row.Title.ThemeTypeVariation = "MutedLabel";
        row.Time.Text = "";
        row.Bar.Value = 0;
        row.Bar.Modulate = new Color(1, 1, 1, 0.35f);
    }

    private Row AddRow(int index)
    {
        var box = new VBoxContainer { Name = $"Row{index}" };
        box.AddThemeConstantOverride("separation", 4);

        var top = new HBoxContainer { Name = "Top" };
        top.AddThemeConstantOverride("separation", 8);
        var icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(24, 24),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        var title = new Label { Name = "Title", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var time = new Label { Name = "Time", ThemeTypeVariation = "MutedLabel" };
        top.AddChild(icon);
        top.AddChild(title);
        top.AddChild(time);

        var bar = new ProgressBar
        {
            Name = "Bar",
            MinValue = 0,
            MaxValue = 1,
            Step = 0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 8),
        };

        box.AddChild(top);
        box.AddChild(bar);
        _rowsBox.AddChild(box);
        return new Row(icon, title, time, bar);
    }
}
