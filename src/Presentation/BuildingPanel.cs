using System;
using System.Collections.Generic;
using Godot;
using Valkaris.Core.Commands;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>Details of the selected building and its upgrade button.</summary>
public partial class BuildingPanel : PanelContainer
{
    private readonly Dictionary<ResourceType, Label> _costs = new();
    private TextureRect _icon = null!;
    private Label _name = null!;
    private Label _level = null!;
    private Label _description = null!;
    private Label _effect = null!;
    private Control _costSection = null!;
    private Label _time = null!;
    private Button _upgrade = null!;
    private Label _reason = null!;

    private IGameSession? _session;
    private int _villageId;
    private BuildingType _building;

    public override void _Ready()
    {
        _icon = GetNode<TextureRect>("%Icon");
        _name = GetNode<Label>("%Name");
        _level = GetNode<Label>("%Level");
        _description = GetNode<Label>("%Description");
        _effect = GetNode<Label>("%Effect");
        _costSection = GetNode<Control>("%CostSection");
        _time = GetNode<Label>("%Time");
        _upgrade = GetNode<Button>("%Upgrade");
        _reason = GetNode<Label>("%Reason");

        var costs = GetNode<HBoxContainer>("%Costs");
        foreach (var type in Enum.GetValues<ResourceType>())
        {
            var box = new HBoxContainer();
            box.AddThemeConstantOverride("separation", 4);
            box.AddChild(new TextureRect
            {
                Texture = GD.Load<Texture2D>(UiContent.ResourceIconPath(type)),
                CustomMinimumSize = new Vector2(20, 20),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            });
            var amount = new Label { ThemeTypeVariation = "StrongLabel", Name = $"Cost{type}" };
            box.AddChild(amount);
            costs.AddChild(box);
            _costs[type] = amount;
        }

        _upgrade.Pressed += () => _session?.Send(new BuildCommand(_villageId, _building));
    }

    public void Refresh(IGameSession session, Village village, BuildingType building)
    {
        _session = session;
        _villageId = village.Id;
        _building = building;

        var info = UiContent.Buildings[building];
        var level = village.BuildingLevel(building);
        var option = BuildPreview.Evaluate(session.State, session.Data, session.LocalPlayerId, village.Id, building);
        var atMax = option.TargetLevel > session.Data.Buildings.Buildings[building].MaxLevel;

        _icon.Texture = GD.Load<Texture2D>(info.IconPath);
        _name.Text = info.Name;
        _level.Text = level == 0 ? "Not built" : $"Level {level}";
        _description.Text = info.Description;

        // A rejection such as "match over" comes with no target level at all.
        var hasNext = !atMax && option.TargetLevel > 0;

        var effect = EffectText(session, building, level, option.TargetLevel, hasNext);
        _effect.Text = effect;
        _effect.Visible = effect.Length > 0;

        _costSection.Visible = hasNext;
        foreach (var (type, label) in _costs)
        {
            label.Text = Format.Amount(option.Cost.Get(type));
            var shortOf = village.Resources.Get(type) < option.Cost.Get(type);
            label.AddThemeColorOverride("font_color", shortOf ? UiContent.Danger : UiContent.Text);
        }
        _time.Text = Format.Time(option.DurationSeconds);

        _upgrade.Disabled = !option.CanBuild;
        _upgrade.Text = atMax ? "Max level"
            : !hasNext ? "Unavailable"
            : option.TargetLevel == 1 ? "Build"
            : $"Upgrade to level {option.TargetLevel}";

        var reason = ReasonText(option);
        _reason.Text = reason;
        _reason.Visible = reason.Length > 0;
    }

    private static string EffectText(IGameSession session, BuildingType building, int level, int nextLevel, bool hasNext)
    {
        var data = session.Data;
        var definition = data.Buildings.Buildings[building];

        string Compare(Func<int, string> describe) =>
            hasNext ? $"Now {describe(level)}   →   Level {nextLevel}: {describe(nextLevel)}" : $"Now {describe(level)}";

        if (definition.Production is { } production)
        {
            var speed = session.State.Settings.SpeedMultiplier;
            return Compare(l => Format.Rate(BuildingRules.Production(data, building, l).Get(production.Resource) * speed));
        }

        if (building == BuildingType.Warehouse)
            return Compare(l => $"holds {Format.Amount(BuildingRules.WarehouseCapacity(data, l))}");

        if (building == BuildingType.MainBuilding)
            return Compare(l => $"others up to level {BuildingRules.MaxOtherBuildingLevel(data, l)}");

        return "";
    }

    private static string ReasonText(BuildOption option) => option.Rejection switch
    {
        null or CommandRejection.MaxLevelReached => "",
        CommandRejection.InsufficientResources when option.CostExceedsCapacity =>
            "This costs more than your Warehouse can hold. Upgrade the Warehouse first.",
        { } rejection => UiContent.RejectionText(rejection),
    };
}
