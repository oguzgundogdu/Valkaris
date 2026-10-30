using Godot;

namespace Valkaris.Presentation;

/// <summary>A short message that fades away on its own.</summary>
public partial class Toast : PanelContainer
{
    private const double HoldSeconds = 2.2;
    private const double FadeSeconds = 0.4;

    private Tween? _tween;

    public void ShowMessage(string message)
    {
        GetNode<Label>("Label").Text = message;
        Visible = true;
        Modulate = Colors.White;

        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenInterval(HoldSeconds);
        _tween.TweenProperty(this, "modulate:a", 0f, FadeSeconds);
        _tween.TweenCallback(Callable.From(() => Visible = false));
    }
}
