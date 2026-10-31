using System;
using System.Collections.Generic;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>Choosing a new capital. It can be set aside: the top bar keeps counting down and reopens it.</summary>
public partial class CapitalDialog : ColorRect
{
    private readonly List<Button> _rows = new();
    private readonly List<int> _villageIds = new();

    public event Action<int>? CapitalChosen;
    public event Action? Dismissed;

    public override void _Ready() => GetNode<Button>("%Later").Pressed += () => Dismissed?.Invoke();

    public void Refresh(IGameSession session, double deadline)
    {
        var config = session.Data.Villages.Capital;
        var automatic = CapitalRules.AutomaticChoice(session.State, session.LocalPlayerId);

        GetNode<Label>("%Message").Text =
            L.T("capital.message", ("level", Format.Whole(config.PenaltyMainLevel)), ("village", automatic.Name));
        GetNode<Label>("%Countdown").Text = Format.Time(deadline - session.State.ElapsedTime);

        var villages = UiContent.OwnVillages(session);
        var list = GetNode<VBoxContainer>("%Villages");
        while (_rows.Count < villages.Count)
            _rows.Add(AddRow(list, _rows.Count));

        _villageIds.Clear();
        for (var i = 0; i < _rows.Count; i++)
        {
            _rows[i].Visible = i < villages.Count;
            if (i >= villages.Count)
                continue;

            _villageIds.Add(villages[i].Id);
            _rows[i].Text = L.T("capital.row", ("village", villages[i].Name), ("level", Format.Whole(villages[i].BuildingLevel(BuildingType.MainBuilding))));
        }
    }

    private Button AddRow(VBoxContainer list, int index)
    {
        var row = new Button { Name = $"Village{index}", FocusMode = FocusModeEnum.None };
        row.Pressed += () => CapitalChosen?.Invoke(_villageIds[index]);
        list.AddChild(row);
        return row;
    }
}
