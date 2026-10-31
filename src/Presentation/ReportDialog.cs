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
        GetNode<Label>("%Result").Text = UiContent.LocalWon(session, report) ? "Victory" : "Defeat";
        GetNode<Label>("%Summary").Text = $"{UiContent.ReportHeadline(session, report)}  ·  {Format.Time(report.Time)} into the match";

        var attacker = UiContent.OwnerName(session, report.AttackerId);
        var defender = UiContent.OwnerName(session, report.DefenderId);
        FillSide(GetNode<VBoxContainer>("%Attacker"), $"{attacker} (attacking)", report.AttackerSent, report.AttackerLost);
        FillSide(GetNode<VBoxContainer>("%Defender"), $"{defender} (defending)", report.DefenderPresent, report.DefenderLost);

        GetNode<Label>("%Powers").Text =
            $"Attack {report.AttackPower:0}  vs  defence {report.DefencePower:0}" +
            (report.WallLevel > 0 ? $"  (Wall level {report.WallLevel})" : "");

        var loot = report.Loot;
        var lootLabel = GetNode<Label>("%Loot");
        lootLabel.Visible = report.AttackerWon;
        lootLabel.Text = $"Loot: {Format.Amount(loot.Wood)} wood, {Format.Amount(loot.Stone)} stone, {Format.Amount(loot.Food)} food";

        var damage = GetNode<Label>("%Damage");
        damage.Visible = report.CatapultDamage is not null;
        if (report.CatapultDamage is { } hit)
            damage.Text = $"Catapults hit the {UiContent.Buildings[hit.Building].Name}: level {hit.FromLevel} → {hit.ToLevel}";

        var loyalty = GetNode<Label>("%Loyalty");
        loyalty.Visible = report.Loyalty is not null || report.CapitalProtected;
        loyalty.Text = report.CapitalProtected ? "Capital protected: loyalty unchanged"
            : report.Loyalty is { Captured: true } ? "Village captured"
            : report.Loyalty is { } change ? $"Loyalty: {Format.Amount(change.From)} → {Format.Amount(change.To)}"
            : "";

        Visible = true;
    }

    public void ShowScoutReport(IGameSession session, ScoutReport report)
    {
        GetNode<Label>("%Result").Text = UiContent.ScoutResultName(report);
        GetNode<Label>("%Summary").Text = $"{UiContent.ScoutHeadline(session, report)}  ·  {Format.Time(report.Time)} into the match";

        var sender = UiContent.OwnerName(session, report.SenderId);
        var defender = UiContent.OwnerName(session, report.DefenderId);
        FillSide(GetNode<VBoxContainer>("%Attacker"), $"{sender} (scouting)", report.Sent, new Dictionary<UnitType, int> { [UnitType.Scout] = report.ScoutsLost });

        var empty = !report.Succeeded ? "Not seen"
            : report.TroopRange is { } range ? $"{UiContent.RangeText(range)} troops, not counted exactly"
            : "No troops";
        FillSide(GetNode<VBoxContainer>("%Defender"), $"{defender} (garrison)", report.Garrison ?? new Dictionary<UnitType, int>(), null, empty);

        var wall = report.WallRange is { } wallRange ? $"Wall level {UiContent.RangeText(wallRange)}" : $"Wall level {report.WallLevel}";
        GetNode<Label>("%Powers").Text = report.Succeeded
            ? $"{wall}  ·  Troops away from the village aren't counted"
            : "Their Scouts stopped yours, so nothing was seen";

        ShowStock(report.Stock);

        if (report.Buildings is { } buildings)
        {
            var damage = GetNode<Label>("%Damage");
            damage.Visible = true;
            damage.Text = "Buildings: " + string.Join(", ", buildings.Where(b => b.Value > 0).OrderBy(b => b.Key).Select(b => $"{UiContent.Buildings[b.Key].Name} {b.Value}"));
        }

        if (report.Loyalty is { } loyalty)
        {
            var label = GetNode<Label>("%Loyalty");
            label.Visible = true;
            label.Text = $"Loyalty {loyalty:0}";
        }

        Visible = true;
    }

    /// <summary>What the defender knows: who came and, if they were caught, how many. Never what they saw.</summary>
    public void ShowScoutNotice(IGameSession session, ScoutNotice notice)
    {
        GetNode<Label>("%Result").Text = notice.Caught ? "Scouts caught" : "Scouted by an enemy";
        GetNode<Label>("%Summary").Text = $"{UiContent.NoticeHeadline(session, notice)}  ·  {Format.Time(notice.Time)} into the match";

        var sender = UiContent.OwnerName(session, notice.SenderId);
        var defender = UiContent.OwnerName(session, notice.DefenderId);
        var sent = notice.ScoutsSent is { } count ? new Dictionary<UnitType, int> { [UnitType.Scout] = count } : new Dictionary<UnitType, int>();
        FillSide(GetNode<VBoxContainer>("%Attacker"), $"{sender} (scouting)", sent, null, "How many isn't known");
        FillSide(GetNode<VBoxContainer>("%Defender"), defender, new Dictionary<UnitType, int>(), null, notice.ScoutsLost > 0 ? $"Scouts lost: {notice.ScoutsLost}" : "No Scouts lost");

        GetNode<Label>("%Powers").Text = notice.Caught ? "Your Scouts stopped them" : "Your Watchtower saw them, but they got what they came for";
        ShowStock(null);
        Visible = true;
    }

    private void ShowStock(Resources? seen)
    {
        var lootLabel = GetNode<Label>("%Loot");
        lootLabel.Visible = seen is not null;
        if (seen is { } stock)
            lootLabel.Text = $"Resources: {Format.Amount(stock.Wood)} wood, {Format.Amount(stock.Stone)} stone, {Format.Amount(stock.Food)} food";

        GetNode<Label>("%Damage").Visible = false;
        GetNode<Label>("%Loyalty").Visible = false;
    }

    /// <summary>With no losses given, it lists only the counts.</summary>
    private static void FillSide(VBoxContainer box, string title, IReadOnlyDictionary<UnitType, int> present, IReadOnlyDictionary<UnitType, int>? lost, string emptyText = "No troops")
    {
        foreach (var child in box.GetChildren())
        {
            box.RemoveChild(child);
            child.QueueFree();
        }

        box.AddChild(new Label { Text = title, ThemeTypeVariation = "StrongLabel" });
        if (present.Count == 0)
            box.AddChild(new Label { Text = emptyText, ThemeTypeVariation = "MutedLabel" });

        foreach (var (unit, count) in present.OrderBy(t => t.Key))
            box.AddChild(new Label { Text = lost is null ? $"{UiContent.Units[unit].Name}: {count}" : $"{UiContent.Units[unit].Name}: {count}, lost {lost.GetValueOrDefault(unit)}" });
    }
}
