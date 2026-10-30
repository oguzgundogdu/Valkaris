using System;
using Godot;

namespace Valkaris.Presentation;

/// <summary>A full-screen dimmed message with one button. It blocks all input to the game behind it.</summary>
public partial class Overlay : ColorRect
{
    public event Action? ActionPressed;

    public override void _Ready() =>
        GetNode<Button>("%Action").Pressed += () => ActionPressed?.Invoke();

    public void Configure(string title, string message, string actionText)
    {
        GetNode<Label>("%Title").Text = title;
        GetNode<Label>("%Message").Text = message;
        GetNode<Button>("%Action").Text = actionText;
    }
}
