using System;
using Godot;

namespace Valkaris.Presentation;

/// <summary>A full-screen dimmed message with one or two buttons. It blocks all input to the game behind it.</summary>
public partial class Overlay : ColorRect
{
    public event Action? ActionPressed;
    public event Action? SecondaryPressed;
    public event Action? TertiaryPressed;

    public override void _Ready()
    {
        GetNode<Button>("%Action").Pressed += () => ActionPressed?.Invoke();
        GetNode<Button>("%Secondary").Pressed += () => SecondaryPressed?.Invoke();
        GetNode<Button>("%Tertiary").Pressed += () => TertiaryPressed?.Invoke();
    }

    /// <summary>A third choice, shown only when there is text for it.</summary>
    public void SetTertiary(string? text)
    {
        var tertiary = GetNode<Button>("%Tertiary");
        tertiary.Text = text ?? "";
        tertiary.Visible = text is not null;
    }

    public void Configure(string title, string message, string actionText, string? secondaryText = null)
    {
        GetNode<Label>("%Title").Text = title;
        GetNode<Label>("%Message").Text = message;
        GetNode<Button>("%Action").Text = actionText;

        var secondary = GetNode<Button>("%Secondary");
        secondary.Text = secondaryText ?? "";
        secondary.Visible = secondaryText is not null;
    }
}
