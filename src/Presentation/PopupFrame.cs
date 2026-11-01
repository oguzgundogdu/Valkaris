using Godot;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>A floating window for a panel: title, close button and scrolling, kept inside the game area. It doesn't block what's behind it.</summary>
public partial class PopupFrame : PanelContainer
{
    private const float ScreenMargin = 8;
    private const float ScrollbarRoom = 14;

    private ScrollContainer _scroll = null!;
    private Control _content = null!;
    private Rect2? _target;

    public override void _Ready()
    {
        _scroll = GetNode<ScrollContainer>("Frame/Scroll");
        _content = _scroll.GetChild<Control>(0);
        GetNode<Button>("Frame/Header/Close").Pressed += Close;
    }

    public void SetTitle(string title) => GetNode<Label>("Frame/Header/Title").Text = title;

    /// <summary>Opens beside a screen rectangle, or centred with none.</summary>
    public void Open(Rect2? target = null)
    {
        _target = target;
        Visible = true;
        _scroll.ScrollVertical = 0;
    }

    public void Close() => Visible = false;

    public override void _Process(double delta)
    {
        if (!Visible)
            return;

        // Everything inside is live, so its height can change while open.
        var layer = GetParent<Control>();
        var area = new ScreenRect(ScreenMargin, layer.Position.Y + ScreenMargin, layer.Size.X - ScreenMargin * 2, layer.Size.Y - ScreenMargin * 2);
        var chrome = GetCombinedMinimumSize().Y - _scroll.CustomMinimumSize.Y;
        var height = Mathf.Min(_content.GetCombinedMinimumSize().Y, (float)area.Height - chrome);
        _scroll.CustomMinimumSize = new Vector2(_content.CustomMinimumSize.X + ScrollbarRoom, height);
        Size = GetCombinedMinimumSize();

        var (x, y) = _target is { } target
            ? PopupPlacement.Beside(new ScreenRect(target.Position.X, target.Position.Y, target.Size.X, target.Size.Y), Size.X, Size.Y, area)
            : PopupPlacement.Centred(Size.X, Size.Y, area);
        GlobalPosition = new Vector2((float)x, (float)y);
    }
}
