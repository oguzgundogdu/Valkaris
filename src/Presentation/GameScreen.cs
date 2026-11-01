using System;
using System.Collections.Generic;
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
    private const int PopupsAbove = 10;
    private const int OverlaysAbove = 20;

    private ResourceBar _resourceBar = null!;
    private VillageView _villageView = null!;
    private WorldMapView _worldMap = null!;
    private BuildingPanel _buildingPanel = null!;
    private QueuePanel _queuePanel = null!;
    private Drawer _drawer = null!;
    private OverviewPanel _overview = null!;
    private PopupFrame _buildingPopup = null!;
    private PopupFrame _troopsPopup = null!;
    private PopupFrame _sendPopup = null!;
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
    private RenamePanel _renamePanel = null!;
    private Tween? _viewTween;
    private bool _menuOpen;
    private bool _confirmingQuit;
    private Overlay _quitOverlay = null!;
    private SettingsScreen _settings = null!;
    private readonly HashSet<int> _heardAttacks = new();
    private bool _hostLost;

#if VALKARIS_DEV
    /// <summary>How long this screen's update took on the last frame, for the frame report.</summary>
    public double LastFrameSeconds { get; private set; }
#endif

    public IGameSession Session { get; private set; } = null!;

    public MainView CurrentView { get; private set; } = MainView.Village;

    public int? CurrentVillageId => _currentVillageId;

    public event Action? ExitRequested;
    public event Action? QuitRequested;

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
        _renamePanel = new RenamePanel { Name = "RenamePanel" };
        _villageInfo.AddSibling(_renamePanel);
        _villageStrip.VillageSelected += SwitchVillage;
        _villageView = GetNode<VillageView>("%VillageView");
        _worldMap = GetNode<WorldMapView>("%WorldMapView");
        _buildingPanel = GetNode<BuildingPanel>("%BuildingPanel");
        _queuePanel = GetNode<QueuePanel>("%QueuePanel");
        _drawer = GetNode<Drawer>("%Drawer");
        _overview = GetNode<OverviewPanel>("%Overview");
        _overview.VillageSelected += SwitchVillage;
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
        _settings = GetNode<SettingsScreen>("%SettingsScreen");
        _quitOverlay = GetNode<Overlay>("%QuitOverlay");
        _settings.LanguageLocked = true;

        _buildingPopup = GetNode<PopupFrame>("%BuildingPopup");
        _troopsPopup = GetNode<PopupFrame>("%TroopsPopup");
        _sendPopup = GetNode<PopupFrame>("%SendPopup");
        _troopsPopup.SetTitle(L.T("tab.troops"));
        _sendPopup.SetTitle(L.T("tab.send"));

        // Building labels draw above the village, so these need to be higher still.
        GetNode<Control>("%PopupLayer").ZIndex = PopupsAbove;
        _resourceBar.Resized += () => GetNode<Control>("%PopupLayer").OffsetTop = _resourceBar.Size.Y;
        foreach (var top in new Control[] { _toast, _reportDialog, _capitalDialog, _eliminatedOverlay, _pauseOverlay, _leaveOverlay, _surrenderOverlay, _quitOverlay, _settings, _matchEndScreen })
            top.ZIndex = OverlaysAbove;

        _villageView.BuildingSelected += building =>
        {
            _selected = building;
            _villageView.CloseCard();
            ShowPanel(GamePanel.Building);
            _buildingPopup.Open(_villageView.BuildingScreenRect(building));
        };
        _villageView.TrainingRequested += OpenTraining;
        _villageView.SendResourcesRequested += OpenTransfer;
        _buildingPanel.TrainingRequested += OpenTraining;
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
        _worldMap.SendRequested += OpenSend;
        _worldMap.ReinforceRequested += villageId => OpenSend(villageId, MovementType.Reinforce);
        _worldMap.SendResourcesRequested += villageId => OpenSend(villageId, MovementType.Transfer);
        _buildingPanel.SendResourcesRequested += OpenTransfer;
        _sendPanel.MapRequested += () => ShowView(MainView.World);

        if (Session.CanPause)
            _pauseOverlay.Configure(L.T("overlay.paused.title"), L.T("overlay.paused.message"), L.T("overlay.resume"), L.T("overlay.leave"));
        else
            _pauseOverlay.Configure(L.T("overlay.menu.title"), L.T("overlay.menu.message"), L.T("overlay.close"), L.T("overlay.leave"));
        _pauseOverlay.ShowMenuChoices();
        _pauseOverlay.QuitPressed += () => _confirmingQuit = true;
        _quitOverlay.Configure(L.T("overlay.quit.title"), L.T(LeaveMessageKey()), L.T("overlay.quit"), L.T("overlay.keep_playing"));
        _quitOverlay.ActionPressed += () => QuitRequested?.Invoke();
        _quitOverlay.SecondaryPressed += () => _confirmingQuit = false;
        _pauseOverlay.SettingsPressed += _settings.Open;
        _pauseOverlay.ActionPressed += TogglePause;
        _pauseOverlay.SecondaryPressed += () => _confirmingLeave = true;

        _leaveOverlay.Configure(L.T("overlay.leave.title"), L.T(LeaveMessageKey()), L.T("overlay.leave"), L.T("overlay.keep_playing"));
        _leaveOverlay.ActionPressed += () => ExitRequested?.Invoke();
        _leaveOverlay.SecondaryPressed += () => _confirmingLeave = false;

        _pauseOverlay.TertiaryPressed += () => _confirmingSurrender = true;
        _surrenderOverlay.Configure(
            L.T("overlay.surrender.title"),
            L.T("overlay.surrender.message"),
            L.T("overlay.surrender"),
            L.T("overlay.keep_playing"));
        _surrenderOverlay.ActionPressed += Surrender;
        _surrenderOverlay.SecondaryPressed += () => _confirmingSurrender = false;

        _matchEndScreen.BackRequested += () => ExitRequested?.Invoke();

        _eliminatedOverlay.Configure(L.T("overlay.eliminated.title"), L.T("overlay.eliminated.message"), L.T("overlay.keep_watching"), L.T("overlay.leave"));
        _eliminatedOverlay.ActionPressed += () => _watching = true;
        _eliminatedOverlay.SecondaryPressed += () => ExitRequested?.Invoke();

        _capitalDialog.CapitalChosen += villageId => Session.Send(new SetCapitalCommand(villageId));
        _capitalDialog.Dismissed += () => _capitalSetAside = true;
        _resourceBar.CapitalWarningPressed += () => _capitalSetAside = false;


        Session.EventRaised += OnGameEvent;
        if (Session is ClientGameSession client)
            client.ConnectionLost += OnHostLost;
        Session.CommandRejected += rejection => _toast.ShowMessage(UiContent.RejectionText(rejection));
    }

    public override void _Process(double delta)
    {
#if VALKARIS_DEV
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
        Session.Tick(delta);

        var village = ResolveCurrentVillage();
        _resourceBar.Refresh(Session, village, CurrentView);
        _villageStrip.Refresh(Session, _currentVillageId);
        if (CurrentView == MainView.Village && village is not null)
            _villageView.Refresh(Session, village, _selected);
        else
            _worldMap.Refresh(Session, village);
        RefreshActiveTab(village);

        _pauseOverlay.Visible = (Session.IsPaused || _menuOpen) && !_confirmingLeave && !_confirmingSurrender && !_confirmingQuit && !_hostLost && !_settings.Visible;
        _quitOverlay.Visible = _confirmingQuit && !_hostLost;
        _pauseOverlay.SetTertiary(Session.State.Players[Session.LocalPlayerId].IsEliminated ? null : L.T("overlay.surrender"));
        _leaveOverlay.Visible = _confirmingLeave || _hostLost;
        _surrenderOverlay.Visible = _confirmingSurrender && !_hostLost;
        RefreshDialogs();
        WarnOfNewAttacks();
#if VALKARIS_DEV
        LastFrameSeconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds;
#endif
    }

    private void WarnOfNewAttacks()
    {
        foreach (var attack in UiContent.IncomingAttacks(Session))
        {
            if (_heardAttacks.Add(attack.Id))
                Sound.Play(SoundEffect.AttackWarning);
        }
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
            _eliminatedOverlay.Configure(L.T("overlay.surrendered.title"), L.T("overlay.surrendered.message"), L.T("overlay.keep_watching"), L.T("overlay.leave"));

        if (state.Outcome is { } outcome && !_matchEndScreen.Visible)
            _matchEndScreen.ShowOutcome(Session, outcome);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;

        if (key.Keycode == Key.Escape)
        {
            HandleEscape();
            return;
        }

        // Whatever covers the game keeps the keys from reaching it.
        if (_settings.Visible || _reportDialog.Visible || _eliminatedOverlay.Visible || _matchEndScreen.Visible || _confirmingLeave || _confirmingSurrender || _confirmingQuit || _hostLost)
            return;

        if (key.Keycode == Key.P)
            TogglePause();
        else if (Session.IsPaused || _menuOpen || Session.State.IsMatchOver || _currentVillageId is null || _capitalDialog.Visible)
            return;
        else if (key.Keycode == Key.Space && CurrentView == MainView.World)
            _worldMap.ToggleWholeMap();
        else if (CurrentView == MainView.World && IsZoomKey(key, out var zoomIn))
        {
            if (zoomIn)
                _worldMap.ZoomIn();
            else
                _worldMap.ZoomOut();
            GetViewport().SetInputAsHandled();
        }
        else if (HandleMapShortcut(key.Keycode))
            GetViewport().SetInputAsHandled();
        else if (key.Keycode == Key.M)
            ShowView(CurrentView == MainView.Village ? MainView.World : MainView.Village);
        else if (key.Keycode == Key.Q)
            CycleVillage(-1);
        else if (key.Keycode == Key.E)
            CycleVillage(1);
    }

    /// <summary>Closes whatever is on top, one layer per press. The choices that have to be made stay until they are.</summary>
    private void HandleEscape()
    {
        if (_hostLost || _matchEndScreen.Visible)
            return;

        if (_settings.Visible)
            _settings.Close();
        else if (_confirmingLeave)
            _confirmingLeave = false;
        else if (_confirmingQuit)
            _confirmingQuit = false;
        else if (_confirmingSurrender)
            _confirmingSurrender = false;
        else if (Session.IsPaused || _menuOpen)
            TogglePause();
        else if (_eliminatedOverlay.Visible)
            return;
        else if (_reportDialog.Visible)
            _reportDialog.Visible = false;
        else if (_capitalDialog.Visible)
            _capitalSetAside = true;
        else if (!CloseTopPopup())
        {
            if (_drawer.IsOpen)
                _drawer.Close();
            _worldMap.ClearSelection();
        }
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

        var hadVillage = _currentVillageId is not null;
        _currentVillageId = next?.Id;
        _sendPanel.ClearAmounts();
        if (next is null)
            StartWatching(hadVillage);
        return next;
    }

    /// <summary>A player with no villages keeps only the map. The reports open once, and the drawer is theirs after that.</summary>
    private void StartWatching(bool justLost)
    {
        ShowView(MainView.World);
        if (justLost)
            ShowPanel(GamePanel.Reports);
    }

    private void EnterVillage(int villageId)
    {
        _currentVillageId = villageId;
        _sendPanel.ClearAmounts();
        _worldMap.ClearSelection();
        CloseAllPopups();
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
        outgoing.Visible = false;
        outgoing.Modulate = Colors.White;

        _viewTween = CreateTween();
        _viewTween.TweenProperty(incoming, "modulate:a", 1f, ViewFadeSeconds);
    }

    public enum GamePanel
    {
        Building,
        Training,
        Send,
        Overview,
        Reports,
    }

    /// <summary>One popup at a time. Overview and Reports are in the drawer, which can stay open behind a popup.</summary>
    public void ShowPanel(GamePanel panel)
    {
        switch (panel)
        {
            case GamePanel.Overview:
                _drawer.Open(Drawer.Tab.Overview);
                return;
            case GamePanel.Reports:
                _drawer.Open(Drawer.Tab.Reports);
                return;
        }

        CloseAllPopups();
        if (panel == GamePanel.Building)
            _buildingPopup.Open();
        else if (panel == GamePanel.Training)
            _troopsPopup.Open();
        else
            _sendPopup.Open();
    }

    private void CloseAllPopups()
    {
        _buildingPopup.Close();
        _troopsPopup.Close();
        _sendPopup.Close();
    }

    public bool IsShowing(GamePanel panel) => panel switch
    {
        GamePanel.Building => _buildingPopup.Visible,
        GamePanel.Training => _troopsPopup.Visible,
        GamePanel.Send => _sendPopup.Visible,
        GamePanel.Overview => _drawer.IsOpen && _drawer.ActiveTab == Drawer.Tab.Overview,
        _ => _drawer.IsOpen && _drawer.ActiveTab == Drawer.Tab.Reports,
    };

    private void RefreshVillageInfo(Village village)
    {
        var text = L.T("village.info", ("village", village.Name), ("loyalty", Format.Amount(village.Loyalty)));
        if (LoyaltyRules.IsCapital(Session.State, village))
        {
            var threshold = Session.Data.Villages.Capital.LockedAboveMainLevel;
            text += "\n" + L.T(LoyaltyRules.IsLocked(Session.State, Session.Data, village) ? "village.capital_protected" : "village.capital_unprotected", ("level", Format.Whole(threshold)));
        }
        _villageInfo.Text = text;
    }

    private void RefreshActiveTab(Village? village)
    {
        var unread = _reportsPanel.UnreadCount(Session);
        _drawer.SetReportsTitle(unread > 0 ? L.T("tab.reports_unread", ("count", Format.Whole(unread))) : L.T("tab.reports"));

        if (_drawer.IsOpen && _drawer.ActiveTab == Drawer.Tab.Reports)
        {
            _reportsPanel.Refresh(Session);
        }
        if (_drawer.IsOpen && _drawer.ActiveTab == Drawer.Tab.Overview)
            _overview.Refresh(Session);

        if (village is null)
        {
            CloseTopPopup();
            return;
        }

        if (_buildingPopup.Visible)
        {
            _buildingPopup.SetTitle(UiContent.Buildings[_selected].Name);
            // The village's own details belong with its Main Building.
            var isMain = _selected == BuildingType.MainBuilding;
            _villageInfo.Visible = isMain;
            _renamePanel.Visible = isMain;
            RefreshVillageInfo(village);
            _renamePanel.Refresh(Session, village);
            _buildingPanel.Refresh(Session, village, _selected);
            _queuePanel.Refresh(L.T("queue.build"), Session.Data.Game.MaxBuildQueueLength, Session.State.ElapsedTime, village.BuildQueue
                    .Select(item => new QueueRow(
                        UiContent.Buildings[item.Building].IconPath,
                        L.T("queue.build_item", ("building", UiContent.Buildings[item.Building].Name), ("level", Format.Whole(item.TargetLevel))),
                        item.StartTime,
                        item.FinishTime))
                .ToList());
        }
        if (_troopsPopup.Visible)
            _troopsPanel.Refresh(Session, village);
        if (_sendPopup.Visible)
            _sendPanel.Refresh(Session, village, CurrentView == MainView.World);
    }

    /// <summary>Commands don't run while paused, and the match resumes for whoever is left.</summary>
    private void Surrender()
    {
        _confirmingSurrender = false;
        _menuOpen = false;
        if (Session.CanPause)
            Session.SetPaused(false);
        Session.Send(new SurrenderCommand());
    }

    private string LeaveMessageKey() => Session switch
    {
        HostGameSession => "overlay.leave.message_host",
        ClientGameSession => "overlay.leave.message_guest",
        _ => "overlay.leave.message",
    };

    /// <summary>Once the match has ended the results are still worth reading, so only a match under way is cut short.</summary>
    private void OnHostLost()
    {
        if (Session.State.IsMatchOver)
            return;

        _hostLost = true;
        _leaveOverlay.Configure(L.T("overlay.host_left.title"), L.T("overlay.host_left.message"), L.T("overlay.host_left.back"));
    }

    private void OpenTraining()
    {
        _villageView.CloseCard();
        ShowPanel(GamePanel.Training);
        _troopsPopup.Open(_villageView.BuildingScreenRect(BuildingType.Barracks));
    }

    private void OpenTransfer()
    {
        _villageView.CloseCard();
        _sendPanel.ShowResourceForm();
        ShowPanel(GamePanel.Send);
        _sendPopup.Open(_villageView.BuildingScreenRect(BuildingType.Marketplace));
    }

    /// <summary>Opens the send form at a village, beside it on the map.</summary>
    private void OpenSend(int villageId, MovementType mission)
    {
        _sendPanel.SetTarget(villageId, mission);
        ShowPanel(GamePanel.Send);
        _sendPopup.Open(_worldMap.VillageScreenRect(villageId));
        _worldMap.ClearSelection();
    }

    /// <summary>A, R, S, T and Enter act on the village under the cursor. Keys typed into a field never get here.</summary>
    /// <summary>By the character as well, since keyboards put + and - in different places.</summary>
    private static bool IsZoomKey(InputEventKey key, out bool zoomIn)
    {
        zoomIn = key.Unicode == '+' || key.Unicode == '=' || key.Keycode is Key.Plus or Key.Equal or Key.KpAdd;
        return zoomIn || key.Unicode == '-' || key.Keycode is Key.Minus or Key.KpSubtract;
    }

    private bool HandleMapShortcut(Key key)
    {
        if (CurrentView != MainView.World || _currentVillageId is null || _worldMap.HoveredVillageId is not { } id)
            return false;

        var isOwn = Session.State.Villages[id].OwnerId == Session.LocalPlayerId;
        switch (key)
        {
            case Key.A or Key.R or Key.S when isOwn:
                _toast.ShowMessage(UiContent.RejectionText(CommandRejection.InvalidMission));
                return true;
            case Key.A:
                OpenSend(id, MovementType.Attack);
                return true;
            case Key.R:
                OpenSend(id, MovementType.Raid);
                return true;
            case Key.S:
                OpenSend(id, MovementType.Scout);
                return true;
            case Key.T when !isOwn:
                _toast.ShowMessage(UiContent.RejectionText(CommandRejection.TargetNotYours));
                return true;
            case Key.T when id == _currentVillageId:
                _toast.ShowMessage(UiContent.RejectionText(CommandRejection.SameVillage));
                return true;
            case Key.T:
                OpenSend(id, MovementType.Transfer);
                return true;
            case Key.Enter or Key.KpEnter when !isOwn:
                _toast.ShowMessage(UiContent.RejectionText(CommandRejection.NotOwner));
                return true;
            case Key.Enter or Key.KpEnter:
                EnterVillage(id);
                return true;
            default:
                return false;
        }
    }

    private bool CloseTopPopup()
    {
        foreach (var popup in new[] { _sendPopup, _troopsPopup, _buildingPopup })
        {
            if (popup.Visible)
            {
                popup.Close();
                return true;
            }
        }
        return false;
    }

    private void TogglePause()
    {
        if (!Session.CanPause)
        {
            if (!Session.State.IsMatchOver && !_confirmingLeave && !_confirmingSurrender && !_confirmingQuit)
                _menuOpen = !_menuOpen;
            return;
        }

        if (Session.CanPause && !Session.State.IsMatchOver && !_confirmingLeave && !_confirmingSurrender && !_confirmingQuit)
            Session.SetPaused(!Session.IsPaused);
    }

    private void OnGameEvent(GameEvent gameEvent)
    {
        switch (gameEvent)
        {
            case BuildingCompleted completed when IsLocalVillage(completed.VillageId):
                _villageView.Pulse(completed.Building);
                Sound.Play(SoundEffect.BuildingComplete);
                _toast.ShowMessage(L.T("toast.building_done", ("building", UiContent.Buildings[completed.Building].Name), ("level", Format.Whole(completed.NewLevel))));
                break;
            case BattleResolved resolved:
                var report = Session.State.Reports.First(r => r.Id == resolved.ReportId);
                if (report.AttackerId == Session.LocalPlayerId || report.DefenderId == Session.LocalPlayerId)
                {
                    Sound.Play(SoundEffect.BattleClash);
                    _toast.ShowMessage(L.T("toast.see_reports", ("headline", UiContent.ReportHeadline(Session, report))));
                }
                break;
            case ScoutResolved scouted:
                var scoutReport = Session.State.ScoutReports.First(r => r.Id == scouted.ReportId);
                if (scoutReport.SenderId == Session.LocalPlayerId)
                    _toast.ShowMessage(L.T("toast.see_reports", ("headline", UiContent.ScoutHeadline(Session, scoutReport))));
                break;
            case ScoutNoticed noticed:
                var notice = Session.State.ScoutNotices.First(n => n.Id == noticed.NoticeId);
                if (notice.DefenderId == Session.LocalPlayerId)
                    _toast.ShowMessage(L.T("toast.see_reports", ("headline", UiContent.NoticeHeadline(Session, notice))));
                break;
            case TroopsReturned returned when IsLocalVillage(returned.VillageId):
                var loot = returned.Loot.Wood + returned.Loot.Stone + returned.Loot.Food;
                _toast.ShowMessage(loot > 0 ? L.T("toast.troops_returned_loot", ("amount", Format.Amount(loot))) : L.T("toast.troops_returned"));
                break;
            case TroopsTrained trained when IsLocalVillage(trained.VillageId):
                Sound.Play(SoundEffect.TrainingComplete);
                _toast.ShowMessage(L.T("toast.trained", ("unit", UiContent.Units[trained.Unit].Name), ("count", Format.Whole(trained.Count))));
                break;
            case ReinforcementsArrived arrived when IsLocalVillage(arrived.VillageId):
                _toast.ShowMessage(L.T("toast.troops_arrived", ("village", Session.State.Villages[arrived.VillageId].Name)));
                break;
            case ResourcesDelivered delivered when IsLocalVillage(delivered.VillageId):
                _toast.ShowMessage(DeliveryText(delivered.VillageId, delivered.Delivered, delivered.Lost));
                break;
            case CarriersReturned returned when IsLocalVillage(returned.VillageId) && returned.Delivered + returned.Lost != Resources.Zero:
                _toast.ShowMessage(DeliveryText(returned.VillageId, returned.Delivered, returned.Lost));
                break;
            case VillageFounded founded when IsLocalVillage(founded.VillageId):
                _toast.ShowMessage(L.T("toast.founded", ("village", Session.State.Villages[founded.VillageId].Name)));
                break;
            case SettlingFailed failed when failed.OwnerId == Session.LocalPlayerId:
                _toast.ShowMessage(L.T("toast.settler_back", ("reason", UiContent.RejectionText(failed.Reason))));
                break;
            case VillageCaptured captured when captured.NewOwnerId == Session.LocalPlayerId:
                Sound.Play(SoundEffect.VillageCaptured);
                _toast.ShowMessage(L.T("toast.you_captured", ("village", Session.State.Villages[captured.VillageId].Name)));
                break;
            case VillageCaptured captured when captured.PreviousOwnerId == Session.LocalPlayerId:
                Sound.Play(SoundEffect.VillageCaptured);
                _toast.ShowMessage(L.T("toast.captured_by", ("village", Session.State.Villages[captured.VillageId].Name), ("owner", UiContent.OwnerName(Session, captured.NewOwnerId))));
                break;
            case PlayerSurrendered surrendered when surrendered.PlayerId != Session.LocalPlayerId:
                _toast.ShowMessage(L.T("toast.surrendered", ("owner", UiContent.OwnerName(Session, surrendered.PlayerId))));
                break;
            case PlayerDisconnected disconnected when disconnected.PlayerId != Session.LocalPlayerId:
                _toast.ShowMessage(L.T("toast.disconnected", ("owner", UiContent.OwnerName(Session, disconnected.PlayerId))));
                break;
            case PlayerEliminated eliminated when eliminated.PlayerId != Session.LocalPlayerId:
                _toast.ShowMessage(L.T("toast.eliminated", ("owner", UiContent.OwnerName(Session, eliminated.PlayerId))));
                break;
        }
    }

    private string DeliveryText(int villageId, Resources delivered, Resources lost)
    {
        var village = Session.State.Villages[villageId].Name;
        return lost == Resources.Zero
            ? L.T("toast.delivered", ("amount", Format.Total(delivered)), ("village", village))
            : L.T("toast.delivered_lost", ("amount", Format.Total(delivered)), ("village", village), ("lost", Format.Total(lost)));
    }

    private bool IsLocalVillage(int villageId) => Session.State.Villages[villageId].OwnerId == Session.LocalPlayerId;
}
