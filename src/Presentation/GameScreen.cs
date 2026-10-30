using System;
using Godot;
using Valkaris.Core.Commands;
using Valkaris.Core.Events;
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
    private Toast _toast = null!;
    private Overlay _pauseOverlay = null!;
    private Overlay _matchOverOverlay = null!;
    private BuildingType _selected = BuildingType.MainBuilding;

    public IGameSession Session { get; private set; } = null!;

    public event Action? ExitRequested;

    public void Init(IGameSession session) => Session = session;

    public override void _Ready()
    {
        _resourceBar = GetNode<ResourceBar>("%ResourceBar");
        _villageView = GetNode<VillageView>("%VillageView");
        _buildingPanel = GetNode<BuildingPanel>("%BuildingPanel");
        _queuePanel = GetNode<QueuePanel>("%QueuePanel");
        _toast = GetNode<Toast>("%Toast");
        _pauseOverlay = GetNode<Overlay>("%PauseOverlay");
        _matchOverOverlay = GetNode<Overlay>("%MatchOverOverlay");

        _villageView.BuildingSelected += building => _selected = building;
        _resourceBar.PauseRequested += TogglePause;

        _pauseOverlay.Configure("Paused", "The match is on hold. Press P to resume.", "Resume");
        _pauseOverlay.ActionPressed += TogglePause;

        _matchOverOverlay.Configure("Time's up", "The match has ended.", "Back to start");
        _matchOverOverlay.ActionPressed += () => ExitRequested?.Invoke();

        Session.EventRaised += OnGameEvent;
        Session.CommandRejected += rejection => _toast.ShowMessage(UiContent.RejectionText(rejection));
    }

    public override void _Process(double delta)
    {
        Session.Tick(delta);

        var village = Session.State.Villages[Session.State.Players[Session.LocalPlayerId].CapitalVillageId];
        _resourceBar.Refresh(Session, village);
        _villageView.Refresh(village, Session.State.ElapsedTime, _selected);
        _buildingPanel.Refresh(Session, village, _selected);
        _queuePanel.Refresh(Session, village);

        _pauseOverlay.Visible = Session.IsPaused;
        _matchOverOverlay.Visible = Session.State.IsMatchOver;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.P })
            TogglePause();
    }

    private void TogglePause()
    {
        if (Session.CanPause && !Session.State.IsMatchOver)
            Session.SetPaused(!Session.IsPaused);
    }

    private void OnGameEvent(GameEvent gameEvent)
    {
        if (gameEvent is not BuildingCompleted completed)
            return;
        if (Session.State.Villages[completed.VillageId].OwnerId != Session.LocalPlayerId)
            return;

        _villageView.Pulse(completed.Building);
        _toast.ShowMessage($"{UiContent.Buildings[completed.Building].Name} reached level {completed.NewLevel}");
    }
}
