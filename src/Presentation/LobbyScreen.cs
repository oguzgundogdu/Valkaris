using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Valkaris.Ai;
using Valkaris.Core.Data;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Network;

namespace Valkaris.Presentation;

/// <summary>The seats and settings before a match. The host can change them, and everyone else watches.</summary>
public partial class LobbyScreen : CenterContainer
{
    private const int OpenItem = 0;
    private static readonly AiDifficulty[] Difficulties = Enum.GetValues<AiDifficulty>();

    private readonly List<(Label Name, OptionButton Kind)> _rows = new();
    private GameData _data = null!;
    private LobbyHost? _host;
    private LobbyClient? _client;
    private IReadOnlyList<string> _addresses = Array.Empty<string>();
    private int _port;
    private OptionButton _duration = null!;
    private OptionButton _speed = null!;
    private OptionButton _mapSize = null!;

    public event Action? LeaveRequested;
    public event Action<HostGameSession>? HostStarted;
    public event Action<ClientGameSession>? ClientStarted;

    /// <summary>The connection failed or was refused, for this reason.</summary>
    public event Action<string>? Failed;

    public void InitHost(GameData data, LobbyHost host, IReadOnlyList<string> addresses, int port)
    {
        _data = data;
        _host = host;
        _addresses = addresses;
        _port = port;
        host.Changed += Refresh;
    }

    public void InitClient(GameData data, LobbyClient client)
    {
        _data = data;
        _client = client;
        client.LobbyChanged += Refresh;
        client.Refused += refused => Failed?.Invoke(refused.Village is { } village ? UiContent.RejectionText(village) : L.T(L.Key("join", refused.Reason)));
        client.ConnectionLost += () => Failed?.Invoke(L.T(client.Lobby is null ? "connect.unreachable" : "connect.lost"));
        client.MatchStarted += session => ClientStarted?.Invoke(session);
    }

    public override void _Ready()
    {
        _duration = GetNode<OptionButton>("%Duration");
        _speed = GetNode<OptionButton>("%Speed");
        _mapSize = GetNode<OptionButton>("%MapSize");
        StartScreen.FillMatchOptions(_data, _duration, _speed, _mapSize);

        var slots = GetNode<VBoxContainer>("%Slots");
        for (var i = 0; i < _data.Game.MaxPlayers; i++)
        {
            var row = new HBoxContainer { Name = $"Slot{i}" };
            var name = new Label { Name = "Player", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
            var kind = new OptionButton { Name = "Kind", CustomMinimumSize = new Vector2(190, 0) };
            kind.AddItem(L.T("lobby.open"));
            foreach (var difficulty in Difficulties)
                kind.AddItem(L.T("lobby.ai", ("difficulty", L.T(L.Key("difficulty", difficulty)))));
            kind.AddItem(L.T("lobby.closed"));
            var index = i;
            kind.ItemSelected += item => OnKindChosen(index, (int)item);
            row.AddChild(name);
            row.AddChild(kind);
            slots.AddChild(row);
            _rows.Add((name, kind));
        }

        _duration.ItemSelected += _ => OnSettingsChosen();
        _speed.ItemSelected += _ => OnSettingsChosen();
        _mapSize.ItemSelected += _ => OnSettingsChosen();

        GetNode<Button>("%Leave").Pressed += () => LeaveRequested?.Invoke();
        var start = GetNode<Button>("%Start");
        start.Visible = _host is not null;
        start.Pressed += () =>
        {
            var session = _host!.Start((ulong)Random.Shared.NextInt64(), (local, id) => new AiPlayer(new AiView(local.State, local.Data, id, command => local.SendAs(id, command))));
            HostStarted?.Invoke(session);
        };

        Refresh();
    }

    public override void _Process(double delta)
    {
        _host?.Poll();
        _client?.Poll();
    }

    private void OnKindChosen(int slot, int item)
    {
        var kind = item == OpenItem ? SlotKind.Open : item <= Difficulties.Length ? SlotKind.Ai : SlotKind.Closed;
        var difficulty = kind == SlotKind.Ai ? Difficulties[item - 1] : AiDifficulty.Normal;
        _host!.SetSlot(slot, kind, difficulty);
    }

    private void OnSettingsChosen() => _host!.SetSettings(
        StartScreen.ChosenDuration(_data, _duration),
        _data.Game.SpeedMultipliers[_speed.Selected],
        Enum.GetValues<MapSize>()[_mapSize.Selected]);

    private void Refresh()
    {
        var slots = _host?.State.Slots ?? _client!.Lobby?.Slots;
        var settings = _host?.State.Settings ?? _client!.Lobby?.Settings;
        var status = GetNode<Label>("%Status");
        GetNode<Control>("%Body").Visible = slots is not null;
        if (slots is null || settings is null)
        {
            status.Text = L.T("lobby.connecting");
            return;
        }

        for (var i = 0; i < _rows.Count; i++)
            RefreshRow(_rows[i], slots[i]);
        ShowSettings(settings);

        foreach (var option in new[] { _duration, _speed, _mapSize })
            option.Disabled = _host is null;

        if (_host is null)
        {
            status.Text = L.T("lobby.waiting_for_host");
            return;
        }

        var start = GetNode<Button>("%Start");
        start.Disabled = !_host.State.CanStart;
        var port = Handshake.PortText(_port);
        var shared = _addresses.Count == 0
            ? L.T("lobby.no_address", ("port", port))
            : L.T("lobby.share", ("addresses", string.Join(", ", _addresses)), ("port", port));
        status.Text = start.Disabled ? shared + "\n" + L.T("lobby.need_players") : shared;
    }

    private void RefreshRow((Label Name, OptionButton Kind) row, LobbySlot slot)
    {
        row.Name.Text = slot.PlayerName is { } name
            ? slot.VillageName is { } village ? L.T("lobby.player_village", ("name", name), ("village", village)) : name
            : slot.Kind switch
            {
                SlotKind.Ai => L.T("lobby.ai", ("difficulty", L.T(L.Key("difficulty", slot.Difficulty)))),
                SlotKind.Closed => L.T("lobby.closed"),
                _ => L.T("lobby.waiting_for_player"),
            };

        row.Kind.Visible = _host is not null && slot.Kind != SlotKind.Host;
        row.Kind.Select(slot.Kind switch
        {
            SlotKind.Ai => 1 + Array.IndexOf(Difficulties, slot.Difficulty),
            SlotKind.Closed => row.Kind.ItemCount - 1,
            _ => OpenItem,
        });
    }

    private void ShowSettings(GameSettings settings)
    {
        var durations = _data.Game.MatchDurationsMinutes;
        _duration.Select(settings.MatchDurationSeconds is { } seconds ? durations.ToList().IndexOf(seconds / 60) : durations.Count);
        _speed.Select(_data.Game.SpeedMultipliers.ToList().IndexOf(settings.SpeedMultiplier));
        _mapSize.Select(Array.IndexOf(Enum.GetValues<MapSize>(), settings.MapSize));
    }
}
