using System;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>What is known about a selected village or piece of land. Other villages' garrisons stay hidden: reports carry that.</summary>
public partial class VillagePopup : PanelContainer
{
    private int _villageId;
    private TileCoord _tile;

    public event Action<int>? SendRequested;
    public event Action<int>? EnterRequested;
    public event Action<int>? ReinforceRequested;
    public event Action<int>? SendResourcesRequested;
    public event Action<TileCoord>? FoundRequested;

    public override void _Ready()
    {
        GetNode<Button>("%Send").Pressed += () => SendRequested?.Invoke(_villageId);
        GetNode<Button>("%Reinforce").Pressed += () => ReinforceRequested?.Invoke(_villageId);
        GetNode<Button>("%SendResources").Pressed += () => SendResourcesRequested?.Invoke(_villageId);
        GetNode<Button>("%Enter").Pressed += () => EnterRequested?.Invoke(_villageId);
        GetNode<Button>("%Found").Pressed += () => FoundRequested?.Invoke(_tile);
    }

    public void Refresh(IGameSession session, Village village, Village? currentVillage)
    {
        _villageId = village.Id;
        var isOwn = village.OwnerId == session.LocalPlayerId;
        var isCurrent = village.Id == currentVillage?.Id;

        GetNode<Label>("%Name").Text = village.Name;
        var owner = UiContent.OwnerName(session, village.OwnerId);
        var ownerLabel = GetNode<Label>("%Owner");
        ownerLabel.Visible = true;
        ownerLabel.Text = village.BarbarianTier is { } tier ? Format.Dotted(owner, UiContent.TierName(session, tier))
            : village.IsAbandoned ? Format.Dotted(owner, L.T("popup.abandoned"))
            : LoyaltyRules.IsCapital(session.State, village) ? Format.Dotted(owner, L.T("popup.capital"))
            : owner;

        var distance = GetNode<Label>("%Distance");
        distance.Visible = currentVillage is not null && !isCurrent;
        if (currentVillage is not null)
            distance.Text = L.T("common.tiles_away", ("distance", Format.OneDecimal(UnitRules.Distance(currentVillage.Position, village.Position))));

        var reason = GetNode<Label>("%Reason");
        reason.Visible = isOwn;
        reason.Text = isOwn ? L.T("scout.loyalty", ("value", Format.Amount(village.Loyalty))) : "";

        GetNode<Button>("%Send").Visible = !isOwn && currentVillage is not null;
        GetNode<Button>("%Enter").Visible = isOwn;
        GetNode<Button>("%Reinforce").Visible = isOwn && !isCurrent && currentVillage is not null;
        GetNode<Button>("%SendResources").Visible = isOwn && !isCurrent && currentVillage is not null;
        GetNode<Button>("%Found").Visible = false;
    }

    /// <summary>Land only opens this popup when the current village has a Settler to send.</summary>
    public void RefreshLand(IGameSession session, TileCoord tile, Village currentVillage)
    {
        _tile = tile;
        var option = FoundPreview.Evaluate(session.State, session.Data, session.LocalPlayerId, currentVillage.Id, tile);

        GetNode<Label>("%Name").Text = L.T(L.Key("terrain", session.State.Map.TileAt(tile).Terrain));
        GetNode<Label>("%Owner").Visible = false;

        var distance = GetNode<Label>("%Distance");
        distance.Visible = true;
        distance.Text = L.T("common.tiles_away", ("distance", Format.OneDecimal(UnitRules.Distance(currentVillage.Position, tile))));

        var reason = GetNode<Label>("%Reason");
        reason.Visible = true;
        reason.Text = option.Rejection is { } rejection ? UiContent.RejectionText(rejection) : L.T("popup.settler_arrives", ("time", Format.Time(option.DurationSeconds)));

        GetNode<Button>("%Send").Visible = false;
        GetNode<Button>("%Enter").Visible = false;
        GetNode<Button>("%Reinforce").Visible = false;
        GetNode<Button>("%SendResources").Visible = false;
        var found = GetNode<Button>("%Found");
        found.Visible = true;
        found.Disabled = !option.CanFound;
    }
}
