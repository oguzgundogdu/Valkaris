using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>One chip per owned village, so any of them is a click away in both views.</summary>
public partial class VillageStrip : HFlowContainer
{
    private const int CapitalIconSize = 16;

    private static readonly Texture2D CapitalIcon = GD.Load<Texture2D>("res://assets/icons/capital.svg");

    private readonly List<Button> _chips = new();
    private readonly List<int> _villageIds = new();

    public event Action<int>? VillageSelected;

    public void Refresh(IGameSession session, int? currentId)
    {
        var villages = UiContent.OwnVillages(session);
        Visible = villages.Count > 1;
        if (!Visible)
            return;

        while (_chips.Count < villages.Count)
            _chips.Add(AddChip(_chips.Count));

        _villageIds.Clear();
        _villageIds.AddRange(villages.Select(v => v.Id));

        for (var i = 0; i < _chips.Count; i++)
        {
            var chip = _chips[i];
            chip.Visible = i < villages.Count;
            if (i >= villages.Count)
                continue;

            var village = villages[i];
            var threatened = UiContent.IsThreatened(session, village);
            chip.Text = threatened ? $"{village.Name} !" : village.Name;
            chip.TooltipText = threatened ? "Under threat" : "";
            chip.Icon = LoyaltyRules.IsCapital(session.State, village) ? CapitalIcon : null;
            chip.ButtonPressed = village.Id == currentId;
            if (threatened)
                chip.AddThemeColorOverride("font_color", UiContent.Warning);
            else
                chip.RemoveThemeColorOverride("font_color");
        }
    }

    private Button AddChip(int index)
    {
        var chip = new Button
        {
            Name = $"Chip{index}",
            ToggleMode = true,
            FocusMode = FocusModeEnum.None,
            ThemeTypeVariation = "SecondaryButton",
            ExpandIcon = false,
        };
        chip.AddThemeConstantOverride("icon_max_width", CapitalIconSize);

        // A toggle would untoggle itself on a second click, and the next refresh restores the true state.
        chip.Pressed += () => VillageSelected?.Invoke(_villageIds[index]);
        AddChild(chip);
        return chip;
    }
}
