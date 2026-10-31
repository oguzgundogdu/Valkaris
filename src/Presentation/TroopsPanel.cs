using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Valkaris.Core.Commands;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>The garrison, with training for each unit, and the training queue.</summary>
public partial class TroopsPanel : VBoxContainer
{
    private const int MaxBatch = 9999;

    private sealed record Row(Label Count, Label Stats, Control Controls, CostView Cost, Label Time, SpinBox Amount, Button Max, Button Train, Label Note);

    private readonly Dictionary<UnitType, Row> _rows = new();
    private QueuePanel _queue = null!;
    private IGameSession? _session;
    private int _villageId;

    public override void _Ready()
    {
        _queue = GetNode<QueuePanel>("%TrainingQueue");
        var rows = GetNode<VBoxContainer>("%Rows");
        foreach (var unit in Enum.GetValues<UnitType>())
            _rows[unit] = AddRow(rows, unit);
    }

    private Row AddRow(VBoxContainer parent, UnitType unit)
    {
        var info = UiContent.Units[unit];
        var box = new VBoxContainer { Name = unit.ToString() };
        box.AddThemeConstantOverride("separation", 4);

        var header = new HBoxContainer { Name = "Header" };
        header.AddThemeConstantOverride("separation", 8);
        header.AddChild(new TextureRect
        {
            Texture = GD.Load<Texture2D>(info.IconPath),
            CustomMinimumSize = new Vector2(28, 28),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        });
        header.AddChild(new Label { Text = info.Name, ThemeTypeVariation = "StrongLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var count = new Label { Name = "Count", ThemeTypeVariation = "StrongLabel" };
        header.AddChild(count);
        box.AddChild(header);

        var stats = new Label { Name = "Stats", ThemeTypeVariation = "MutedLabel" };
        box.AddChild(stats);

        var controls = new VBoxContainer { Name = "Controls" };
        controls.AddThemeConstantOverride("separation", 4);

        var costRow = new HBoxContainer();
        costRow.AddThemeConstantOverride("separation", 12);
        var cost = new CostView { Name = "Cost", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var time = new Label { Name = "Time", ThemeTypeVariation = "MutedLabel" };
        costRow.AddChild(cost);
        costRow.AddChild(time);
        controls.AddChild(costRow);

        var actions = new HBoxContainer { Name = "Actions" };
        actions.AddThemeConstantOverride("separation", 6);
        var amount = new SpinBox { Name = "Amount", MinValue = 0, MaxValue = MaxBatch, Step = 1, Value = 1, UpdateOnTextChanged = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var max = new Button { Name = "Max", Text = L.T("common.max"), ThemeTypeVariation = "SecondaryButton", FocusMode = FocusModeEnum.None };
        var train = new Button { Name = "Train", CustomMinimumSize = new Vector2(120, 0), FocusMode = FocusModeEnum.None };
        actions.AddChild(amount);
        actions.AddChild(max);
        actions.AddChild(train);
        controls.AddChild(actions);
        box.AddChild(controls);

        var note = new Label { Name = "Note", ThemeTypeVariation = "MutedLabel" };
        box.AddChild(note);

        parent.AddChild(box);
        parent.AddChild(new HSeparator { Modulate = new Color(1, 1, 1, 0.3f) });

        max.Pressed += () => amount.Value = Math.Min(MaxBatch, CurrentMax(unit));
        train.Pressed += () => _session?.Send(new TrainCommand(_villageId, unit, (int)amount.Value));

        return new Row(count, stats, controls, cost, time, amount, max, train, note);
    }

    private int CurrentMax(UnitType unit) => _session is null
        ? 0
        : TrainPreview.Evaluate(_session.State, _session.Data, _session.LocalPlayerId, _villageId, unit, 1).MaxCount;

    public void Refresh(IGameSession session, Village village)
    {
        _session = session;
        _villageId = village.Id;

        var barracks = village.BuildingLevel(BuildingType.Barracks);
        foreach (var (unit, row) in _rows)
        {
            var definition = session.Data.Units.Units[unit];
            row.Count.Text = Format.Times(village.TroopCount(unit));
            row.Stats.Text =
                L.T("troops.stats", ("attack", Format.Whole(definition.Attack)), ("infantry", Format.Whole(definition.DefenceVsInfantry)), ("cavalry", Format.Whole(definition.DefenceVsCavalry)), ("carry", Format.Whole(definition.LootCapacity)));

            var locked = barracks < definition.RequiredBarracksLevel;
            row.Controls.Visible = !locked;
            if (locked)
            {
                row.Note.Text = L.T("troops.requires", ("level", Format.Whole(definition.RequiredBarracksLevel)));
                row.Note.Visible = true;
                continue;
            }

            var count = (int)row.Amount.Value;
            var option = TrainPreview.Evaluate(session.State, session.Data, session.LocalPlayerId, village.Id, unit, count);
            row.Cost.Refresh(option.Cost, village.Resources);
            row.Time.Text = Format.Time(option.DurationSeconds);
            row.Train.Text = L.T("troops.train", ("count", Format.Whole(count)));
            row.Train.Disabled = !option.CanTrain;

            var note = option.Rejection is null or CommandRejection.InvalidCount ? "" : UiContent.RejectionText(option.Rejection.Value);
            row.Note.Text = note;
            row.Note.Visible = note.Length > 0;
        }

        var now = session.State.ElapsedTime;
        _queue.Refresh(L.T("queue.training"), session.Data.Game.MaxTrainingQueueLength, now, village.TrainingQueue
            .Select(item => new QueueRow(UiContent.Units[item.Unit].IconPath, L.T("queue.train_item", ("unit", UiContent.Units[item.Unit].Name), ("count", Format.Whole(item.Count))), item.StartTime, item.FinishTime))
            .ToList());
    }
}
