using Godot;
using Valkaris.Core.Hosting;

namespace Valkaris.Presentation;

/// <summary>Dev only: F9 toggles god mode, and a badge in the top bar shows it. The rules themselves are in Core.</summary>
public partial class GodModeControl : Node
{
    private LocalGameSession _session = null!;
    private Label _badge = null!;

    public void Init(LocalGameSession session) => _session = session;

    public override void _Ready()
    {
        var box = GetParent().GetNode<ResourceBar>("%ResourceBar").GetNode<HBoxContainer>("Box");
        _badge = new Label { Name = "GodBadge", Text = "GOD MODE", ThemeTypeVariation = "StrongLabel" };
        _badge.AddThemeColorOverride("font_color", UiContent.Warning);
        box.AddChild(_badge);
        box.MoveChild(_badge, box.GetNode("Pause").GetIndex());
    }

    public override void _Process(double delta) => _badge.Visible = _session.IsGodMode;

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Keycode: Key.F9, Pressed: true, Echo: false })
            _session.SetGodMode(!_session.IsGodMode);
    }
}
