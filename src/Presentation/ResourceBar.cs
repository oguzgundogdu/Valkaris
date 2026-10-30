using System;
using System.Collections.Generic;
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

    public override void _Ready()
    {
        _clock = GetNode<Label>("%Clock");

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

    public void Refresh(IGameSession session, Village village)
    {
        var capacity = BuildingRules.VillageCapacity(session.Data, village);
        var production = BuildingRules.VillageProduction(session.Data, village) * session.State.Settings.SpeedMultiplier;

        foreach (var (type, chip) in _chips)
        {
            chip.Amount.Text = Format.Amount(village.Resources.Get(type));
            chip.Capacity.Text = $"/ {Format.Amount(capacity)}";
            chip.Rate.Text = Format.Rate(production.Get(type));
        }

        var remaining = session.State.Settings.MatchDurationSeconds - session.State.ElapsedTime;
        _clock.Text = Format.Time(remaining);
    }
}
