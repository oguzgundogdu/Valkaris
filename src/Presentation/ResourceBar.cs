using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>Top bar: resources of the current village and the match clock.</summary>
public partial class ResourceBar : PanelContainer
{
    private sealed record Chip(Label Amount, Label Capacity, Label Rate);

    private readonly Dictionary<ResourceType, Chip> _chips = new();
    private Label _clock = null!;
    private Button _pause = null!;
    private Button _incoming = null!;

    private Button _viewVillage = null!;
    private Button _viewWorld = null!;

    private int _incomingTarget;

    public event Action? PauseRequested;
    public event Action? CapitalWarningPressed;
    public event Action<int>? IncomingPressed;
    public event Action<GameScreen.MainView>? ViewRequested;

    public override void _Ready()
    {
        _clock = GetNode<Label>("%Clock");
        _pause = GetNode<Button>("%Pause");
        _incoming = GetNode<Button>("%Incoming");
        _incoming.Pressed += () => IncomingPressed?.Invoke(_incomingTarget);
        _pause.Pressed += () => PauseRequested?.Invoke();
        GetNode<Button>("%CapitalWarning").Pressed += () => CapitalWarningPressed?.Invoke();

        _viewVillage = GetNode<Button>("%ViewVillage");
        _viewWorld = GetNode<Button>("%ViewWorld");
        _viewVillage.Pressed += () => ViewRequested?.Invoke(GameScreen.MainView.Village);
        _viewWorld.Pressed += () => ViewRequested?.Invoke(GameScreen.MainView.World);

        var chips = GetNode<HBoxContainer>("%Chips");
        foreach (var type in Enum.GetValues<ResourceType>())
            _chips[type] = AddChip(chips, type);
    }

    private static Chip AddChip(HBoxContainer parent, ResourceType type)
    {
        var box = new HBoxContainer { Name = type.ToString() };
        box.AddThemeConstantOverride("separation", 6);

        box.AddChild(new TextureRect
        {
            Texture = GD.Load<Texture2D>(UiContent.ResourceIconPath(type)),
            CustomMinimumSize = new Vector2(26, 26),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TooltipText = type.ToString(),
        });

        var amount = new Label { ThemeTypeVariation = "StrongLabel", Name = "Amount" };
        var capacity = new Label { ThemeTypeVariation = "MutedLabel", Name = "Capacity" };
        var rate = new Label { ThemeTypeVariation = "MutedLabel", Name = "Rate" };
        box.AddChild(amount);
        box.AddChild(capacity);
        box.AddChild(rate);

        parent.AddChild(box);
        return new Chip(amount, capacity, rate);
    }

    /// <summary>With no village, the player is only watching: there is nothing of theirs to show.</summary>
    /// <summary>Shown while a new capital is due and its dialog has been set aside.</summary>
    public void RefreshCapitalWarning(double? secondsLeft)
    {
        var warning = GetNode<Button>("%CapitalWarning");
        warning.Visible = secondsLeft is not null;
        if (secondsLeft is { } seconds)
            warning.Text = $"Choose a capital {Format.Time(seconds)}";
    }

    private void RefreshResources(IGameSession session, Village village)
    {
        var capacity = BuildingRules.VillageCapacity(session.Data, village);
        var production = BuildingRules.VillageProduction(session.Data, village) * session.State.Settings.SpeedMultiplier;

        foreach (var (type, chip) in _chips)
        {
            var amount = village.Resources.Get(type);
            chip.Amount.Text = Format.Amount(amount);
            chip.Amount.AddThemeColorOverride("font_color", amount >= capacity ? UiContent.Warning : UiContent.Text);
            chip.Capacity.Text = $"/ {Format.Amount(capacity)}";
            chip.Rate.Text = Format.Rate(production.Get(type));
        }
    }

    public void Refresh(IGameSession session, Village? village, GameScreen.MainView view)
    {
        _viewVillage.ButtonPressed = view == GameScreen.MainView.Village;
        _viewWorld.ButtonPressed = view == GameScreen.MainView.World;
        GetNode<Control>("%ViewToggle").Visible = village is not null;
        GetNode<Control>("%Chips").Modulate = village is null ? Colors.Transparent : Colors.White;

        if (village is not null)
            RefreshResources(session, village);

        var remaining = session.State.Settings.MatchDurationSeconds - session.State.ElapsedTime;
        _clock.Text = Format.Time(remaining);
        _pause.Visible = session.CanPause;

        var hostile = session.State.Movements
            .Where(m => m.OwnerId != session.LocalPlayerId && m.ToVillageId is { } target && session.State.Villages[target].OwnerId == session.LocalPlayerId)
            .OrderBy(m => m.ArrivalTime)
            .FirstOrDefault();
        _incoming.Visible = hostile is not null;
        if (hostile is not null)
            _incomingTarget = hostile.ToVillageId!.Value;
        if (hostile is not null)
            _incoming.Text = $"{hostile.Type} arriving in {Format.Time(hostile.ArrivalTime - session.State.ElapsedTime)}";
    }
}
