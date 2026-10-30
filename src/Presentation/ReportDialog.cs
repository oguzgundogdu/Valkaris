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

        var attacker = session.State.Players[report.AttackerId].Name;
        var defender = session.State.Players[report.DefenderId].Name;
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

        Visible = true;
    }

    private static void FillSide(VBoxContainer box, string title, IReadOnlyDictionary<UnitType, int> present, IReadOnlyDictionary<UnitType, int> lost)
    {
        foreach (var child in box.GetChildren())
        {
            box.RemoveChild(child);
            child.QueueFree();
        }

        box.AddChild(new Label { Text = title, ThemeTypeVariation = "StrongLabel" });
        if (present.Count == 0)
            box.AddChild(new Label { Text = "No troops", ThemeTypeVariation = "MutedLabel" });

        foreach (var (unit, count) in present.OrderBy(t => t.Key))
            box.AddChild(new Label { Text = $"{UiContent.Units[unit].Name}: {count}, lost {lost.GetValueOrDefault(unit)}" });
    }
}
