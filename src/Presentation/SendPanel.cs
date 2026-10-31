using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Valkaris.Core.Commands;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>Sends troops at the village picked on the map, and lists what is on the way.</summary>
public partial class SendPanel : VBoxContainer
{
    private sealed record UnitRow(SpinBox Amount, Label Available);

    private readonly Dictionary<UnitType, UnitRow> _units = new();
    private readonly List<(HBoxContainer Box, Label Title, Label Time)> _moving = new();
    private readonly List<MovementType> _missions = new();
    private readonly Dictionary<ResourceType, UnitRow> _resources = new();

    private Label _targetLine = null!;
    private Button _pickTarget = null!;
    private OptionButton _mission = null!;
    private Control _catapultRow = null!;
    private OptionButton _catapultTarget = null!;
    private Label _arrival = null!;
    private Button _send = null!;
    private Label _reason = null!;
    private VBoxContainer _rows = null!;
    private Label _none = null!;

    private IGameSession? _session;
    private ICommand? _command;
    private int? _targetId;
    private Village? _village;
    private MovementType? _wantedMission;
    private bool _sendingResources;

    public event Action? MapRequested;

    public void SetTarget(int villageId, bool sendResources = false)
    {
        _targetId = villageId;
        _sendingResources = sendResources;
        _wantedMission = sendResources ? MovementType.Transfer : MovementType.Reinforce;
    }

    /// <summary>Opens the resource form from the Marketplace. A target that isn't the player's own is dropped.</summary>
    public void ShowResourceForm()
    {
        _sendingResources = true;
        _wantedMission = MovementType.Transfer;
    }

    /// <summary>The amounts belonged to the village being left. The target stays.</summary>
    public void ClearAmounts()
    {
        foreach (var row in _units.Values.Concat(_resources.Values))
            row.Amount.Value = 0;
    }

    public override void _Ready()
    {
        _targetLine = GetNode<Label>("%TargetLine");
        _pickTarget = GetNode<Button>("%PickTarget");
        _pickTarget.Pressed += () => MapRequested?.Invoke();
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

        var resources = GetNode<VBoxContainer>("%ResourceRows");
        foreach (var type in Enum.GetValues<ResourceType>())
            _resources[type] = AddResourceRow(resources, type);

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

    private UnitRow AddResourceRow(VBoxContainer parent, ResourceType type)
    {
        var row = new HBoxContainer { Name = type.ToString() };
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(new TextureRect
        {
            Texture = GD.Load<Texture2D>(UiContent.ResourceIconPath(type)),
            CustomMinimumSize = new Vector2(24, 24),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        });
        row.AddChild(new Label { Text = type.ToString(), SizeFlagsHorizontal = SizeFlags.ExpandFill });

        var amount = new SpinBox { Name = "Amount", MinValue = 0, MaxValue = 0, Step = 1, UpdateOnTextChanged = true };
        var available = new Label { Name = "Available", ThemeTypeVariation = "MutedLabel", CustomMinimumSize = new Vector2(44, 0) };
        var max = new Button { Name = "Max", Text = "Max", ThemeTypeVariation = "SecondaryButton", FocusMode = FocusModeEnum.None };
        max.Pressed += () => amount.Value = MaxResourceAmount(type);
        row.AddChild(amount);
        row.AddChild(available);
        row.AddChild(max);

        parent.AddChild(row);
        return new UnitRow(amount, available);
    }

    /// <summary>What is in stock, and what the free carriers can still hold after the other rows.</summary>
    private double MaxResourceAmount(ResourceType type)
    {
        if (_session is null || _village is null)
            return 0;

        var room = CarrierRules.Free(_session.State, _session.Data, _village) * _session.Data.Villages.Carriers.Capacity
            - _resources.Where(r => r.Key != type).Sum(r => r.Value.Amount.Value);
        return Math.Clamp(room, 0, Math.Floor(_village.Resources.Get(type)));
    }

    public void Refresh(IGameSession session, Village village, bool mapIsOpen)
    {
        _session = session;
        _village = village;

        GetNode<Label>("%FromLine").Text = $"From {village.Name}";

        var target = _targetId is { } id ? session.State.Villages[id] : null;
        if (_sendingResources && target is not null && target.OwnerId != village.OwnerId)
        {
            _targetId = null;
            target = null;
        }

        RefreshTarget(session, village, target, mapIsOpen);
        RefreshIntel(session, target);
        RefreshMissions(target, village);
        var type = _missions[_mission.Selected];
        _sendingResources = type == MovementType.Transfer;

        GetNode<Control>("%Units").Visible = !_sendingResources;
        GetNode<Control>("%ResourceRows").Visible = _sendingResources;
        GetNode<Control>("%Warning").Visible = false;
        var scoutRule = GetNode<Label>("%ScoutRule");
        scoutRule.Visible = type == MovementType.Scout;
        scoutRule.Text = "Scouts face the Scouts at home there. Equal numbers rarely get through, and those caught all die. A large majority nearly always succeeds, and the bigger the margin, the more you see.";
        _send.Text = _sendingResources ? "Send resources" : "Send troops";

        if (_sendingResources)
            RefreshResourceForm(session, village, target);
        else
            RefreshTroopForm(session, village, target, type);

        RefreshMoving(session);
    }

    private void RefreshResourceForm(IGameSession session, Village village, Village? target)
    {
        foreach (var (type, row) in _resources)
        {
            var stock = Math.Floor(village.Resources.Get(type));
            row.Amount.MaxValue = stock;
            row.Available.Text = $"/ {stock}";
        }

        _catapultRow.Visible = false;

        int Amount(ResourceType type) => (int)_resources[type].Amount.Value;
        var command = target is null ? null : new SendResourcesCommand(village.Id, target.Id, Amount(ResourceType.Wood), Amount(ResourceType.Stone), Amount(ResourceType.Food));
        _command = command;
        var option = command is null ? null : SendResourcesPreview.Evaluate(session.State, session.Data, session.LocalPlayerId, command);

        _send.Disabled = option is not { CanSend: true };
        _arrival.Visible = option is { CanSend: true };
        _arrival.Text = option is { CanSend: true }
            ? $"Uses {option.CarriersUsed} of {option.CarriersFree} free carriers  ·  Arrives in {Format.Time(option.DurationSeconds)}"
            : "";
        _reason.Visible = _send.Disabled;
        _reason.Text = option is null
            ? "Pick one of your villages on the map."
            : option.Rejection == CommandRejection.NotEnoughCarriers
                ? $"This needs {option.CarriersUsed} carriers, and {option.CarriersFree} are free."
                : option.Rejection is { } rejection ? UiContent.RejectionText(rejection) : "";

        if (option is { CanSend: true } && option.ExpectedLoss != Resources.Zero)
        {
            var warning = GetNode<Label>("%Warning");
            warning.Visible = true;
            warning.Text = $"About {LossText(option.ExpectedLoss)} will be lost: {target!.Name}'s warehouse will be full.";
        }
    }

    private static string LossText(Resources loss) =>
        string.Join(", ", Enum.GetValues<ResourceType>()
            .Where(type => loss.Get(type) >= 1)
            .Select(type => $"{Format.Amount(loss.Get(type))} {type.ToString().ToLowerInvariant()}")
            .DefaultIfEmpty("a little"));

    private void RefreshTroopForm(IGameSession session, Village village, Village? target, MovementType type)
    {

        var unitRows = GetNode<VBoxContainer>("%Units");
        foreach (var (unit, row) in _units)
        {
            var present = village.TroopCount(unit);
            row.Amount.MaxValue = present;
            row.Available.Text = $"/ {present}";
            unitRows.GetNode<Control>(unit.ToString()).Visible = CanGo(unit, type);
        }

        var troops = _units.ToDictionary(u => u.Key, u => CanGo(u.Key, type) ? (int)u.Value.Amount.Value : 0);
        var showCatapultTarget = type == MovementType.Attack && troops[UnitType.Catapult] > 0;
        _catapultRow.Visible = showCatapultTarget;
        BuildingType? aim = showCatapultTarget && _catapultTarget.Selected > 0
            ? Enum.GetValues<BuildingType>()[_catapultTarget.Selected - 1]
            : null;

        var command = target is null ? null : new SendTroopsCommand(village.Id, target.Id, type, troops, aim);
        _command = command;
        var option = command is null ? null : SendPreview.Evaluate(session.State, session.Data, session.LocalPlayerId, command);

        _send.Disabled = option is not { CanSend: true };
        _arrival.Visible = option is { CanSend: true };
        _arrival.Text = option is { CanSend: true } ? $"Arrives in {Format.Time(option.DurationSeconds)}" : "";
        _reason.Visible = _send.Disabled;
        _reason.Text = option is null
            ? "Pick a target on the world map."
            : target!.Id == village.Id ? "Troops are already in this village."
            : option.Rejection is { } rejection ? UiContent.RejectionText(rejection) : "";
    }

    /// <summary>Only Scouts scout, and they never go on an attack or raid.</summary>
    private static bool CanGo(UnitType unit, MovementType type) => type switch
    {
        MovementType.Scout => unit == UnitType.Scout,
        MovementType.Attack or MovementType.Raid => unit != UnitType.Scout,
        _ => true,
    };

    private void RefreshTarget(IGameSession session, Village village, Village? target, bool mapIsOpen)
    {
        _targetLine.Text = target is null
            ? "No target yet"
            : $"{target.Name}  ·  {UiContent.OwnerName(session, target.OwnerId)}  ·  {Format.OneDecimal(UnitRules.Distance(village.Position, target.Position))} tiles";

        _pickTarget.Visible = !mapIsOpen;
        _pickTarget.Text = target is null ? "Pick a village on the map" : "Change target on the map";
    }

    /// <summary>What the player last learned here, so it is in view while choosing troops.</summary>
    private void RefreshIntel(IGameSession session, Village? target)
    {
        var intel = GetNode<Label>("%Intel");
        var text = target is null || target.OwnerId == session.LocalPlayerId ? null : UiContent.LatestIntel(session, target);
        intel.Visible = text is not null;
        intel.Text = text ?? "";
    }

    private void RefreshMissions(Village? target, Village village)
    {
        var offered = target is not null && target.OwnerId == village.OwnerId
            ? new List<MovementType> { MovementType.Reinforce, MovementType.Transfer }
            : target is null && _sendingResources
                ? new List<MovementType> { MovementType.Transfer }
                : new List<MovementType> { MovementType.Attack, MovementType.Raid, MovementType.Scout };

        if (!offered.SequenceEqual(_missions))
        {
            _missions.Clear();
            _missions.AddRange(offered);
            _mission.Clear();
            foreach (var mission in _missions)
                _mission.AddItem(mission == MovementType.Transfer ? "Send resources" : mission.ToString());
        }

        if (_wantedMission is { } wanted && _missions.Contains(wanted))
            _mission.Select(_missions.IndexOf(wanted));
        _wantedMission = null;
    }

    private void SendTroops()
    {
        if (_session is null || _command is null)
            return;

        _session.Send(_command);
        ClearAmounts();
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
        var title = new Label { Name = "Title", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var time = new Label { Name = "Time", ThemeTypeVariation = "MutedLabel" };
        box.AddChild(title);
        box.AddChild(time);
        _rows.AddChild(box);
        return (box, title, time);
    }
}
