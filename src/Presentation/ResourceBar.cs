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
    private const float IconButtonWidth = 44;
    private const string ViewVillageIcon = "res://assets/icons/view_village.svg";
    private const string ViewWorldIcon = "res://assets/icons/view_world.svg";

    private sealed record Chip(Label Amount, Label Capacity, Label Rate);

    private readonly Dictionary<ResourceType, Chip> _chips = new();
    private Label _clock = null!;
    private Button _pause = null!;
    private Button _incoming = null!;

    private Button _viewVillage = null!;
    private Button _viewWorld = null!;

    private readonly List<Control> _extraItems = new();
    private int _incomingTarget;
    private string _fitKey = "";
    private TopBarPlan _plan = TopBarLayout.Levels[0];
    private string _capitalTime = "";
    private string _incomingMission = "";
    private string _incomingTime = "";

    /// <summary>The bar's parts at the current level of detail, for the UI test.</summary>
    public TopBarPlan Plan => _plan;

    /// <summary>The width its contents need, for the UI test: it must never be more than the bar has.</summary>
    public float ContentWidth => GetNode<Control>("%Box").GetCombinedMinimumSize().X;

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

        foreach (var button in new[] { _viewVillage, _viewWorld })
            button.ExpandIcon = true;

        var chips = GetNode<HBoxContainer>("%Chips");
        foreach (var type in Enum.GetValues<ResourceType>())
            _chips[type] = AddChip(chips, type);

        // The row sits in a clipped box of no minimum width, so it can never push the window wider.
        var clip = GetNode<Control>("%Clip");
        clip.CustomMinimumSize = new Vector2(0, GetNode<Control>("%Box").GetCombinedMinimumSize().Y);
    }

    /// <summary>Something else that always shows in the bar, such as the God mode badge.</summary>
    public void AddFixedItem(Control item) => _extraItems.Add(item);

    public void RemoveFixedItem(Control item) => _extraItems.Remove(item);

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
            TooltipText = UiContent.ResourceName(type),
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
        _capitalTime = secondsLeft is { } seconds ? Format.Time(seconds) : "";
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
            chip.Capacity.Text = Format.Limit(capacity);
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

        _clock.Text = Format.Time(session.State.Settings.MatchDurationSeconds is { } duration
            ? duration - session.State.ElapsedTime
            : session.State.ElapsedTime);
        if (!session.CanPause)
        {
            _pause.Text = L.T("bar.menu");
            _pause.TooltipText = L.T("bar.menu_tip");
        }

        var hostile = UiContent.IncomingAttacks(session).OrderBy(m => m.ArrivalTime).FirstOrDefault();
        _incoming.Visible = hostile is not null;
        if (hostile is not null)
        {
            _incomingTarget = hostile.ToVillageId!.Value;
            _incomingMission = UiContent.MissionName(hostile.Type);
            _incomingTime = Format.Time(hostile.ArrivalTime - session.State.ElapsedTime);
        }

        RefreshStats(session, village is not null);
        Fit();
    }

    /// <summary>Only ever the player's own. Other players' figures stay hidden until the match ends.</summary>
    private void RefreshStats(IGameSession session, bool show)
    {
        var state = session.State;
        var score = ScoreRules.For(state, session.Data, state.Players[session.LocalPlayerId]);
        GetNode<Control>("%Stats").Modulate = show ? Colors.White : Colors.Transparent;
        GetNode<Label>("%Population").Text = Format.Whole(score.Population);
        GetNode<Label>("%Score").Text = Format.Whole((int)score.Score);
    }

    private string CapitalText(TopBarWarning level) => level switch
    {
        TopBarWarning.Full => L.T("bar.choose_capital", ("time", _capitalTime)),
        TopBarWarning.Short => L.T("bar.choose_capital_short", ("time", _capitalTime)),
        _ => _capitalTime,
    };

    private string IncomingText(TopBarWarning level) => level switch
    {
        TopBarWarning.Full => L.T("bar.incoming", ("mission", _incomingMission), ("time", _incomingTime)),
        TopBarWarning.Short => L.T("bar.incoming_short", ("mission", _incomingMission), ("time", _incomingTime)),
        _ => _incomingTime,
    };

    /// <summary>Text only changes width when its length does, so the level is rarely worked out again.</summary>
    private void Fit()
    {
        var capital = GetNode<Button>("%CapitalWarning");
        var available = GetNode<Control>("%Clip").Size.X;
        var key = string.Join(',', available, capital.Visible, CapitalText(TopBarWarning.Full).Length, _incoming.Visible, IncomingText(TopBarWarning.Full).Length,
            _clock.Text.Length, GetNode<Label>("%Population").Text.Length, GetNode<Label>("%Score").Text.Length, _extraItems.Count(i => i.Visible),
            string.Concat(_chips.Values.Select(c => c.Amount.Text.Length + c.Capacity.Text.Length + c.Rate.Text.Length)));
        if (key != _fitKey)
        {
            _fitKey = key;
            _plan = TopBarLayout.Choose(MeasureWidths(capital), available);
            ApplyPlan();
        }

        if (capital.Visible)
            capital.Text = CapitalText(_plan.Warning);
        if (_incoming.Visible)
            _incoming.Text = IncomingText(_plan.Warning);
    }

    private static float TextWidth(Control control, string text) =>
        control.GetThemeFont("font").GetStringSize(text, HorizontalAlignment.Left, -1, control.GetThemeFontSize("font_size")).X;

    private TopBarWidths MeasureWidths(Button capital)
    {
        // A chip's parts have a small gap between them, and a button has margins around its text.
        const float partGap = 6, iconWidth = 26, buttonMargin = 28, iconButton = IconButtonWidth, statGap = 14, toggleGap = 2;

        float Warnings(TopBarWarning level) =>
            (capital.Visible ? TextWidth(capital, CapitalText(level)) + buttonMargin : 0)
            + (_incoming.Visible ? TextWidth(_incoming, IncomingText(level)) + buttonMargin : 0);

        float Parts(Func<Chip, Label> part) => _chips.Values.Sum(chip => TextWidth(part(chip), part(chip).Text) + partGap);

        var population = GetNode<Label>("%Population");
        var score = GetNode<Label>("%Score");
        var populationLabel = GetNode<Label>("%PopulationLabel");
        var scoreLabel = GetNode<Label>("%ScoreLabel");
        var viewVillage = TextWidth(_viewVillage, L.T("ui.bar.village")) + buttonMargin;
        var viewWorld = TextWidth(_viewWorld, L.T("ui.bar.world")) + buttonMargin;
        var brand = GetNode<Label>("%Brand");
        var fixedParts = TextWidth(_clock, _clock.Text) + _pause.GetCombinedMinimumSize().X + _extraItems.Where(i => i.Visible).Sum(i => i.GetCombinedMinimumSize().X);
        var visibleExtras = _extraItems.Count(i => i.Visible) + (capital.Visible ? 1 : 0) + (_incoming.Visible ? 1 : 0);

        return new TopBarWidths(
            TextWidth(brand, brand.Text),
            _chips.Count * iconWidth + Parts(c => c.Amount),
            Parts(c => c.Rate),
            Parts(c => c.Capacity),
            TextWidth(population, population.Text) + TextWidth(score, score.Text) + statGap,
            TextWidth(populationLabel, populationLabel.Text) + TextWidth(scoreLabel, scoreLabel.Text) + statGap * 2,
            viewVillage + viewWorld + toggleGap,
            iconButton * 2 + toggleGap,
            Warnings(TopBarWarning.Full),
            Warnings(TopBarWarning.Short),
            Warnings(TopBarWarning.TimeOnly),
            fixedParts,
            Gaps: 5 + visibleExtras,
            ChipGaps: _chips.Count - 1);
    }

    private void ApplyPlan()
    {
        var box = GetNode<HBoxContainer>("%Box");
        box.AddThemeConstantOverride("separation", (int)(_plan.WideSpacing ? TopBarLayout.WideGap : TopBarLayout.NarrowGap));
        GetNode<HBoxContainer>("%Chips").AddThemeConstantOverride("separation", (int)(_plan.WideSpacing ? TopBarLayout.WideChipGap : TopBarLayout.NarrowChipGap));
        GetNode<Control>("%Brand").Visible = _plan.Brand;
        foreach (var chip in _chips.Values)
        {
            chip.Rate.Visible = _plan.Rates;
            chip.Capacity.Visible = _plan.Capacity;
        }
        GetNode<Control>("%PopulationLabel").Visible = _plan.StatLabels;
        GetNode<Control>("%ScoreLabel").Visible = _plan.StatLabels;

        _viewVillage.Text = _plan.ViewLabels ? L.T("ui.bar.village") : "";
        _viewWorld.Text = _plan.ViewLabels ? L.T("ui.bar.world") : "";
        _viewVillage.Icon = _plan.ViewLabels ? null : GD.Load<Texture2D>(ViewVillageIcon);
        _viewWorld.Icon = _plan.ViewLabels ? null : GD.Load<Texture2D>(ViewWorldIcon);
        var iconSize = _plan.ViewLabels ? Vector2.Zero : new Vector2(IconButtonWidth, 0);
        _viewVillage.CustomMinimumSize = iconSize;
        _viewWorld.CustomMinimumSize = iconSize;
    }
}
