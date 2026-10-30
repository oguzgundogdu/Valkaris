using Godot;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>One clickable building on the village ground.</summary>
public partial class BuildingTile : Button
{
    private static readonly Color Accent = new(0.851f, 0.643f, 0.255f);
    private static readonly Color TileColor = new(0.141f, 0.118f, 0.098f, 0.92f);
    private static readonly Color HoverColor = new(0.204f, 0.169f, 0.141f, 0.96f);

    private TextureRect _icon = null!;
    private Label _name = null!;
    private Label _level = null!;
    private ProgressBar _progress = null!;
    private Label _state = null!;
    private bool _selected;

    public BuildingType Building { get; private set; }

    public void Init(BuildingType building) => Building = building;

    public override void _Ready()
    {
        _icon = GetNode<TextureRect>("%Icon");
        _name = GetNode<Label>("%Name");
        _level = GetNode<Label>("%Level");
        _progress = GetNode<ProgressBar>("%Progress");
        _state = GetNode<Label>("%State");

        var info = UiContent.Buildings[Building];
        _icon.Texture = GD.Load<Texture2D>(info.IconPath);
        _name.Text = info.Name;
        ApplyStyles();
    }

    public void Refresh(Village village, double now, bool selected)
    {
        if (selected != _selected)
        {
            _selected = selected;
            ApplyStyles();
        }

        var level = village.BuildingLevel(Building);
        _level.Text = level == 0 ? "Not built" : $"Level {level}";
        _icon.Modulate = level == 0 ? new Color(1, 1, 1, 0.4f) : Colors.White;

        var item = FindQueued(village);
        var running = item is not null && now >= item.StartTime;
        _progress.Visible = running;
        _state.Visible = item is not null && !running;
        _state.Text = "Queued";
        if (running)
            _progress.Value = (now - item!.StartTime) / (item.FinishTime - item.StartTime);
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

    private void ApplyStyles()
    {
        var normal = TileStyle(TileColor);
        var hover = TileStyle(HoverColor);
        AddThemeStyleboxOverride("normal", normal);
        AddThemeStyleboxOverride("pressed", normal);
        AddThemeStyleboxOverride("hover", hover);
        AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    private StyleBoxFlat TileStyle(Color background)
    {
        var style = new StyleBoxFlat { BgColor = background };
        style.SetCornerRadiusAll(12);
        style.SetBorderWidthAll(_selected ? 3 : 1);
        style.BorderColor = _selected ? Accent : new Color(0.267f, 0.224f, 0.184f);
        return style;
    }
}
