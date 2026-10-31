using Godot;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>One clickable building on the village ground.</summary>
public partial class BuildingTile : Button
{
    private static readonly Color Accent = new(0.851f, 0.643f, 0.255f);
    private static readonly Vector2 GroundCentre = new(75, 105);

    private TextureRect _icon = null!;
    private Label _name = null!;
    private Label _level = null!;
    private ProgressBar _progress = null!;
    private Label _state = null!;
    private TextureRect _scaffold = null!;
    private Control _badge = null!;
    private Label _badgeText = null!;
    private Control _plate = null!;
    private bool _selected;

    public BuildingType Building { get; private set; }

    /// <summary>The visual stage on show: 0 is the empty plot.</summary>
    public int Stage { get; private set; } = -1;

    public void Init(BuildingType building) => Building = building;

    public override void _Ready()
    {
        _icon = GetNode<TextureRect>("%Icon");
        _name = GetNode<Label>("%Name");
        _level = GetNode<Label>("%Level");
        _progress = GetNode<ProgressBar>("%Progress");
        _state = GetNode<Label>("%State");

        _scaffold = GetNode<TextureRect>("%Scaffold");
        _badge = GetNode<Control>("%Badge");
        _badgeText = GetNode<Label>("%BadgeText");
        _plate = GetNode<Control>("%Plate");

        _name.Text = UiContent.Buildings[Building].Name;
        _scaffold.Texture = GD.Load<Texture2D>(VillageArt.ScaffoldSprite);
        foreach (var sprite in new[] { _icon, _scaffold })
        {
            sprite.Size = Vector2.One * VillageArt.SpriteCanvas * VillageArt.SpriteScale;
            sprite.Position = GroundCentre - new Vector2(VillageArt.SpriteGroundX, VillageArt.SpriteGroundY) * VillageArt.SpriteScale;
        }

        var slim = new StyleBoxFlat { BgColor = new Color(0.08f, 0.07f, 0.05f, 0.88f) };
        slim.SetCornerRadiusAll(9);
        slim.ContentMarginLeft = slim.ContentMarginRight = 8;
        slim.ContentMarginTop = slim.ContentMarginBottom = 1;
        _badge.AddThemeStyleboxOverride("panel", slim);
        _plate.AddThemeStyleboxOverride("panel", slim);

        // Only the picture shows, and the button keeps its click area.
        var none = new StyleBoxEmpty();
        AddThemeStyleboxOverride("normal", none);
        AddThemeStyleboxOverride("pressed", none);
        AddThemeStyleboxOverride("hover", none);
        AddThemeStyleboxOverride("focus", none);
    }

    /// <summary>Only a level badge by default. The name shows on hover, when selected, or when asked for all at once.</summary>
    public void Refresh(Village village, double now, bool selected, int maxLevel, bool showPlate)
    {
        if (selected != _selected)
        {
            _selected = selected;
            QueueRedraw();
        }

        var level = village.BuildingLevel(Building);
        _level.Text = level == 0 ? L.T("common.not_built") : L.T("common.level", ("level", Format.Whole(level)));
        _badgeText.Text = Format.Whole(level);

        var stage = VillageArt.Stage(level, maxLevel);
        if (stage != Stage)
        {
            Stage = stage;
            _icon.Texture = GD.Load<Texture2D>(VillageArt.Sprite(Building, stage));
        }

        var item = FindQueued(village);
        var running = item is not null && now >= item.StartTime;
        _scaffold.Visible = running;
        _progress.Visible = running;
        _state.Visible = item is not null && !running;
        _state.Text = L.T("tile.queued");
        if (running)
            _progress.Value = (now - item!.StartTime) / (item.FinishTime - item.StartTime);

        var plate = selected || showPlate || IsHovered();
        _plate.Visible = plate;
        _badge.Visible = !plate && level > 0;
        if (plate)
            _plate.Position = new Vector2((Size.X - _plate.Size.X) / 2, GroundCentre.Y + 18);
        if (_badge.Visible)
            _badge.Position = new Vector2((Size.X - _badge.Size.X) / 2, GroundCentre.Y + 20);
    }

    /// <summary>A brief grow-and-settle when construction finishes.</summary>
    public void Pulse()
    {
        PivotOffset = Size / 2;
        var tween = CreateTween();
        tween.TweenProperty(this, "scale", new Vector2(1.08f, 1.08f), 0.12);
        tween.TweenProperty(this, "scale", Vector2.One, 0.18);
    }

    private BuildQueueItem? FindQueued(Village village)
    {
        foreach (var item in village.BuildQueue)
        {
            if (item.Building == Building)
                return item;
        }
        return null;
    }

    public override void _Draw()
    {
        if (_selected)
            DrawSetTransform(GroundCentre, 0, new Vector2(1, 0.55f));
        if (_selected)
            DrawArc(Vector2.Zero, 72, 0, Mathf.Tau, 64, Accent, 3, antialiased: true);
    }
}
