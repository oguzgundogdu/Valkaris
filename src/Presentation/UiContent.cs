using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Valkaris.Core.Commands;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Presentation;

/// <summary>Icons, and display names through the translation files. Presentation content, not balance.</summary>
public sealed record BuildingInfo(BuildingType Type, string IconPath)
{
    public string Name => L.T(L.Key("building", Type) + ".name");

    public string Description => L.T(L.Key("building", Type) + ".description");
}

public sealed record UnitInfo(UnitType Type, string IconPath)
{
    public string Name => L.T(L.Key("unit", Type) + ".name");
}

public static class UiContent
{
    public static readonly Color Danger = new(0.898f, 0.439f, 0.345f);
    public static readonly Color Warning = new(0.961f, 0.682f, 0.243f);
    public static readonly Color Text = new(0.941f, 0.902f, 0.839f);

    public static readonly IReadOnlyDictionary<BuildingType, BuildingInfo> Buildings = new Dictionary<BuildingType, BuildingInfo>
    {
        [BuildingType.MainBuilding] = new(BuildingType.MainBuilding, "res://assets/icons/main_building.svg"),
        [BuildingType.Woodcutter] = new(BuildingType.Woodcutter, "res://assets/icons/woodcutter.svg"),
        [BuildingType.Quarry] = new(BuildingType.Quarry, "res://assets/icons/quarry.svg"),
        [BuildingType.Farm] = new(BuildingType.Farm, "res://assets/icons/farm.svg"),
        [BuildingType.Warehouse] = new(BuildingType.Warehouse, "res://assets/icons/warehouse.svg"),
        [BuildingType.Barracks] = new(BuildingType.Barracks, "res://assets/icons/barracks.svg"),
        [BuildingType.Wall] = new(BuildingType.Wall, "res://assets/icons/wall.svg"),
        [BuildingType.Marketplace] = new(BuildingType.Marketplace, "res://assets/icons/marketplace.svg"),
        [BuildingType.Watchtower] = new(BuildingType.Watchtower, "res://assets/icons/watchtower.svg"),
    };

    public static readonly IReadOnlyDictionary<UnitType, UnitInfo> Units = new Dictionary<UnitType, UnitInfo>
    {
        [UnitType.Spearman] = new(UnitType.Spearman, "res://assets/icons/spearman.svg"),
        [UnitType.Swordsman] = new(UnitType.Swordsman, "res://assets/icons/swordsman.svg"),
        [UnitType.Cavalry] = new(UnitType.Cavalry, "res://assets/icons/cavalry.svg"),
        [UnitType.Catapult] = new(UnitType.Catapult, "res://assets/icons/catapult.svg"),
        [UnitType.Settler] = new(UnitType.Settler, "res://assets/icons/settler.svg"),
        [UnitType.Lord] = new(UnitType.Lord, "res://assets/icons/lord.svg"),
        [UnitType.Scout] = new(UnitType.Scout, "res://assets/icons/scout.svg"),
    };

    private static readonly Color[] PlayerColours =
    {
        new(0.851f, 0.643f, 0.255f),
        new(0.80f, 0.27f, 0.25f),
        new(0.27f, 0.56f, 0.80f),
        new(0.56f, 0.40f, 0.75f),
    };

    private static readonly Color BarbarianColour = new(0.25f, 0.23f, 0.21f);

    public static Color OwnerColour(IGameSession session, int ownerId) =>
        ownerId == Village.BarbarianOwnerId ? BarbarianColour : PlayerColours[(ownerId - 1) % PlayerColours.Length];

    public static string VillageIconPath(IGameSession session, Village village)
    {
        if (!village.IsBarbarian)
            return "res://assets/icons/map_village.svg";

        // Only the strongest tier gets the fortified look.
        return village.BarbarianTier == session.Data.Map.Barbarians.Tiers.Count - 1
            ? "res://assets/icons/map_stronghold.svg"
            : "res://assets/icons/map_camp.svg";
    }

    /// <summary>The local player sees their own movements and anything heading for their villages.</summary>
    public static bool IsVisibleMovement(IGameSession session, Movement movement) =>
        movement.OwnerId == session.LocalPlayerId
        || Visibility.Spectates(session.State, session.LocalPlayerId)
        || (movement.Type is not (MovementType.Transfer or MovementType.CarrierReturn or MovementType.Scout) && movement.ToVillageId is { } target && session.State.Villages[target].OwnerId == session.LocalPlayerId);

    /// <summary>The local player's villages, in the order they are shown and cycled through.</summary>
    /// <summary>Proper nouns read the same in every language, so the identifier is the name.</summary>
    public static string NationName(Nation nation) => Enum.GetName(nation)!;

    public static string NationDescription(Nation? nation) => L.T(nation switch
    {
        Nation.Valdren => "nation.valdren_desc",
        Nation.Karuk => "nation.karuk_desc",
        Nation.Solari => "nation.solari_desc",
        _ => "start.nation_random_desc",
    });

    /// <summary>Attacks and raids on the way to the local player's villages.</summary>
    public static IEnumerable<Movement> IncomingAttacks(IGameSession session) => session.State.Movements
        .Where(m => m.OwnerId != session.LocalPlayerId
            && m.Type is MovementType.Attack or MovementType.Raid
            && session.State.Villages[m.ToVillageId!.Value].OwnerId == session.LocalPlayerId);

    public static IReadOnlyList<Village> OwnVillages(IGameSession session) =>
        session.State.Villages.Values.Where(v => v.OwnerId == session.LocalPlayerId).OrderBy(v => v.Id).ToList();

    public static bool IsThreatened(IGameSession session, Village village) =>
        session.State.Movements.Any(m => m.ToVillageId == village.Id
            && m.OwnerId != village.OwnerId
            && m.Type is MovementType.Attack or MovementType.Raid);

    public static string DescribeMovement(IGameSession session, Movement movement)
    {
        if (movement.OwnerId != session.LocalPlayerId)
            return L.T("movement.from", ("mission", MissionName(movement.Type)), ("owner", OwnerName(session, movement.OwnerId)));
        if (movement.Type == MovementType.Settle)
            return L.T("movement.settler");
        if (movement.Type == MovementType.Transfer)
            return L.T("movement.carrying", ("amount", Format.Total(movement.Loot)), ("target", session.State.Villages[movement.ToVillageId!.Value].Name));
        if (movement.Type == MovementType.CarrierReturn)
            return movement.Loot == Resources.Zero ? L.T("movement.carriers_returning") : L.T("movement.returning_with", ("amount", Format.Total(movement.Loot)));
        if (movement.Type != MovementType.Return)
            return L.T("movement.to", ("mission", MissionName(movement.Type)), ("target", session.State.Villages[movement.ToVillageId!.Value].Name));

        var loot = movement.Loot.Wood + movement.Loot.Stone + movement.Loot.Food;
        return loot > 0 ? L.T("movement.returning_loot", ("amount", Format.Amount(loot))) : L.T("movement.returning");
    }

    public static string OwnerName(IGameSession session, int ownerId) =>
        ownerId == Village.BarbarianOwnerId ? L.T("player.barbarians")
        : ownerId == session.LocalPlayerId ? L.T("player.you")
        : session.State.Players[ownerId].Name;

    public static bool LocalWon(IGameSession session, BattleReport report) =>
        report.AttackerId == session.LocalPlayerId ? report.AttackerWon : !report.AttackerWon;

    public static string ReportHeadline(IGameSession session, BattleReport report)
    {
        // A spectator is on neither side.
        if (report.AttackerId != session.LocalPlayerId && report.DefenderId != session.LocalPlayerId)
            return L.T(report.AttackerWon ? "report.headline_watched_won" : "report.headline_watched_lost", ("mission", MissionName(report.Type)), ("attacker", OwnerName(session, report.AttackerId)), ("target", OwnerName(session, report.DefenderId)));

        var result = L.T(LocalWon(session, report) ? "report.won" : "report.lost");
        var mission = MissionName(report.Type);
        return report.AttackerId == session.LocalPlayerId
            ? L.T("report.headline_attacker", ("result", result), ("mission", mission), ("target", OwnerName(session, report.DefenderId)))
            : L.T("report.headline_defender", ("result", result), ("mission", mission), ("attacker", OwnerName(session, report.AttackerId)));
    }

    public static string ScoutHeadline(IGameSession session, ScoutReport report)
    {
        var target = session.State.Villages[report.TargetVillageId].Name;
        return L.T(report.Succeeded ? "scout.headline_seen" : "scout.headline_lost", ("target", target));
    }

    public static string NoticeHeadline(IGameSession session, ScoutNotice notice)
    {
        var target = session.State.Villages[notice.TargetVillageId].Name;
        var sender = OwnerName(session, notice.SenderId);
        return L.T(notice.Caught ? "notice.caught" : "notice.seen", ("sender", sender), ("target", target));
    }

    public static string ScoutResultName(ScoutReport report) => L.T(report.Quality switch
    {
        ScoutQuality.Narrow => "scout.result_narrow",
        ScoutQuality.Clear => "scout.result_clear",
        ScoutQuality.Overwhelming => "scout.result_full",
        _ => "scout.result_lost",
    });

    public static string RangeText(ValueRange range) => Format.Range(range.Low, range.High);

    /// <summary>What the player last learned of a village under its current owner, and any failure since. Null if there is neither.</summary>
    public static string? LatestIntel(IGameSession session, Village target)
    {
        var reports = session.State.ScoutReports
            .Where(r => r.SenderId == session.LocalPlayerId && r.TargetVillageId == target.Id && r.DefenderId == target.OwnerId)
            .ToList();
        if (reports.Count == 0)
            return null;

        string Age(ScoutReport report) => Format.Time(session.State.ElapsedTime - report.Time);
        var seen = reports.LastOrDefault(r => r.Succeeded);
        var lost = reports[^1].Succeeded ? null : reports[^1];
        if (seen is null)
            return L.T("intel.lost_only", ("age", Age(lost!)));

        var intel = L.T("intel.seen", ("age", Age(seen)), ("seen", SeenText(seen)));
        return lost is null ? intel : L.T("intel.seen_then_lost", ("intel", intel), ("age", Age(lost)));
    }

    private static string SeenText(ScoutReport report)
    {
        if (report.Quality == ScoutQuality.Narrow)
            return L.T("intel.narrow", ("troops", RangeText(report.TroopRange!)), ("wall", RangeText(report.WallRange!)));

        var parts = new List<string> { L.N("intel.troops", report.Garrison!.Where(t => t.Key != UnitType.Scout).Sum(t => t.Value)) };
        if (report.WallLevel is > 0 and { } wall)
            parts.Add(L.T("intel.wall", ("level", Format.Whole(wall))));
        parts.Add(L.T("intel.resources", ("amount", Format.Total(report.Stock!.Value))));
        if (report.Loyalty is { } loyalty)
            parts.Add(L.T("intel.loyalty", ("value", Format.Whole(loyalty))));
        return string.Join(", ", parts);
    }

    public static string RejectionText(CommandRejection rejection) => L.T(L.Key("rejection", rejection));

    public static string ResourceName(ResourceType type) => L.T(L.Key("resource", type) + ".name");

    public static string ResourceAmount(ResourceType type, double amount) => L.T(L.Key("resource", type) + ".amount", ("amount", Format.Amount(amount)));

    /// <summary>Every resource with its amount, as in "5 wood, 3 stone, 2 grain".</summary>
    public static string ResourceList(Resources resources) =>
        string.Join(", ", Enum.GetValues<ResourceType>().Select(type => ResourceAmount(type, resources.Get(type))));

    public static string MissionName(MovementType type) => L.T(L.Key("mission", type));

    public static string TierName(IGameSession session, int tier) => L.T(L.Key("tier", session.Data.Map.Barbarians.Tiers[tier].Name));

    public static string ResourceIconPath(ResourceType type) => type switch
    {
        ResourceType.Wood => "res://assets/icons/wood.svg",
        ResourceType.Stone => "res://assets/icons/stone.svg",
        ResourceType.Food => "res://assets/icons/food.svg",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}

public static class Format
{
    /// <summary>Whole units, rounded down so the display never promises more than is available.</summary>
    public static string Amount(double value) => Mathf.FloorToInt(value).ToString(CultureInfo.InvariantCulture);

    public static string Total(Resources resources) => Amount(resources.Wood + resources.Stone + resources.Food);

    public static string Rate(double perSecond) => $"+{OneDecimal(perSecond)}/s";

    /// <summary>Rounded up, so a countdown reaches 0:00 only when the time is really over.</summary>
    public static string Time(double seconds)
    {
        var total = Math.Max(0, (int)Math.Ceiling(seconds));
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var rest = total % 60;
        return hours > 0 ? $"{Whole(hours)}:{Padded(minutes)}:{Padded(rest)}" : $"{Whole(minutes)}:{Padded(rest)}";
    }

    /// <summary>The limit shown after an amount, as in "120 / 800".</summary>
    public static string Limit(double value) => $"/ {Amount(value)}";

    public static string Times(int count) => $"× {Whole(count)}";

    public static string OutOf(int count, int total) => $"{Whole(count)} / {Whole(total)}";

    public static string Multiplier(double value) => $"{Trimmed(value)}x";

    /// <summary>A village name that needs attention.</summary>
    public static string Flagged(string name) => $"{name} !";

    public static string Range(int low, int high) => $"{Whole(low)}-{Whole(high)}";

    /// <summary>Parts of one line, with the separator the game uses everywhere.</summary>
    public static string Dotted(params string[] parts) => string.Join("  ·  ", parts);

    public static string Whole(double value) => value.ToString("0", CultureInfo.InvariantCulture);

    public static string OneDecimal(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>Up to two decimals, and none where the value has none.</summary>
    public static string Trimmed(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Padded(int value) => value.ToString("00", CultureInfo.InvariantCulture);
}
