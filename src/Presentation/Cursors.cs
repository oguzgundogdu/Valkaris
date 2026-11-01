using Godot;

namespace Valkaris.Presentation;

/// <summary>The themed cursor shapes. Every button points by itself, and shows when it can't be pressed.</summary>
public partial class Cursors : Node
{
    public override void _Ready()
    {
        Set(Input.CursorShape.Arrow, "res://assets/cursors/arrow.png", new Vector2(9, 7));
        Set(Input.CursorShape.PointingHand, "res://assets/cursors/hand.png", new Vector2(9, 3));
        Set(Input.CursorShape.Cross, "res://assets/cursors/attack.png", new Vector2(16, 16));
        Set(Input.CursorShape.Forbidden, "res://assets/cursors/forbidden.png", new Vector2(16, 16));
        GetTree().NodeAdded += OnNodeAdded;
    }

    public override void _ExitTree() => GetTree().NodeAdded -= OnNodeAdded;

    private static void Set(Input.CursorShape shape, string path, Vector2 hotspot) =>
        Input.SetCustomMouseCursor(GD.Load<Texture2D>(path), shape, hotspot);

    private static void OnNodeAdded(Node node)
    {
        if (node is not BaseButton button)
            return;

        button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        button.MouseEntered += () =>
            button.MouseDefaultCursorShape = button.Disabled ? Control.CursorShape.Forbidden : Control.CursorShape.PointingHand;
    }
}
