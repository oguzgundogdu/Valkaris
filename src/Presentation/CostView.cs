using System;
using System.Collections.Generic;
using Godot;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>A row of resource amounts. Anything the village is short of turns red.</summary>
public partial class CostView : HBoxContainer
{
    private readonly Dictionary<ResourceType, Label> _amounts = new();

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 12);
        foreach (var type in Enum.GetValues<ResourceType>())
        {
            var box = new HBoxContainer();
            box.AddThemeConstantOverride("separation", 4);
            box.AddChild(new TextureRect
            {
                Texture = GD.Load<Texture2D>(UiContent.ResourceIconPath(type)),
                CustomMinimumSize = new Vector2(20, 20),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            });
            var amount = new Label { ThemeTypeVariation = "StrongLabel" };
            box.AddChild(amount);
            AddChild(box);
            _amounts[type] = amount;
        }
    }

    public void Refresh(Resources cost, Resources available)
    {
        foreach (var (type, label) in _amounts)
        {
            label.Text = Format.Amount(cost.Get(type));
            label.AddThemeColorOverride("font_color", available.Get(type) < cost.Get(type) ? UiContent.Danger : UiContent.Text);
        }
    }
}
