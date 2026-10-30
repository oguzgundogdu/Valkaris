using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>Owns the session, drives it every frame and refreshes the views from its state.</summary>
public partial class GameScreen : Control
{
    private ResourceBar _resourceBar = null!;
    private VillageView _villageView = null!;
    private BuildingPanel _buildingPanel = null!;
    private QueuePanel _queuePanel = null!;
    private BuildingType _selected = BuildingType.MainBuilding;

    public IGameSession Session { get; private set; } = null!;

    public void Init(IGameSession session) => Session = session;

    public override void _Ready()
    {
        _resourceBar = GetNode<ResourceBar>("%ResourceBar");
        _villageView = GetNode<VillageView>("%VillageView");
        _buildingPanel = GetNode<BuildingPanel>("%BuildingPanel");
        _queuePanel = GetNode<QueuePanel>("%QueuePanel");

        _villageView.BuildingSelected += building => _selected = building;
    }

    public override void _Process(double delta)
    {
        Session.Tick(delta);

        var village = Session.State.Villages[Session.State.Players[Session.LocalPlayerId].CapitalVillageId];
        _resourceBar.Refresh(Session, village);
        _villageView.Refresh(village, Session.State.ElapsedTime, _selected);
        _buildingPanel.Refresh(Session, village, _selected);
        _queuePanel.Refresh(Session, village);
    }
}
