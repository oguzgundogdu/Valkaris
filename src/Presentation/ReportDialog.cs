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
        var mine = report.SenderId == session.LocalPlayerId;
        GetNode<Label>("%Result").Text = report.Succeeded ? "Scouted" : mine ? "Scouts lost" : "Scouts caught";
        GetNode<Label>("%Summary").Text = $"{UiContent.ScoutHeadline(session, report)}  ·  {Format.Time(report.Time)} into the match";

        var sender = UiContent.OwnerName(session, report.SenderId);
        var defender = UiContent.OwnerName(session, report.DefenderId);
        FillSide(GetNode<VBoxContainer>("%Attacker"), $"{sender} (scouting)", report.Sent, report.Succeeded ? null : report.Sent);

        var side = GetNode<VBoxContainer>("%Defender");
        FillSide(side, $"{defender} (garrison)", report.Garrison ?? new Dictionary<UnitType, int>(), null, report.Succeeded ? "No troops" : mine ? "Not seen" : "No losses");

        GetNode<Label>("%Powers").Text = report.Succeeded
            ? $"Wall level {report.WallLevel}  ·  Troops away from the village aren't counted"
            : mine ? $"The garrison holds more than {report.Sent.Values.Sum() * session.Data.Combat.TroopsPerScout} troops" : "Your garrison stopped them";

        var lootLabel = GetNode<Label>("%Loot");
        lootLabel.Visible = report.Stock is not null;
        if (report.Stock is { } stock)
            lootLabel.Text = $"Resources: {Format.Amount(stock.Wood)} wood, {Format.Amount(stock.Stone)} stone, {Format.Amount(stock.Food)} food";

        GetNode<Label>("%Damage").Visible = false;
        GetNode<Label>("%Loyalty").Visible = false;
        Visible = true;
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
