using System.Collections.Generic;
using System.Linq;
using Godot;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>The full battle report in a modal. It blocks input to the game behind it.</summary>
public partial class ReportDialog : ColorRect
{
    public override void _Ready() => GetNode<Button>("%Close").Pressed += () => Visible = false;

    public void ShowReport(IGameSession session, BattleReport report)
    {
        GetNode<Label>("%Result").Text = L.T(UiContent.LocalWon(session, report) ? "report.victory" : "report.defeat");
        GetNode<Label>("%Summary").Text = L.T("report.time", ("headline", UiContent.ReportHeadline(session, report)), ("time", Format.Time(report.Time)));

        var attacker = UiContent.OwnerName(session, report.AttackerId);
        var defender = UiContent.OwnerName(session, report.DefenderId);
        FillSide(GetNode<VBoxContainer>("%Attacker"), L.T("report.attacking", ("owner", attacker)), report.AttackerSent, report.AttackerLost);
        FillSide(GetNode<VBoxContainer>("%Defender"), L.T("report.defending", ("owner", defender)), report.DefenderPresent, report.DefenderLost);

        GetNode<Label>("%Powers").Text =
            L.T(report.WallLevel > 0 ? "report.powers_wall" : "report.powers", ("attack", Format.Whole(report.AttackPower)), ("defence", Format.Whole(report.DefencePower)), ("level", Format.Whole(report.WallLevel)));

        var loot = report.Loot;
        var lootLabel = GetNode<Label>("%Loot");
        lootLabel.Visible = report.AttackerWon;
        lootLabel.Text = L.T("report.loot", ("list", UiContent.ResourceList(loot)));

        var damage = GetNode<Label>("%Damage");
        damage.Visible = report.CatapultDamage is not null;
        if (report.CatapultDamage is { } hit)
            damage.Text = L.T("report.catapults", ("building", UiContent.Buildings[hit.Building].Name), ("from", Format.Whole(hit.FromLevel)), ("to", Format.Whole(hit.ToLevel)));

        var loyalty = GetNode<Label>("%Loyalty");
        loyalty.Visible = report.Loyalty is not null || report.CapitalProtected;
        loyalty.Text = report.CapitalProtected ? L.T("report.capital_protected")
            : report.Loyalty is { Captured: true } ? L.T("report.captured")
            : report.Loyalty is { } change ? L.T("report.loyalty_change", ("from", Format.Amount(change.From)), ("to", Format.Amount(change.To)))
            : "";

        Visible = true;
    }

    public void ShowScoutReport(IGameSession session, ScoutReport report)
    {
        GetNode<Label>("%Result").Text = UiContent.ScoutResultName(report);
        GetNode<Label>("%Summary").Text = L.T("report.time", ("headline", UiContent.ScoutHeadline(session, report)), ("time", Format.Time(report.Time)));

        var sender = UiContent.OwnerName(session, report.SenderId);
        var defender = UiContent.OwnerName(session, report.DefenderId);
        FillSide(GetNode<VBoxContainer>("%Attacker"), L.T("scout.scouting", ("owner", sender)), report.Sent, new Dictionary<UnitType, int> { [UnitType.Scout] = report.ScoutsLost });

        var empty = !report.Succeeded ? L.T("scout.not_seen")
            : report.TroopRange is { } range ? L.T("scout.range_troops", ("range", UiContent.RangeText(range)))
            : L.T("report.no_troops");
        FillSide(GetNode<VBoxContainer>("%Defender"), L.T("scout.garrison", ("owner", defender)), report.Garrison ?? new Dictionary<UnitType, int>(), null, empty);

        var wall = L.T("scout.wall_level", ("level", report.WallRange is { } wallRange ? UiContent.RangeText(wallRange) : Format.Whole(report.WallLevel ?? 0)));
        GetNode<Label>("%Powers").Text = report.Succeeded
            ? L.T("scout.seen_note", ("wall", wall))
            : L.T("scout.stopped");

        ShowStock(report.Stock);

        if (report.Buildings is { } buildings)
        {
            var damage = GetNode<Label>("%Damage");
            damage.Visible = true;
            damage.Text = L.T("scout.buildings", ("list", string.Join(", ", buildings.Where(b => b.Value > 0).OrderBy(b => b.Key).Select(b => L.T("scout.building_level", ("building", UiContent.Buildings[b.Key].Name), ("level", Format.Whole(b.Value)))))));
        }

        if (report.Loyalty is { } loyalty)
        {
            var label = GetNode<Label>("%Loyalty");
            label.Visible = true;
            label.Text = L.T("scout.loyalty", ("value", Format.Whole(loyalty)));
        }

        Visible = true;
    }

    /// <summary>What the defender knows: who came and, if they were caught, how many. Never what they saw.</summary>
    public void ShowScoutNotice(IGameSession session, ScoutNotice notice)
    {
        GetNode<Label>("%Result").Text = L.T(notice.Caught ? "notice.result_caught" : "notice.result_seen");
        GetNode<Label>("%Summary").Text = L.T("report.time", ("headline", UiContent.NoticeHeadline(session, notice)), ("time", Format.Time(notice.Time)));

        var sender = UiContent.OwnerName(session, notice.SenderId);
        var defender = UiContent.OwnerName(session, notice.DefenderId);
        var sent = notice.ScoutsSent is { } count ? new Dictionary<UnitType, int> { [UnitType.Scout] = count } : new Dictionary<UnitType, int>();
        FillSide(GetNode<VBoxContainer>("%Attacker"), L.T("scout.scouting", ("owner", sender)), sent, null, L.T("notice.unknown_count"));
        FillSide(GetNode<VBoxContainer>("%Defender"), defender, new Dictionary<UnitType, int>(), null, notice.ScoutsLost > 0 ? L.T("notice.scouts_lost", ("count", Format.Whole(notice.ScoutsLost))) : L.T("notice.no_scouts_lost"));

        GetNode<Label>("%Powers").Text = L.T(notice.Caught ? "notice.stopped" : "notice.watchtower");
        ShowStock(null);
        Visible = true;
    }

    private void ShowStock(Resources? seen)
    {
        var lootLabel = GetNode<Label>("%Loot");
        lootLabel.Visible = seen is not null;
        if (seen is { } stock)
            lootLabel.Text = L.T("scout.resources", ("list", UiContent.ResourceList(stock)));

        GetNode<Label>("%Damage").Visible = false;
        GetNode<Label>("%Loyalty").Visible = false;
    }

    /// <summary>With no losses given, it lists only the counts.</summary>
    private static void FillSide(VBoxContainer box, string title, IReadOnlyDictionary<UnitType, int> present, IReadOnlyDictionary<UnitType, int>? lost, string? emptyText = null)
    {
        foreach (var child in box.GetChildren())
        {
            box.RemoveChild(child);
            child.QueueFree();
        }

        box.AddChild(new Label { Text = title, ThemeTypeVariation = "StrongLabel" });
        if (present.Count == 0)
            box.AddChild(new Label { Text = emptyText ?? L.T("report.no_troops"), ThemeTypeVariation = "MutedLabel" });

        foreach (var (unit, count) in present.OrderBy(t => t.Key))
            box.AddChild(new Label { Text = lost is null
                ? L.T("report.unit_count", ("unit", UiContent.Units[unit].Name), ("count", Format.Whole(count)))
                : L.T("report.unit_lost", ("unit", UiContent.Units[unit].Name), ("count", Format.Whole(count)), ("lost", Format.Whole(lost.GetValueOrDefault(unit)))) });
    }
}
