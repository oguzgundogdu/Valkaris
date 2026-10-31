using System;
using System.Linq;
using Godot;
using Valkaris.Core.Commands;
using Valkaris.Core.Events;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

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
    private Overlay _surrenderOverlay = null!;
    private Overlay _eliminatedOverlay = null!;
    private CapitalDialog _capitalDialog = null!;
    private MatchEndScreen _matchEndScreen = null!;
    private bool _capitalSetAside;
    private bool _watching;
    private bool _confirmingLeave;
    private bool _confirmingSurrender;
    private BuildingType _selected = BuildingType.MainBuilding;
    private int? _currentVillageId;
    private VillageStrip _villageStrip = null!;
    private Label _villageInfo = null!;
    private Tween? _viewTween;

    public IGameSession Session { get; private set; } = null!;

    public MainView CurrentView { get; private set; } = MainView.Village;

    public int? CurrentVillageId => _currentVillageId;

    public event Action? ExitRequested;

    public void ExitToStart() => ExitRequested?.Invoke();

    public void Init(IGameSession session)
    {
        Session = session;
        _currentVillageId = session.State.Players[session.LocalPlayerId].CapitalVillageId!.Value;
    }

    public override void _Ready()
    {
        _resourceBar = GetNode<ResourceBar>("%ResourceBar");
        _villageStrip = GetNode<VillageStrip>("%VillageStrip");
        _villageInfo = GetNode<Label>("%VillageInfo");
        _villageStrip.VillageSelected += SwitchVillage;
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
        _reportsPanel.ScoutReportOpened += report => _reportDialog.ShowScoutReport(Session, report);
        _reportsPanel.ScoutNoticeOpened += notice => _reportDialog.ShowScoutNotice(Session, notice);
        _toast = GetNode<Toast>("%Toast");
        _pauseOverlay = GetNode<Overlay>("%PauseOverlay");
        _leaveOverlay = GetNode<Overlay>("%LeaveOverlay");
        _surrenderOverlay = GetNode<Overlay>("%SurrenderOverlay");
        _eliminatedOverlay = GetNode<Overlay>("%EliminatedOverlay");
        _capitalDialog = GetNode<CapitalDialog>("%CapitalDialog");
        _matchEndScreen = GetNode<MatchEndScreen>("%MatchEndScreen");

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
        _worldMap.FoundRequested += tile =>
        {
            if (_currentVillageId is { } from)
                Session.Send(new FoundVillageCommand(from, tile));
        };
        _worldMap.SendRequested += villageId =>
        {
            _sendPanel.SetTarget(villageId);
            ShowTab(SideTab.Send);
        };
        _worldMap.ReinforceRequested += villageId =>
        {
            _sendPanel.SetTarget(villageId);
            ShowTab(SideTab.Send);
        };
        _worldMap.SendResourcesRequested += villageId =>
        {
            _sendPanel.SetTarget(villageId, sendResources: true);
            ShowTab(SideTab.Send);
        };
        _buildingPanel.SendResourcesRequested += () =>
        {
            _sendPanel.ShowResourceForm();
            ShowTab(SideTab.Send);
        };
        _sendPanel.MapRequested += () => ShowView(MainView.World);

        _pauseOverlay.Configure("Paused", "The match is on hold. Press P to resume.", "Resume", "Leave match");
        _pauseOverlay.ActionPressed += TogglePause;
        _pauseOverlay.SecondaryPressed += () => _confirmingLeave = true;

        _leaveOverlay.Configure("Leave match?", "Your progress in this match will be lost.", "Leave match", "Keep playing");
        _leaveOverlay.ActionPressed += () => ExitRequested?.Invoke();
        _leaveOverlay.SecondaryPressed += () => _confirmingLeave = false;

        _pauseOverlay.TertiaryPressed += () => _confirmingSurrender = true;
        _surrenderOverlay.Configure(
            "Surrender?",
            "You lose this match. Your villages stay on the map as barbarian villages, with their troops and resources, for others to raid or capture. Troops on the way are lost.",
            "Surrender",
            "Keep playing");
        _surrenderOverlay.ActionPressed += Surrender;
        _surrenderOverlay.SecondaryPressed += () => _confirmingSurrender = false;

        _matchEndScreen.BackRequested += () => ExitRequested?.Invoke();

        _eliminatedOverlay.Configure("You were eliminated", "You have no villages left. The match goes on without you.", "Keep watching", "Leave match");
        _eliminatedOverlay.ActionPressed += () => _watching = true;
        _eliminatedOverlay.SecondaryPressed += () => ExitRequested?.Invoke();

        _capitalDialog.CapitalChosen += villageId => Session.Send(new SetCapitalCommand(villageId));
        _capitalDialog.Dismissed += () => _capitalSetAside = true;
        _resourceBar.CapitalWarningPressed += () => _capitalSetAside = false;

        Session.EventRaised += OnGameEvent;
        Session.CommandRejected += rejection => _toast.ShowMessage(UiContent.RejectionText(rejection));
    }

    public override void _Process(double delta)
    {
        Session.Tick(delta);

        var village = ResolveCurrentVillage();
        _resourceBar.Refresh(Session, village, CurrentView);
        _villageStrip.Refresh(Session, _currentVillageId);
        if (CurrentView == MainView.Village && village is not null)
            _villageView.Refresh(village, Session.State.ElapsedTime, _selected);
        else
            _worldMap.Refresh(Session, village);
        RefreshActiveTab(village);

        _pauseOverlay.Visible = Session.IsPaused && !_confirmingLeave && !_confirmingSurrender;
        _pauseOverlay.SetTertiary(Session.State.Players[Session.LocalPlayerId].IsEliminated ? null : "Surrender");
        _leaveOverlay.Visible = _confirmingLeave;
        _surrenderOverlay.Visible = _confirmingSurrender;
        RefreshDialogs();
    }

    private void RefreshDialogs()
    {
        var state = Session.State;
        var player = state.Players[Session.LocalPlayerId];

        if (player.CapitalChoiceDeadline is null)
            _capitalSetAside = false;

        var deadline = state.IsMatchOver ? null : player.CapitalChoiceDeadline;
        _capitalDialog.Visible = deadline is not null && !_capitalSetAside;
        if (deadline is { } time)
        {
            if (_capitalDialog.Visible)
                _capitalDialog.Refresh(Session, time);
            _resourceBar.RefreshCapitalWarning(_capitalSetAside ? time - state.ElapsedTime : null);
        }
        else
        {
            _resourceBar.RefreshCapitalWarning(null);
        }

        _eliminatedOverlay.Visible = player.IsEliminated && !_watching && !state.IsMatchOver;
        if (_eliminatedOverlay.Visible && player.HasSurrendered)
            _eliminatedOverlay.Configure("You surrendered", "Your villages now belong to the barbarians. The match goes on without you.", "Keep watching", "Leave match");

        if (state.Outcome is { } outcome && !_matchEndScreen.Visible)
            _matchEndScreen.ShowOutcome(Session, outcome);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;

        if (key.Keycode == Key.P)
            TogglePause();
        else if (Session.IsPaused || Session.State.IsMatchOver || _currentVillageId is null || _capitalDialog.Visible)
            return;
        else if (key.Keycode == Key.M)
            ShowView(CurrentView == MainView.Village ? MainView.World : MainView.Village);
        else if (key.Keycode == Key.Q)
            CycleVillage(-1);
        else if (key.Keycode == Key.E)
            CycleVillage(1);
    }

    private void CycleVillage(int direction)
    {
        var villages = UiContent.OwnVillages(Session);
        if (villages.Count < 2)
            return;

        var index = villages.ToList().FindIndex(v => v.Id == _currentVillageId);
        SwitchVillage(villages[(index + direction + villages.Count) % villages.Count].Id);
    }

    /// <summary>Keeps the view, the tab and the Send target. On the map, the camera follows.</summary>
    public void SwitchVillage(int villageId)
    {
        if (villageId == _currentVillageId)
            return;

        _currentVillageId = villageId;
        _sendPanel.ClearAmounts();
        if (CurrentView == MainView.World)
            _worldMap.CentreOn(Session.State.Villages[villageId]);
    }

    /// <summary>Moves off a village that was lost: to the capital if there is one. Null once the player has no villages.</summary>
    private Village? ResolveCurrentVillage()
    {
        if (_currentVillageId is { } id && Session.State.Villages[id].OwnerId == Session.LocalPlayerId)
            return Session.State.Villages[id];

        var player = Session.State.Players[Session.LocalPlayerId];
        var next = player.CapitalVillageId is { } capital
            ? Session.State.Villages[capital]
            : UiContent.OwnVillages(Session).FirstOrDefault();

        _currentVillageId = next?.Id;
        _sendPanel.ClearAmounts();
        if (next is null)
            StartWatching();
        return next;
    }

    /// <summary>A player with no villages keeps only the map and the reports.</summary>
    private void StartWatching()
    {
        ShowView(MainView.World);
        ShowTab(SideTab.Reports);
        _tabs.SetTabHidden((int)SideTab.Village, true);
        _tabs.SetTabHidden((int)SideTab.Troops, true);
        _tabs.SetTabHidden((int)SideTab.Send, true);
    }

    private void EnterVillage(int villageId)
    {
        _currentVillageId = villageId;
        _sendPanel.ClearAmounts();
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
            _worldMap.Refresh(Session, _currentVillageId is { } id ? Session.State.Villages[id] : null);

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

    private void RefreshVillageInfo(Village village)
    {
        var text = $"{village.Name}  ·  Loyalty {Format.Amount(village.Loyalty)}";
        if (LoyaltyRules.IsCapital(Session.State, village))
        {
            var threshold = Session.Data.Villages.Capital.LockedAboveMainLevel;
            text += LoyaltyRules.IsLocked(Session.State, Session.Data, village)
                ? $"\nCapital: protected while its Main Building is above level {threshold}"
                : $"\nCapital: not protected until its Main Building is above level {threshold}";
        }
        _villageInfo.Text = text;
    }

    private void RefreshActiveTab(Village? village)
    {
        var unread = _reportsPanel.UnreadCount(Session);
        _tabs.SetTabTitle((int)SideTab.Reports, unread > 0 ? $"Reports ({unread})" : "Reports");

        if (village is null)
        {
            _reportsPanel.Refresh(Session);
            _reportsPanel.MarkAllRead(Session);
            return;
        }

        switch ((SideTab)_tabs.CurrentTab)
        {
            case SideTab.Village:
                RefreshVillageInfo(village);
                _buildingPanel.Refresh(Session, village, _selected);
                _queuePanel.Refresh("Build queue", Session.Data.Game.MaxBuildQueueLength, Session.State.ElapsedTime, village.BuildQueue
                    .Select(item => new QueueRow(
                        UiContent.Buildings[item.Building].IconPath,
                        $"{UiContent.Buildings[item.Building].Name} › level {item.TargetLevel}",
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
    }

    /// <summary>Commands don't run while paused, and the match resumes for whoever is left.</summary>
    private void Surrender()
    {
        _confirmingSurrender = false;
        Session.SetPaused(false);
        Session.Send(new SurrenderCommand());
    }

    private void TogglePause()
    {
        if (Session.CanPause && !Session.State.IsMatchOver && !_confirmingLeave && !_confirmingSurrender)
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
            case ScoutResolved scouted:
                var scoutReport = Session.State.ScoutReports.First(r => r.Id == scouted.ReportId);
                if (scoutReport.SenderId == Session.LocalPlayerId)
                    _toast.ShowMessage($"{UiContent.ScoutHeadline(Session, scoutReport)}. See Reports.");
                break;
            case ScoutNoticed noticed:
                var notice = Session.State.ScoutNotices.First(n => n.Id == noticed.NoticeId);
                if (notice.DefenderId == Session.LocalPlayerId)
                    _toast.ShowMessage($"{UiContent.NoticeHeadline(Session, notice)}. See Reports.");
                break;
            case TroopsReturned returned when IsLocalVillage(returned.VillageId):
                var loot = returned.Loot.Wood + returned.Loot.Stone + returned.Loot.Food;
                _toast.ShowMessage(loot > 0 ? $"Troops returned with {Format.Amount(loot)} loot" : "Troops returned");
                break;
            case TroopsTrained trained when IsLocalVillage(trained.VillageId):
                _toast.ShowMessage($"{UiContent.Units[trained.Unit].Name} × {trained.Count} trained");
                break;
            case ReinforcementsArrived arrived when IsLocalVillage(arrived.VillageId):
                _toast.ShowMessage($"Troops arrived at {Session.State.Villages[arrived.VillageId].Name}");
                break;
            case ResourcesDelivered delivered when IsLocalVillage(delivered.VillageId):
                _toast.ShowMessage(DeliveryText(delivered.VillageId, delivered.Delivered, delivered.Lost));
                break;
            case CarriersReturned returned when IsLocalVillage(returned.VillageId) && returned.Delivered + returned.Lost != Resources.Zero:
                _toast.ShowMessage(DeliveryText(returned.VillageId, returned.Delivered, returned.Lost));
                break;
            case VillageFounded founded when IsLocalVillage(founded.VillageId):
                _toast.ShowMessage($"{Session.State.Villages[founded.VillageId].Name} was founded");
                break;
            case SettlingFailed failed when failed.OwnerId == Session.LocalPlayerId:
                _toast.ShowMessage($"Settler turned back. {UiContent.RejectionText(failed.Reason)}");
                break;
            case VillageCaptured captured when captured.NewOwnerId == Session.LocalPlayerId:
                _toast.ShowMessage($"You captured {Session.State.Villages[captured.VillageId].Name}");
                break;
            case VillageCaptured captured when captured.PreviousOwnerId == Session.LocalPlayerId:
                _toast.ShowMessage($"{Session.State.Villages[captured.VillageId].Name} was captured by {UiContent.OwnerName(Session, captured.NewOwnerId)}");
                break;
            case PlayerSurrendered surrendered when surrendered.PlayerId != Session.LocalPlayerId:
                _toast.ShowMessage($"{UiContent.OwnerName(Session, surrendered.PlayerId)} surrendered");
                break;
            case PlayerEliminated eliminated when eliminated.PlayerId != Session.LocalPlayerId:
                _toast.ShowMessage($"{UiContent.OwnerName(Session, eliminated.PlayerId)} was eliminated");
                break;
        }
    }

    private string DeliveryText(int villageId, Resources delivered, Resources lost)
    {
        var text = $"{Format.Total(delivered)} delivered to {Session.State.Villages[villageId].Name}";
        return lost == Resources.Zero ? text : $"{text}, {Format.Total(lost)} lost: warehouse full";
    }

    private bool IsLocalVillage(int villageId) => Session.State.Villages[villageId].OwnerId == Session.LocalPlayerId;
}
