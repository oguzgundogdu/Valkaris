using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>All the player's villages at a glance, and everything on the way. Laid out in columns for a short, wide drawer.</summary>
public partial class OverviewPanel : HBoxContainer
{
    private const int ColumnCount = 9;

    private readonly List<(Button Name, Label[] Cells)> _villages = new();
    private readonly List<int> _villageIds = new();
    private readonly List<(HBoxContainer Box, Label Title, Label Time)> _moving = new();
    private GridContainer _grid = null!;
    private VBoxContainer _rows = null!;
    private Label _none = null!;

    public event Action<int>? VillageSelected;

    public override void _Ready()
    {
        _grid = GetNode<GridContainer>("%VillageGrid");
        _rows = GetNode<VBoxContainer>("%Rows");
        _none = GetNode<Label>("%None");
        _grid.Columns = ColumnCount;

        var headers = new[]
        {
            L.T("overview.village"), UiContent.ResourceName(ResourceType.Wood), UiContent.ResourceName(ResourceType.Stone), UiContent.ResourceName(ResourceType.Food),
            L.T("overview.population"), L.T("overview.score"), L.T("overview.troops"), L.T("overview.building"), L.T("overview.training"),
        };
        foreach (var header in headers)
            _grid.AddChild(new Label { Text = header, ThemeTypeVariation = "MutedLabel" });
    }

    public void Refresh(IGameSession session)
    {
        RefreshVillages(session);
        RefreshMoving(session);
    }

    private void RefreshVillages(IGameSession session)
    {
        var villages = UiContent.OwnVillages(session);
        var now = session.State.ElapsedTime;
        while (_villages.Count < villages.Count)
            _villages.Add(AddVillageRow(_villages.Count));

        _villageIds.Clear();
        _villageIds.AddRange(villages.Select(v => v.Id));
        for (var i = 0; i < _villages.Count; i++)
        {
            var (name, cells) = _villages[i];
            var shown = i < villages.Count;
            name.Visible = shown;
            foreach (var cell in cells)
                cell.Visible = shown;
            if (!shown)
                continue;

            var village = villages[i];
            name.Text = village.Name;
            cells[0].Text = Format.Amount(village.Resources.Wood);
            cells[1].Text = Format.Amount(village.Resources.Stone);
            cells[2].Text = Format.Amount(village.Resources.Food);
            cells[3].Text = Format.Whole(ScoreRules.Population(session.Data, village));
            cells[4].Text = Format.Whole((int)ScoreRules.VillageScore(session.Data, village));
            cells[5].Text = Format.Whole(village.Troops.Values.Sum());
            cells[6].Text = village.BuildQueue.Count == 0
                ? L.T("overview.idle")
                : Format.Dotted(UiContent.Buildings[village.BuildQueue[0].Building].Name, Format.Time(village.BuildQueue[0].FinishTime - now));
            cells[7].Text = village.TrainingQueue.Count == 0
                ? L.T("overview.idle")
                : Format.Dotted(UiContent.Units[village.TrainingQueue[0].Unit].Name, Format.Time(village.TrainingQueue[0].FinishTime - now));
        }
    }

    private (Button, Label[]) AddVillageRow(int index)
    {
        var name = new Button { Name = $"Village{index}", ThemeTypeVariation = "SecondaryButton", FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left };
        name.Pressed += () => VillageSelected?.Invoke(_villageIds[index]);
        _grid.AddChild(name);

        var cells = new Label[ColumnCount - 1];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = new Label { Name = $"Cell{index}_{i}" };
            _grid.AddChild(cells[i]);
        }
        return (name, cells);
    }

    private void RefreshMoving(IGameSession session)
    {
        var now = session.State.ElapsedTime;
        var visible = session.State.Movements
            .Where(m => UiContent.IsVisibleMovement(session, m))
            .OrderBy(m => m.ArrivalTime)
            .ToList();

        while (_moving.Count < visible.Count)
            _moving.Add(AddMovingRow(_moving.Count));

        _none.Visible = visible.Count == 0;
        for (var i = 0; i < _moving.Count; i++)
        {
            var (box, title, time) = _moving[i];
            box.Visible = i < visible.Count;
            if (i >= visible.Count)
                continue;

            title.Text = UiContent.DescribeMovement(session, visible[i]);
            title.AddThemeColorOverride("font_color", visible[i].OwnerId == session.LocalPlayerId ? UiContent.Text : UiContent.Warning);
            time.Text = Format.Time(visible[i].ArrivalTime - now);
        }
    }

    private (HBoxContainer, Label, Label) AddMovingRow(int index)
    {
        var box = new HBoxContainer { Name = $"Moving{index}" };
        var title = new Label { Name = "Title", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        var time = new Label { Name = "Time", ThemeTypeVariation = "MutedLabel" };
        box.AddChild(title);
        box.AddChild(time);
        _rows.AddChild(box);
        return (box, title, time);
    }
}
