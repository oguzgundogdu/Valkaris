using System;
using Godot;

namespace Valkaris.Presentation;

/// <summary>The short logo animation at launch. Any key or click skips it.</summary>
public partial class IntroScreen : Control
{
    public const double Seconds = 2.5;
    private const double FadeInSeconds = 0.6;
    private const double FadeOutSeconds = 0.4;

    private bool _done;

    public event Action? Finished;

    public override void _Ready()
    {
        var logo = GetNode<Control>("%Logo");
        var swords = GetNode<Control>("%Swords");
        logo.Modulate = Colors.Transparent;
        swords.PivotOffset = swords.CustomMinimumSize / 2;
        swords.Scale = new Vector2(0.6f, 0.6f);

        var tween = CreateTween();
        tween.TweenProperty(logo, "modulate:a", 1f, FadeInSeconds);
        tween.Parallel().TweenProperty(swords, "scale", Vector2.One, FadeInSeconds).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenInterval(Seconds - FadeInSeconds - FadeOutSeconds);
        tween.TweenProperty(logo, "modulate:a", 0f, FadeOutSeconds);
        tween.TweenCallback(Callable.From(Finish));
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true } or InputEventMouseButton { Pressed: true })
        {
            GetViewport().SetInputAsHandled();
            Finish();
        }
    }

    private void Finish()
    {
        if (_done)
            return;

        _done = true;
        Finished?.Invoke();
    }
}
