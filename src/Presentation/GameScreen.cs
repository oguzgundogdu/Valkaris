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
    private const double ViewFadeSeconds = 0.12;

    private ResourceBar _resourceBar = null!;
    private VillageView _villageView = null!;
    private WorldMapView _worldMap = null!;
    private BuildingPanel _buildingPanel = null!;
    private QueuePanel _queuePanel = null!;
    private TabContainer _tabs = null!;
    private TroopsPanel _troopsPanel = null!;
    private SendPanel _sendPanel = null!;
    private ReportsPanel _reportsPanel = null!;
    private ReportDialog _reportDialog = null!;
    private Toast _toast = null!;
    private Overlay _pauseOverlay = null!;
    private Overlay _leaveOverlay = null!;
    private Overlay _matchOverOverlay = null!;
    private bool _confirmingLeave;
    private double? _devRivalAttackAt;
    private BuildingType _selected = BuildingType.MainBuilding;
    private int _currentVillageId;
    private Tween? _viewTween;

    public IGameSession Session { get; private set; } = null!;

    public MainView CurrentView { get; private set; } = MainView.Village;

    public event Action? ExitRequested;

    public void Init(IGameSession session, double? devRivalAttackSeconds = null)
    {
        Session = session;
        _currentVillageId = session.State.Players[session.LocalPlayerId].CapitalVillageId!.Value;
        _devRivalAttackAt = devRivalAttackSeconds;
    }

    public override void _Ready()
    {
        _resourceBar = GetNode<ResourceBar>("%ResourceBar");
        _villageView = GetNode<VillageView>("%VillageView");
        _worldMap = GetNode<WorldMapView>("%WorldMapView");
        _buildingPanel = GetNode<BuildingPanel>("%BuildingPanel");
        _queuePanel = GetNode<QueuePanel>("%QueuePanel");
        _tabs = GetNode<TabContainer>("%Side");
        _troopsPanel = GetNode<TroopsPanel>("%Troops");
        _sendPanel = GetNode<SendPanel>("%Send");
        _reportsPanel = GetNode<ReportsPanel>("%Reports");
        _reportDialog = GetNode<ReportDialog>("%ReportDialog");
        _reportsPanel.ReportOpened += report => _reportDialog.ShowReport(Session, report);
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
        _resourceBar.ViewRequested += ShowView;
        _resourceBar.IncomingPressed += villageId =>
        {
            ShowView(MainView.World);
            _worldMap.CentreOn(Session.State.Villages[villageId]);
        };
        _worldMap.EnterRequested += EnterVillage;
        _worldMap.SendRequested += villageId =>
        {
            _sendPanel.SetTarget(villageId);
            ShowTab(SideTab.Send);
        };
        _sendPanel.MapRequested += () => ShowView(MainView.World);

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
        RunDevRivalAttack();

        var village = Session.State.Villages[_currentVillageId];
        _resourceBar.Refresh(Session, village, CurrentView);
        if (CurrentView == MainView.Village)
            _villageView.Refresh(village, Session.State.ElapsedTime, _selected);
        else
            _worldMap.Refresh(Session, village);
        RefreshActiveTab(village);

        _pauseOverlay.Visible = Session.IsPaused && !_confirmingLeave;
        _leaveOverlay.Visible = _confirmingLeave;
        _matchOverOverlay.Visible = Session.State.IsMatchOver;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;

        if (key.Keycode == Key.P)
            TogglePause();
        else if (key.Keycode == Key.M && !Session.IsPaused && !Session.State.IsMatchOver)
            ShowView(CurrentView == MainView.Village ? MainView.World : MainView.Village);
    }

    /// <summary>Dev only: the idle rival sends its whole garrison, so being attacked can be tried before AI exists.</summary>
    private void RunDevRivalAttack()
    {
        if (_devRivalAttackAt is not { } time || Session.State.ElapsedTime < time || Session is not LocalGameSession local)
            return;

        _devRivalAttackAt = null;
        var rival = Session.State.Villages.Values
            .Where(v => v.OwnerId != Session.LocalPlayerId && !v.IsBarbarian)
            .MaxBy(v => v.Troops.Values.Sum())!;
        var home = Session.State.Players[Session.LocalPlayerId].CapitalVillageId!.Value;
        local.SendAs(rival.OwnerId, new SendTroopsCommand(rival.Id, home, MovementType.Attack, rival.Troops.ToDictionary(t => t.Key, t => t.Value)));
    }

    private void EnterVillage(int villageId)
    {
        _currentVillageId = villageId;
        _worldMap.ClearSelection();
        ShowTab(SideTab.Village);
        ShowView(MainView.Village);
    }

    public enum MainView
    {
        Village,
        World,
    }

    /// <summary>Both views stay alive, so the camera and selection survive switching. A short fade softens the cut.</summary>
    public void ShowView(MainView view)
    {
        if (view == CurrentView)
            return;

        CurrentView = view;
        Control incoming = view == MainView.World ? _worldMap : _villageView;
        Control outgoing = view == MainView.World ? _villageView : _worldMap;

        if (view == MainView.World)
            _worldMap.Refresh(Session, Session.State.Villages[_currentVillageId]);

        _viewTween?.Kill();
        incoming.Visible = true;
        incoming.Modulate = new Color(1, 1, 1, 0);
        incoming.GetParent().MoveChild(incoming, -1);
        outgoing.Visible = false;
        outgoing.Modulate = Colors.White;

        _viewTween = CreateTween();
        _viewTween.TweenProperty(incoming, "modulate:a", 1f, ViewFadeSeconds);
    }

    public enum SideTab
    {
        Village,
        Troops,
        Send,
        Reports,
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
            case SideTab.Send:
                _sendPanel.Refresh(Session, village, CurrentView == MainView.World);
                break;
            case SideTab.Reports:
                _reportsPanel.Refresh(Session);
                _reportsPanel.MarkAllRead(Session);
                break;
        }

        var unread = _reportsPanel.UnreadCount(Session);
        _tabs.SetTabTitle((int)SideTab.Reports, unread > 0 ? $"Reports ({unread})" : "Reports");
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
            case BattleResolved resolved:
                var report = Session.State.Reports.First(r => r.Id == resolved.ReportId);
                if (report.AttackerId == Session.LocalPlayerId || report.DefenderId == Session.LocalPlayerId)
                    _toast.ShowMessage($"{UiContent.ReportHeadline(Session, report)}. See Reports.");
                break;
            case TroopsReturned returned when IsLocalVillage(returned.VillageId):
                var loot = returned.Loot.Wood + returned.Loot.Stone + returned.Loot.Food;
                _toast.ShowMessage(loot > 0 ? $"Troops returned with {Format.Amount(loot)} loot" : "Troops returned");
                break;
            case TroopsTrained trained when IsLocalVillage(trained.VillageId):
                _toast.ShowMessage($"{UiContent.Units[trained.Unit].Name} × {trained.Count} trained");
                break;
        }
    }

    private bool IsLocalVillage(int villageId) => Session.State.Villages[villageId].OwnerId == Session.LocalPlayerId;
}
