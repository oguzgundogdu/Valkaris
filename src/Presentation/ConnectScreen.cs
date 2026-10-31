using System;
using Godot;
using Valkaris.Core.Commands;
using Valkaris.Core.Data;
using Valkaris.Core.Network;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>Asks who you are before hosting, and also where to go before joining.</summary>
public partial class ConnectScreen : CenterContainer
{
    private GameData _data = null!;
    private bool _hosting;
    private string? _error;
    private string _village = "";
    private string _address = "";

    public event Action? BackRequested;

    /// <summary>Player name, village name, and the address when joining.</summary>
    public event Action<string, string?, string>? ContinueRequested;

    public void Init(GameData data, bool hosting, string? error, string village, string address)
    {
        _village = village;
        _address = address;
        _data = data;
        _hosting = hosting;
        _error = error;
    }

    public override void _Ready()
    {
        GetNode<Label>("%Title").Text = L.T(_hosting ? "connect.host_title" : "connect.join_title");
        GetNode<Control>("%AddressRow").Visible = !_hosting;

        var player = GetNode<LineEdit>("%PlayerName");
        var village = GetNode<LineEdit>("%VillageName");
        var address = GetNode<LineEdit>("%Address");
        player.MaxLength = village.MaxLength = _data.Game.VillageNames.MaxLength;
        player.Text = PlayerSettings.LoadName();
        village.Text = _village;
        address.Text = _address;
        player.TextChanged += _ => Edited();
        village.TextChanged += _ => Edited();
        address.TextChanged += _ => Edited();

        GetNode<Button>("%Back").Pressed += () => BackRequested?.Invoke();
        GetNode<Button>("%Continue").Pressed += () =>
        {
            var name = VillageNameRules.Clean(player.Text);
            PlayerSettings.SaveName(name);
            ContinueRequested?.Invoke(name, VillageNameRules.Clean(village.Text) is { Length: > 0 } clean ? clean : null, address.Text);
        };
        Check();
    }

    /// <summary>Typing means the last failure no longer applies.</summary>
    private void Edited()
    {
        _error = null;
        Check();
    }

    private void Check()
    {
        var problem = Problem();
        var reason = GetNode<Label>("%Reason");
        reason.Text = _error ?? problem ?? "";
        reason.Visible = reason.Text.Length > 0;
        GetNode<Button>("%Continue").Disabled = problem is not null;
    }

    private string? Problem()
    {
        var limits = _data.Game.VillageNames;
        if (VillageNameRules.FormatRejection(limits, GetNode<LineEdit>("%PlayerName").Text) is not null)
            return L.T("connect.name_invalid", ("min", Format.Whole(limits.MinLength)), ("max", Format.Whole(limits.MaxLength)));

        var village = VillageNameRules.Clean(GetNode<LineEdit>("%VillageName").Text);
        if (village.Length > 0 && VillageNameRules.Rejection(_data, Array.Empty<string>(), village) is { } rejection)
            return UiContent.RejectionText(rejection);

        if (!_hosting && NetAddress.Parse(GetNode<LineEdit>("%Address").Text) is null)
            return L.T("connect.bad_address");
        return null;
    }
}
