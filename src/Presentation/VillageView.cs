using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>The village ground, with one tile per building.</summary>
public partial class VillageView : Control
{
    private const int LabelsAbove = 6;
    private const float QueueLineMargin = 12;
    private const float QueueLineBottom = 44;

    private static readonly PackedScene TileScene = GD.Load<PackedScene>("res://scenes/BuildingTile.tscn");
    private readonly Dictionary<BuildingType, BuildingTile> _tiles = new();
    private readonly List<VillageScenery> _layers = new();
    private readonly VillageScenery _ring = new() { MouseFilter = MouseFilterEnum.Ignore };
    private List<Placed> _scenery = new();
    private int _wallStage;
    private (int Seed, int Total, int Farm) _sceneryFor;
    private int _seed;
    private readonly HoverTimer<BuildingType> _hover = new();
    private BuildingCard _card = null!;
    private PanelContainer _queueLine = null!;
    private Label _queueText = null!;

    /// <summary>Let the UI test stand in for a cursor, which a headless run doesn't have.</summary>
    public BuildingType? TestHoveredBuilding { get; set; }
    public bool TestCursorOnCard { get; set; }

    public BuildingCard Card => _card;
    public Control QueueLine => _queueLine;
    public string QueueText => _queueText.Text;

    public event Action? TrainingRequested;
    public event Action? SendResourcesRequested;

    /// <summary>How many things stand on the ground, so tests can watch the village grow.</summary>
    public IReadOnlyList<Placed> Scenery => _scenery;

    /// <summary>The pieces of the Wall's ring on show, for tests.</summary>
    public IReadOnlyList<Placed> Ring { get; private set; } = Array.Empty<Placed>();

    /// <summary>The ground in use, so tests can see it stays the same.</summary>
    public Texture2D Ground => VillageGround.For(_seed, Size);

    public event Action<BuildingType>? BuildingSelected;

    public override void _Ready()
    {
        foreach (var building in Enum.GetValues<BuildingType>())
        {
            var tile = TileScene.Instantiate<BuildingTile>();
            tile.Name = building.ToString();
            tile.Init(building);
            tile.Pressed += () => BuildingSelected?.Invoke(building);
            AddChild(tile);
            _tiles[building] = tile;
        }

        AddChild(_ring);

        // A layer sits before each tile and one after them all, so scenery sorts between the buildings.
        for (var i = 0; i <= _tiles.Count; i++)
        {
            var layer = new VillageScenery { MouseFilter = MouseFilterEnum.Ignore };
            AddChild(layer);
            _layers.Add(layer);
        }

        _queueLine = new PanelContainer { Name = "QueueLine", ThemeTypeVariation = "CardPanel", MouseFilter = MouseFilterEnum.Ignore, Visible = false, ZIndex = LabelsAbove };
        _queueText = new Label { Name = "Text", ThemeTypeVariation = "MutedLabel" };
        _queueLine.AddChild(_queueText);
        AddChild(_queueLine);

        _card = new BuildingCard { Name = "Card", Visible = false, ZIndex = LabelsAbove + 1 };
        AddChild(_card);
        _card.TrainingRequested += () => TrainingRequested?.Invoke();
        _card.SendResourcesRequested += () => SendResourcesRequested?.Invoke();

        Resized += PlaceTiles;
        PlaceTiles();
    }

    /// <summary>A building on screen, in window coordinates: what a popup must not cover.</summary>
    public Rect2 BuildingScreenRect(BuildingType building) => _tiles[building].GetGlobalRect();

    public void CloseCard() => _hover.Clear();

    private BuildingType? UnderCursor()
    {
        if (TestHoveredBuilding is not null)
            return TestHoveredBuilding;
        foreach (var (building, tile) in _tiles)
        {
            if (tile.IsHovered())
                return building;
        }
        return null;
    }

    private void RefreshCard(IGameSession session, Village village)
    {
        var overCard = TestCursorOnCard || (_card.Visible && _card.GetGlobalRect().HasPoint(GetGlobalMousePosition()));
        _hover.Update(overCard ? null : UnderCursor(), overCard, GetProcessDeltaTime());

        _card.Visible = _hover.Shown is not null;
        if (_hover.Shown is not { } building)
            return;

        _card.Refresh(session, village, building);
        _card.Size = _card.GetCombinedMinimumSize();
        var tile = _tiles[building].GetRect();
        var (x, y) = PopupPlacement.Beside(new ScreenRect(tile.Position.X, tile.Position.Y, tile.Size.X, tile.Size.Y), _card.Size.X, _card.Size.Y, new ScreenRect(0, 0, Size.X, Size.Y));
        _card.Position = new Vector2((float)x, (float)y);
    }

    private void RefreshQueueLine(IGameSession session, Village village)
    {
        _queueLine.Visible = village.BuildQueue.Count > 0;
        if (village.BuildQueue.Count == 0)
            return;

        var item = village.BuildQueue[0];
        var name = L.T("queue.build_item", ("building", UiContent.Buildings[item.Building].Name), ("level", Format.Whole(item.TargetLevel)));
        _queueText.Text = Format.Dotted(name, Format.Time(item.FinishTime - session.State.ElapsedTime));
        _queueLine.Size = _queueLine.GetCombinedMinimumSize();
        _queueLine.Position = new Vector2(QueueLineMargin, Size.Y - _queueLine.Size.Y - QueueLineBottom);
    }

    public void Refresh(IGameSession session, Village village, BuildingType selected)
    {
        var seed = VillageArt.Seed(session.State.Settings.Seed, village.Id);
        if (seed != _seed)
        {
            _seed = seed;
            QueueRedraw();
        }

        var total = village.Buildings.Values.Sum();
        var farm = VillageArt.Stage(village.BuildingLevel(BuildingType.Farm), session.Data.Buildings.Buildings[BuildingType.Farm].MaxLevel);
        if ((seed, total, farm) != _sceneryFor)
        {
            _sceneryFor = (seed, total, farm);
            _scenery = VillageArt.Scenery(seed, total, farm);
            SortScenery();
        }

        var wall = VillageArt.Stage(village.BuildingLevel(BuildingType.Wall), session.Data.Buildings.Buildings[BuildingType.Wall].MaxLevel);
        if (wall != _wallStage)
        {
            _wallStage = wall;
            ShowRing();
        }

        var now = session.State.ElapsedTime;
        var showPlates = Input.IsKeyPressed(Key.Alt);
        foreach (var (building, tile) in _tiles)
            tile.Refresh(village, now, building == selected, session.Data.Buildings.Buildings[building].MaxLevel, showPlates);

        RefreshCard(session, village);
        RefreshQueueLine(session, village);
    }

    public void Pulse(BuildingType building) => _tiles[building].Pulse();

    private void PlaceTiles()
    {
        foreach (var (building, tile) in _tiles)
            tile.Position = new Vector2(VillageArt.Plots[building].X, VillageArt.Plots[building].Y) * Size - tile.Size / 2;

        SortScenery();
        ShowRing();
        QueueRedraw();
    }

    /// <summary>Under everything else, so it can't cover a building, a label or a click.</summary>
    private void ShowRing()
    {
        Ring = VillageArt.Ring(_wallStage, Size.X, Size.Y);
        _ring.Show(new List<Placed>(Ring));
        MoveChild(_ring, 0);
    }

    /// <summary>Further down the ground draws on top, so nearer things overlap farther ones.</summary>
    private void SortScenery()
    {
        if (_layers.Count == 0)
            return;

        var order = 1;
        var left = _scenery.OrderBy(item => item.Y).ToList();
        var from = 0;
        var plots = VillageArt.Plots.OrderBy(p => p.Value.Y).ToList();
        for (var i = 0; i <= plots.Count; i++)
        {
            var limit = i < plots.Count ? plots[i].Value.Y + VillageArt.TileGroundBelowCentre / Size.Y : float.MaxValue;
            var count = 0;
            while (from + count < left.Count && left[from + count].Y < limit)
                count++;
            _layers[i].Show(left.GetRange(from, count));
            from += count;

            MoveChild(_layers[i], order++);
            if (i < plots.Count)
                MoveChild(_tiles[plots[i].Key], order++);
        }
    }

    public override void _Draw() => DrawTextureRect(Ground, new Rect2(Vector2.Zero, Size), tile: false);
}
