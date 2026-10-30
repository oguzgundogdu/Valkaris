using System;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>Owns the session, drives it every frame and refreshes the views from its state.</summary>
public partial class GameScreen : Control
{
    private ResourceBar _resourceBar = null!;
    private BuildingPanel _buildingPanel = null!;
    private QueuePanel _queuePanel = null!;
    private BuildingType _selected = BuildingType.MainBuilding;

    public IGameSession Session { get; private set; } = null!;

    public void Init(IGameSession session) => Session = session;

    public override void _Ready()
    {
        _resourceBar = GetNode<ResourceBar>("%ResourceBar");
        _buildingPanel = GetNode<BuildingPanel>("%BuildingPanel");
        _queuePanel = GetNode<QueuePanel>("%QueuePanel");

        // Temporary list until the village ground replaces it.
        var list = new VBoxContainer();
        foreach (var building in Enum.GetValues<BuildingType>())
        {
            var button = new Button { Name = building.ToString(), Text = UiContent.Buildings[building].Name, ThemeTypeVariation = "SecondaryButton" };
            button.Pressed += () => Select(building);
            list.AddChild(button);
        }
        GetNode("%VillageArea").AddChild(list);
    }

    public void Select(BuildingType building) => _selected = building;

    public override void _Process(double delta)
    {
        Session.Tick(delta);

        var village = Session.State.Villages[Session.State.Players[Session.LocalPlayerId].CapitalVillageId];
        _resourceBar.Refresh(Session, village);
        _buildingPanel.Refresh(Session, village, _selected);
        _queuePanel.Refresh(Session, village);
    }
}
