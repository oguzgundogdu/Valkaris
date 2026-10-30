using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Valkaris.Core.Commands;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>
/// Sends troops and lists what is on the way. The mission and troop form stays when the world map arrives;
/// only the temporary target list is replaced by picking a village on the map.
/// </summary>
public partial class SendPanel : VBoxContainer
{
    private sealed record UnitRow(SpinBox Amount, Label Available);

    private readonly Dictionary<UnitType, UnitRow> _units = new();
    private readonly List<(HBoxContainer Box, Label Title, Label Time)> _moving = new();
    private readonly List<MovementType> _missions = new();
    private readonly ButtonGroup _targetGroup = new();
    private readonly List<int> _targetIds = new();

    private VBoxContainer _targets = null!;
    private OptionButton _mission = null!;
    private Control _catapultRow = null!;
    private OptionButton _catapultTarget = null!;
    private Label _arrival = null!;
    private Button _send = null!;
    private Label _reason = null!;
    private VBoxContainer _rows = null!;
    private Label _none = null!;

    private IGameSession? _session;
    private SendTroopsCommand? _command;
    private int _targetId;

    public override void _Ready()
    {
        _targets = GetNode<VBoxContainer>("%Targets");
        _mission = GetNode<OptionButton>("%Mission");
        _catapultRow = GetNode<Control>("%CatapultRow");
        _catapultTarget = GetNode<OptionButton>("%CatapultTarget");
        _arrival = GetNode<Label>("%Arrival");
        _send = GetNode<Button>("%Send");
        _reason = GetNode<Label>("%Reason");
        _rows = GetNode<VBoxContainer>("%Rows");
        _none = GetNode<Label>("%None");

        var units = GetNode<VBoxContainer>("%Units");
        foreach (var unit in Enum.GetValues<UnitType>())
            _units[unit] = AddUnitRow(units, unit);

        _catapultTarget.AddItem("Random");
        foreach (var building in Enum.GetValues<BuildingType>())
            _catapultTarget.AddItem(UiContent.Buildings[building].Name);

        _send.Pressed += SendTroops;
    }

    private static UnitRow AddUnitRow(VBoxContainer parent, UnitType unit)
    {
        var info = UiContent.Units[unit];
        var row = new HBoxContainer { Name = unit.ToString() };
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(new TextureRect
        {
            Texture = GD.Load<Texture2D>(info.IconPath),
            CustomMinimumSize = new Vector2(24, 24),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        });
        row.AddChild(new Label { Text = info.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill });

        var amount = new SpinBox { Name = "Amount", MinValue = 0, MaxValue = 0, Step = 1, UpdateOnTextChanged = true };
        var available = new Label { Name = "Available", ThemeTypeVariation = "MutedLabel", CustomMinimumSize = new Vector2(44, 0) };
        var max = new Button { Name = "Max", Text = "Max", ThemeTypeVariation = "SecondaryButton", FocusMode = FocusModeEnum.None };
        max.Pressed += () => amount.Value = amount.MaxValue;
        row.AddChild(amount);
        row.AddChild(available);
        row.AddChild(max);

        parent.AddChild(row);
        return new UnitRow(amount, available);
    }

    public void Refresh(IGameSession session, Village village)
    {
        _session = session;

        var hasOtherOwnVillage = session.State.Villages.Values.Any(v => v.OwnerId == village.OwnerId && v.Id != village.Id);
        RefreshMissions(hasOtherOwnVillage);
        var type = _missions[_mission.Selected];

        RefreshTargets(session, village, type);

        foreach (var (unit, row) in _units)
        {
            var present = village.TroopCount(unit);
            row.Amount.MaxValue = present;
            row.Available.Text = $"/ {present}";
        }

        var troops = _units.ToDictionary(u => u.Key, u => (int)u.Value.Amount.Value);
        var showCatapultTarget = type == MovementType.Attack && troops[UnitType.Catapult] > 0;
        _catapultRow.Visible = showCatapultTarget;
        BuildingType? aim = showCatapultTarget && _catapultTarget.Selected > 0
            ? Enum.GetValues<BuildingType>()[_catapultTarget.Selected - 1]
            : null;

        _command = new SendTroopsCommand(village.Id, _targetId, type, troops, aim);
        var option = SendPreview.Evaluate(session.State, session.Data, session.LocalPlayerId, _command);

        _send.Disabled = !option.CanSend;
        _arrival.Text = option.CanSend ? $"Arrives in {Format.Time(option.DurationSeconds)}" : "";
        _arrival.Visible = option.CanSend;
        _reason.Text = option.Rejection is { } rejection ? UiContent.RejectionText(rejection) : "";
        _reason.Visible = !option.CanSend;

        RefreshMoving(session);
    }

    private void RefreshMissions(bool hasOtherOwnVillage)
    {
        var wanted = new List<MovementType> { MovementType.Attack, MovementType.Raid };
        if (hasOtherOwnVillage)
            wanted.Add(MovementType.Reinforce);
        if (wanted.SequenceEqual(_missions))
            return;

        _missions.Clear();
        _missions.AddRange(wanted);
        _mission.Clear();
        foreach (var mission in _missions)
            _mission.AddItem(mission.ToString());
    }

    // Temporary until the world map lets the player pick a village.
    private void RefreshTargets(IGameSession session, Village village, MovementType type)
    {
        var targets = session.State.Villages.Values
            .Where(v => type == MovementType.Reinforce ? v.OwnerId == village.OwnerId && v.Id != village.Id : v.OwnerId != village.OwnerId)
            .OrderBy(v => UnitRules.Distance(village.Position, v.Position))
            .ToList();

        if (!targets.Select(t => t.Id).SequenceEqual(_targetIds))
        {
            _targetIds.Clear();
            _targetIds.AddRange(targets.Select(t => t.Id));
            foreach (var child in _targets.GetChildren())
                child.QueueFree();

            foreach (var target in targets)
            {
                var id = target.Id;
                var owner = UiContent.OwnerName(session, target.OwnerId);
                var button = new Button
                {
                    Name = $"Target{id}",
                    Text = $"{owner}'s village  ·  {UnitRules.Distance(village.Position, target.Position):0.0} tiles",
                    ThemeTypeVariation = "SecondaryButton",
                    ToggleMode = true,
                    ButtonGroup = _targetGroup,
                    FocusMode = FocusModeEnum.None,
                };
                button.Pressed += () => _targetId = id;
                _targets.AddChild(button);
                if (id == _targetId || !_targetIds.Contains(_targetId))
                {
                    _targetId = id;
                    button.ButtonPressed = true;
                }
            }
        }
    }

    private void SendTroops()
    {
        if (_session is null || _command is null)
            return;

        _session.Send(_command);
        foreach (var row in _units.Values)
            row.Amount.Value = 0;
    }

    private void RefreshMoving(IGameSession session)
    {
        var now = session.State.ElapsedTime;
        var visible = session.State.Movements
            .Where(m => m.OwnerId == session.LocalPlayerId || IsHostileArrival(session, m))
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

            title.Text = Describe(session, visible[i]);
            title.AddThemeColorOverride("font_color", visible[i].OwnerId == session.LocalPlayerId ? UiContent.Text : UiContent.Warning);
            time.Text = Format.Time(visible[i].ArrivalTime - now);
        }
    }

    private static bool IsHostileArrival(IGameSession session, Movement movement) =>
        movement.OwnerId != session.LocalPlayerId && session.State.Villages[movement.ToVillageId].OwnerId == session.LocalPlayerId;

    private static string Describe(IGameSession session, Movement movement)
    {
        if (movement.OwnerId != session.LocalPlayerId)
            return $"{movement.Type} from {UiContent.OwnerName(session, movement.OwnerId)}";

        var target = UiContent.OwnerName(session, session.State.Villages[movement.ToVillageId].OwnerId);
        if (movement.Type != MovementType.Return)
            return $"{movement.Type} → {target}";

        var loot = movement.Loot.Wood + movement.Loot.Stone + movement.Loot.Food;
        return loot > 0 ? $"Returning with {Format.Amount(loot)} loot" : "Returning";
    }

    private (HBoxContainer, Label, Label) AddMovingRow(int index)
    {
        var box = new HBoxContainer { Name = $"Moving{index}" };
        var title = new Label { Name = "Title", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var time = new Label { Name = "Time", ThemeTypeVariation = "MutedLabel" };
        box.AddChild(title);
        box.AddChild(time);
        _rows.AddChild(box);
        return (box, title, time);
    }
}
