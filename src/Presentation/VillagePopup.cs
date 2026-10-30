using System;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>What is known about a selected village. Other villages' garrisons stay hidden: reports carry that.</summary>
public partial class VillagePopup : PanelContainer
{
    private int _villageId;

    public event Action<int>? SendRequested;
    public event Action<int>? EnterRequested;

    public override void _Ready()
    {
        GetNode<Button>("%Send").Pressed += () => SendRequested?.Invoke(_villageId);
        GetNode<Button>("%Enter").Pressed += () => EnterRequested?.Invoke(_villageId);
    }

    public void Refresh(IGameSession session, Village village, Village currentVillage)
    {
        _villageId = village.Id;
        var isOwn = village.OwnerId == session.LocalPlayerId;

        GetNode<Label>("%Name").Text = village.Name;
        GetNode<Label>("%Owner").Text = village.BarbarianTier is { } tier
            ? $"{UiContent.OwnerName(session, village.OwnerId)}  ·  {session.Data.Map.Barbarians.Tiers[tier].Name}"
            : UiContent.OwnerName(session, village.OwnerId);

        var distance = GetNode<Label>("%Distance");
        distance.Visible = village.Id != currentVillage.Id;
        distance.Text = $"{UnitRules.Distance(currentVillage.Position, village.Position):0.0} tiles away";

        GetNode<Button>("%Send").Visible = !isOwn;
        GetNode<Button>("%Enter").Visible = isOwn;
    }
}
