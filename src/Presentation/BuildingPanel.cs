using System;
using Godot;
using Valkaris.Core.Commands;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>Details of the selected building and its upgrade button.</summary>
public partial class BuildingPanel : PanelContainer
{
    private TextureRect _icon = null!;
    private Label _name = null!;
    private Label _level = null!;
    private Label _description = null!;
    private Label _effect = null!;
    private Control _costSection = null!;
    private Label _time = null!;
    private Button _upgrade = null!;
    private Label _reason = null!;
    private CostView _costs = null!;
    private Button _openTraining = null!;

    private IGameSession? _session;
    private int _villageId;
    private BuildingType _building;

    public event Action? TrainingRequested;
    public event Action? SendResourcesRequested;

    public override void _Ready()
    {
        _icon = GetNode<TextureRect>("%Icon");
        _name = GetNode<Label>("%Name");
        _level = GetNode<Label>("%Level");
        _description = GetNode<Label>("%Description");
        _effect = GetNode<Label>("%Effect");
        _costSection = GetNode<Control>("%CostSection");
        _time = GetNode<Label>("%Time");
        _upgrade = GetNode<Button>("%Upgrade");
        _reason = GetNode<Label>("%Reason");
        _costs = new CostView { Name = "Cost" };
        GetNode("%Costs").AddChild(_costs);
        _openTraining = GetNode<Button>("%OpenTraining");
        _openTraining.Pressed += () => TrainingRequested?.Invoke();
        GetNode<Button>("%OpenTransfer").Pressed += () => SendResourcesRequested?.Invoke();

        _upgrade.Pressed += () => _session?.Send(new BuildCommand(_villageId, _building));
    }

    public void Refresh(IGameSession session, Village village, BuildingType building)
    {
        _session = session;
        _villageId = village.Id;
        _building = building;

        var info = UiContent.Buildings[building];
        var level = village.BuildingLevel(building);
        var option = BuildPreview.Evaluate(session.State, session.Data, session.LocalPlayerId, village.Id, building);
        var atMax = option.TargetLevel > session.Data.Buildings.Buildings[building].MaxLevel;

        _icon.Texture = GD.Load<Texture2D>(info.IconPath);
        _name.Text = info.Name;
        _level.Text = level == 0 ? L.T("common.not_built") : L.T("common.level", ("level", Format.Whole(level)));
        _description.Text = info.Description;

        // A rejection such as "match over" comes with no target level at all.
        var hasNext = !atMax && option.TargetLevel > 0;

        var effect = EffectText(session, village, building, level, option.TargetLevel, hasNext);
        _effect.Text = effect;
        _effect.Visible = effect.Length > 0;

        _costSection.Visible = hasNext;
        _costs.Refresh(option.Cost, village.Resources);
        _time.Text = Format.Time(option.DurationSeconds);

        _upgrade.Disabled = !option.CanBuild;
        _upgrade.Text = atMax ? L.T("upgrade.max")
            : !hasNext ? L.T("upgrade.unavailable")
            : option.TargetLevel == 1 ? L.T("upgrade.build")
            : L.T("upgrade.to_level", ("level", Format.Whole(option.TargetLevel)));

        _openTraining.Visible = building == BuildingType.Barracks;
        GetNode<Button>("%OpenTransfer").Visible = building == BuildingType.Marketplace;

        var reason = ReasonText(option);
        _reason.Text = reason;
        _reason.Visible = reason.Length > 0;
    }

    private static string EffectText(IGameSession session, Village village, BuildingType building, int level, int nextLevel, bool hasNext)
    {
        var data = session.Data;
        var definition = data.Buildings.Buildings[building];

        string Compare(Func<int, string> describe) =>
            hasNext
                ? L.T("effect.compare", ("now", describe(level)), ("level", Format.Whole(nextLevel)), ("next", describe(nextLevel)))
                : L.T("effect.now", ("value", describe(level)));

        if (definition.Production is { } production)
        {
            var speed = session.State.Settings.SpeedMultiplier;
            return Compare(l => Format.Rate(BuildingRules.Production(data, building, l).Get(production.Resource) * speed));
        }

        if (building == BuildingType.Warehouse)
            return Compare(l => L.T("effect.holds", ("amount", Format.Amount(BuildingRules.WarehouseCapacity(data, l)))));

        if (building == BuildingType.MainBuilding)
            return Compare(l => L.T("effect.others", ("level", Format.Whole(BuildingRules.MaxOtherBuildingLevel(data, l)))));

        if (building == BuildingType.Marketplace)
        {
            var carriers = data.Buildings.CarriersByMarketplaceLevel;
            var capacity = data.Villages.Carriers.Capacity;
            return Compare(l => L.N("effect.carriers", carriers[l]))
                + "\n" + L.T("effect.carriers_free", ("free", Format.Whole(CarrierRules.Free(session.State, data, village))), ("capacity", Format.Whole(capacity)));
        }

        if (building == BuildingType.Watchtower)
            return Compare(l => L.T("effect.watchtower", ("value", Format.Trimmed(1 + data.Combat.Scouting.WatchtowerBonusPerLevel * l))));

        return "";
    }

    private static string ReasonText(BuildOption option) => option.Rejection switch
    {
        null or CommandRejection.MaxLevelReached => "",
        CommandRejection.InsufficientResources when option.CostExceedsCapacity =>
            L.T("upgrade.warehouse_small"),
        { } rejection => UiContent.RejectionText(rejection),
    };
}
