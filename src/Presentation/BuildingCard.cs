using System;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>A building's details while hovering it. Clicking the building opens the full popup.</summary>
public partial class BuildingCard : PanelContainer
{
    private const float Width = 280;

    private Label _name = null!;
    private Label _level = null!;
    private Label _description = null!;
    private CostView _cost = null!;
    private Button _action = null!;
    private BuildingType _building;

    public event Action? TrainingRequested;
    public event Action? SendResourcesRequested;

    public override void _Ready()
    {
        ThemeTypeVariation = "CardPanel";
        var box = new VBoxContainer { Name = "Box", CustomMinimumSize = new Vector2(Width, 0) };
        box.AddThemeConstantOverride("separation", 6);
        AddChild(box);

        _name = new Label { Name = "Name", ThemeTypeVariation = "HeadingLabel" };
        _level = new Label { Name = "Level", ThemeTypeVariation = "MutedLabel" };
        _description = new Label { Name = "Description", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _cost = new CostView { Name = "Cost" };
        _action = new Button { Name = "Action", FocusMode = FocusModeEnum.None };
        _action.Pressed += () =>
        {
            if (_building == BuildingType.Barracks)
                TrainingRequested?.Invoke();
            else
                SendResourcesRequested?.Invoke();
        };

        foreach (var part in new Control[] { _name, _level, _description, _cost, _action })
            box.AddChild(part);
    }

    public void Refresh(IGameSession session, Village village, BuildingType building)
    {
        _building = building;
        var info = UiContent.Buildings[building];
        var level = village.BuildingLevel(building);
        var option = BuildPreview.Evaluate(session.State, session.Data, session.LocalPlayerId, village.Id, building);
        var hasNext = option.TargetLevel > 0 && option.TargetLevel <= session.Data.Buildings.Buildings[building].MaxLevel;

        _name.Text = info.Name;
        _level.Text = level == 0 ? L.T("common.not_built") : L.T("common.level", ("level", Format.Whole(level)));
        _description.Text = info.Description;
        _cost.Visible = hasNext;
        if (hasNext)
            _cost.Refresh(option.Cost, village.Resources);

        _action.Visible = building is BuildingType.Barracks or BuildingType.Marketplace;
        _action.Text = L.T(building == BuildingType.Barracks ? "card.train" : "card.send_resources");
    }
}
