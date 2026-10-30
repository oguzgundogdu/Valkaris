using System;
using System.Linq;
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
    private TabContainer _tabs = null!;
    private TroopsPanel _troopsPanel = null!;
    private Toast _toast = null!;
    private Overlay _pauseOverlay = null!;
    private Overlay _leaveOverlay = null!;
    private Overlay _matchOverOverlay = null!;
    private bool _confirmingLeave;
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
        _tabs = GetNode<TabContainer>("%Side");
        _troopsPanel = GetNode<TroopsPanel>("%Troops");
        _toast = GetNode<Toast>("%Toast");
        _pauseOverlay = GetNode<Overlay>("%PauseOverlay");
        _leaveOverlay = GetNode<Overlay>("%LeaveOverlay");
        _matchOverOverlay = GetNode<Overlay>("%MatchOverOverlay");

        _villageView.BuildingSelected += building =>
        {
            _selected = building;
            ShowTab(SideTab.Village);
        };
        _buildingPanel.TrainingRequested += () => ShowTab(SideTab.Troops);
        _resourceBar.PauseRequested += TogglePause;

        _pauseOverlay.Configure("Paused", "The match is on hold. Press P to resume.", "Resume", "Leave match");
        _pauseOverlay.ActionPressed += TogglePause;
        _pauseOverlay.SecondaryPressed += () => _confirmingLeave = true;

        _leaveOverlay.Configure("Leave match?", "Your progress in this match will be lost.", "Leave match", "Keep playing");
        _leaveOverlay.ActionPressed += () => ExitRequested?.Invoke();
        _leaveOverlay.SecondaryPressed += () => _confirmingLeave = false;

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
        RefreshActiveTab(village);

        _pauseOverlay.Visible = Session.IsPaused && !_confirmingLeave;
        _leaveOverlay.Visible = _confirmingLeave;
        _matchOverOverlay.Visible = Session.State.IsMatchOver;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.P })
            TogglePause();
    }

    public enum SideTab
    {
        Village,
        Troops,
    }

    public void ShowTab(SideTab tab) => _tabs.CurrentTab = (int)tab;

    private void RefreshActiveTab(Village village)
    {
        switch ((SideTab)_tabs.CurrentTab)
        {
            case SideTab.Village:
                _buildingPanel.Refresh(Session, village, _selected);
                _queuePanel.Refresh("Build queue", Session.Data.Game.MaxBuildQueueLength, Session.State.ElapsedTime, village.BuildQueue
                    .Select(item => new QueueRow(
                        UiContent.Buildings[item.Building].IconPath,
                        $"{UiContent.Buildings[item.Building].Name} → level {item.TargetLevel}",
                        item.StartTime,
                        item.FinishTime))
                    .ToList());
                break;
            case SideTab.Troops:
                _troopsPanel.Refresh(Session, village);
                break;
        }
    }

    private void TogglePause()
    {
        if (Session.CanPause && !Session.State.IsMatchOver && !_confirmingLeave)
            Session.SetPaused(!Session.IsPaused);
    }

    private void OnGameEvent(GameEvent gameEvent)
    {
        switch (gameEvent)
        {
            case BuildingCompleted completed when IsLocalVillage(completed.VillageId):
                _villageView.Pulse(completed.Building);
                _toast.ShowMessage($"{UiContent.Buildings[completed.Building].Name} reached level {completed.NewLevel}");
                break;
            case TroopsTrained trained when IsLocalVillage(trained.VillageId):
                _toast.ShowMessage($"{UiContent.Units[trained.Unit].Name} × {trained.Count} trained");
                break;
        }
    }

    private bool IsLocalVillage(int villageId) => Session.State.Villages[villageId].OwnerId == Session.LocalPlayerId;
}
