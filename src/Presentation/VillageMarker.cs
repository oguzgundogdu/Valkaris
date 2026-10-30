using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>A village on the map: icon, owner's banner and name. It keeps a constant size on screen at any zoom.</summary>
public partial class VillageMarker : Control
{
    /// <summary>Clicks this close to the marker's centre, in screen pixels, count as hitting it.</summary>
    public const float HitRadius = 36;

    private const float IconSize = 64;

    private readonly TextureRect _icon = new();
    private readonly Label _name = new();
    private Color _banner = Colors.White;
    private bool _isLocal;
    private bool _selected;

    public int VillageId { get; set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        _icon.MouseFilter = MouseFilterEnum.Ignore;
        _icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        _icon.Size = new Vector2(IconSize, IconSize);
        _icon.Position = new Vector2(-IconSize / 2, -IconSize * 0.72f);
        AddChild(_icon);

        _name.Name = "Name";
        _name.MouseFilter = MouseFilterEnum.Ignore;
        _name.ThemeTypeVariation = "StrongLabel";
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        _name.Size = new Vector2(180, 22);
        _name.Position = new Vector2(-90, IconSize * 0.3f);
        _name.AddThemeFontSizeOverride("font_size", 14);
        _name.AddThemeConstantOverride("outline_size", 6);
        _name.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.07f, 0.05f, 0.9f));
        AddChild(_name);
    }

    public void Refresh(IGameSession session, Village village, bool selected)
    {
        _name.Text = village.Name;
        _icon.Texture = GD.Load<Texture2D>(UiContent.VillageIconPath(session, village));

        var banner = UiContent.OwnerColour(session, village.OwnerId);
        var isLocal = village.OwnerId == session.LocalPlayerId;
        if (banner != _banner || isLocal != _isLocal || selected != _selected)
        {
            _banner = banner;
            _isLocal = isLocal;
            _selected = selected;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_selected)
            DrawArc(new Vector2(0, 6), 34, 0, Mathf.Tau, 48, Colors.White, 3, antialiased: true);
        else if (_isLocal)
            DrawArc(new Vector2(0, 6), 32, 0, Mathf.Tau, 48, new Color(_banner, 0.75f), 2, antialiased: true);

        var pole = new Vector2(22, -IconSize * 0.72f);
        DrawLine(pole, pole + new Vector2(0, 26), new Color(0.2f, 0.16f, 0.12f), 2, antialiased: true);
        DrawColoredPolygon(new[] { pole, pole + new Vector2(16, 5), pole + new Vector2(0, 11) }, _banner);
    }
}
