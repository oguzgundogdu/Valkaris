using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Valkaris.Core.Commands;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

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
        || (movement.Type is not (MovementType.Transfer or MovementType.CarrierReturn or MovementType.Scout) && movement.ToVillageId is { } target && session.State.Villages[target].OwnerId == session.LocalPlayerId);

    /// <summary>The local player's villages, in the order they are shown and cycled through.</summary>
    public static IReadOnlyList<Village> OwnVillages(IGameSession session) =>
        session.State.Villages.Values.Where(v => v.OwnerId == session.LocalPlayerId).OrderBy(v => v.Id).ToList();

    public static bool IsThreatened(IGameSession session, Village village) =>
        session.State.Movements.Any(m => m.ToVillageId == village.Id
            && m.OwnerId != village.OwnerId
            && m.Type is MovementType.Attack or MovementType.Raid);

    public static string DescribeMovement(IGameSession session, Movement movement)
    {
        if (movement.OwnerId != session.LocalPlayerId)
            return $"{movement.Type} from {OwnerName(session, movement.OwnerId)}";
        if (movement.Type == MovementType.Settle)
            return "Settler › new village";
        if (movement.Type == MovementType.Transfer)
            return $"Carrying {Format.Total(movement.Loot)} › {session.State.Villages[movement.ToVillageId!.Value].Name}";
        if (movement.Type == MovementType.CarrierReturn)
            return movement.Loot == Resources.Zero ? "Carriers returning" : $"Returning with {Format.Total(movement.Loot)}";
        if (movement.Type != MovementType.Return)
            return $"{movement.Type} › {session.State.Villages[movement.ToVillageId!.Value].Name}";

        var loot = movement.Loot.Wood + movement.Loot.Stone + movement.Loot.Food;
        return loot > 0 ? $"Returning with {Format.Amount(loot)} loot" : "Returning";
    }

    public static string OwnerName(IGameSession session, int ownerId) =>
        ownerId == Village.BarbarianOwnerId ? L.T("player.barbarians")
        : ownerId == session.LocalPlayerId ? L.T("player.you")
        : session.State.Players[ownerId].Name;

    public static bool LocalWon(IGameSession session, BattleReport report) =>
        report.AttackerId == session.LocalPlayerId ? report.AttackerWon : !report.AttackerWon;

    public static string ReportHeadline(IGameSession session, BattleReport report)
    {
        var result = LocalWon(session, report) ? "Won" : "Lost";
        return report.AttackerId == session.LocalPlayerId
            ? $"{result}: {report.Type} on {OwnerName(session, report.DefenderId)}"
            : $"{result}: {report.Type} by {OwnerName(session, report.AttackerId)}";
    }

    public static string ScoutHeadline(IGameSession session, ScoutReport report)
    {
        var target = session.State.Villages[report.TargetVillageId].Name;
        return report.Succeeded ? $"Scouted {target}" : $"Scouts lost at {target}";
    }

    public static string NoticeHeadline(IGameSession session, ScoutNotice notice)
    {
        var target = session.State.Villages[notice.TargetVillageId].Name;
        var sender = OwnerName(session, notice.SenderId);
        return notice.Caught ? $"Caught scouts from {sender} at {target}" : $"{sender} scouted {target}";
    }

    public static string ScoutResultName(ScoutReport report) => report.Quality switch
    {
        ScoutQuality.Narrow => "Scouted: a rough look",
        ScoutQuality.Clear => "Scouted",
        ScoutQuality.Overwhelming => "Scouted in full",
        _ => "Scouts lost",
    };

    public static string RangeText(ValueRange range) => $"{range.Low}-{range.High}";

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
            return $"Scouts lost {Age(lost!)} ago: nothing seen";

        var intel = $"Scouted {Age(seen)} ago: {SeenText(seen)}";
        return lost is null ? intel : $"{intel}. Scouts lost {Age(lost)} ago";
    }

    private static string SeenText(ScoutReport report)
    {
        if (report.Quality == ScoutQuality.Narrow)
            return $"{RangeText(report.TroopRange!)} troops, Wall {RangeText(report.WallRange!)}";

        var wall = report.WallLevel > 0 ? $", Wall {report.WallLevel}" : "";
        var loyalty = report.Loyalty is { } value ? $", loyalty {Format.Whole(value)}" : "";
        return $"{report.Garrison!.Where(t => t.Key != UnitType.Scout).Sum(t => t.Value)} troops{wall}, {Format.Total(report.Stock!.Value)} resources{loyalty}";
    }

    public static string RejectionText(CommandRejection rejection) => rejection switch
    {
        CommandRejection.InvalidCount => "Choose how many.",
        CommandRejection.BarracksLevelTooLow => "Upgrade the Barracks to train this unit.",
        CommandRejection.InvalidMission => "That mission can't go to this target.",
        CommandRejection.NoTroopsSelected => "Choose some troops to send.",
        CommandRejection.NotEnoughTroops => "You don't have that many troops here.",
        CommandRejection.CatapultsCannotRaid => "Catapults can't go on a raid.",
        CommandRejection.SettlersCannotFight => "Settlers can't go on an attack or raid.",
        CommandRejection.ScoutsCannotFight => "Scouts can't go on an attack or raid.",
        CommandRejection.LordsCannotRaid => "Lords can't go on a raid.",
        CommandRejection.OnlyScoutsCanScout => "Only Scouts can scout.",
        CommandRejection.NoSettlerHere => "Train a Settler in this village first.",
        CommandRejection.TileOutsideMap => "That's outside the map.",
        CommandRejection.TileIsWater => "Villages can't be founded on water.",
        CommandRejection.TileTaken => "A village already stands here.",
        CommandRejection.TooCloseToVillage => "Too close to another village.",
        CommandRejection.VillageLimitReached => "Upgrade your capital's Main Building to found more villages.",
        CommandRejection.NoCapitalChoicePending => "There's no capital to choose right now.",
        CommandRejection.NoCapital => "Choose a new capital first.",
        CommandRejection.NotOwner => "That isn't your village.",
        CommandRejection.TargetNotYours => "Resources can only go to your own villages.",
        CommandRejection.SameVillage => "The resources are already in this village.",
        CommandRejection.NoResourcesSelected => "Choose some resources to send.",
        CommandRejection.NoMarketplace => "Build a Marketplace in this village first.",
        CommandRejection.NotEnoughCarriers => "Not enough free carriers for this load.",
        CommandRejection.InsufficientResources => "Not enough resources yet.",
        CommandRejection.QueueFull => "The queue is full.",
        CommandRejection.MainBuildingLevelTooLow => "Upgrade the Main Building to go higher.",
        CommandRejection.MaxLevelReached => "Maximum level reached.",
        CommandRejection.MatchOver => "The match is over.",
        CommandRejection.GamePaused => "The match is paused.",
        _ => "This can't be done right now.",
    };

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

    public static string Whole(double value) => value.ToString("0", CultureInfo.InvariantCulture);

    public static string OneDecimal(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>Up to two decimals, and none where the value has none.</summary>
    public static string Trimmed(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Padded(int value) => value.ToString("00", CultureInfo.InvariantCulture);
}
