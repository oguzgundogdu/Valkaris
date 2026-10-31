using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Valkaris.Core.Commands;
using Valkaris.Core.Hosting;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>Display names, descriptions and icons. Presentation content, not balance.</summary>
public sealed record BuildingInfo(string Name, string Description, string IconPath);

public sealed record UnitInfo(string Name, string IconPath);

public static class UiContent
{
    public static readonly Color Danger = new(0.898f, 0.439f, 0.345f);
    public static readonly Color Warning = new(0.961f, 0.682f, 0.243f);
    public static readonly Color Text = new(0.941f, 0.902f, 0.839f);

    public static readonly IReadOnlyDictionary<BuildingType, BuildingInfo> Buildings = new Dictionary<BuildingType, BuildingInfo>
    {
        [BuildingType.MainBuilding] = new("Main Building", "The heart of the village. Its level sets how far other buildings can grow.", "res://assets/icons/main_building.svg"),
        [BuildingType.Woodcutter] = new("Woodcutter", "Produces wood.", "res://assets/icons/woodcutter.svg"),
        [BuildingType.Quarry] = new("Quarry", "Produces stone.", "res://assets/icons/quarry.svg"),
        [BuildingType.Farm] = new("Farm", "Produces food, the main cost of training troops.", "res://assets/icons/farm.svg"),
        [BuildingType.Warehouse] = new("Warehouse", "Sets how much of each resource the village can hold.", "res://assets/icons/warehouse.svg"),
        [BuildingType.Barracks] = new("Barracks", "Trains troops. Higher levels train faster and unlock stronger units.", "res://assets/icons/barracks.svg"),
        [BuildingType.Wall] = new("Wall", "Raises the defence of everyone stationed in the village.", "res://assets/icons/wall.svg"),
        [BuildingType.Marketplace] = new("Marketplace", "Its carriers move resources to your other villages. Only the sending village needs one.", "res://assets/icons/marketplace.svg"),
        [BuildingType.Watchtower] = new("Watchtower", "Strengthens the Scouts at home, and warns you of any scouting attempt.", "res://assets/icons/watchtower.svg"),
    };

    public static readonly IReadOnlyDictionary<UnitType, UnitInfo> Units = new Dictionary<UnitType, UnitInfo>
    {
        [UnitType.Spearman] = new("Spearman", "res://assets/icons/spearman.svg"),
        [UnitType.Swordsman] = new("Swordsman", "res://assets/icons/swordsman.svg"),
        [UnitType.Cavalry] = new("Cavalry", "res://assets/icons/cavalry.svg"),
        [UnitType.Catapult] = new("Catapult", "res://assets/icons/catapult.svg"),
        [UnitType.Settler] = new("Settler", "res://assets/icons/settler.svg"),
        [UnitType.Lord] = new("Lord", "res://assets/icons/lord.svg"),
        [UnitType.Scout] = new("Scout", "res://assets/icons/scout.svg"),
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
            return "Settler → new village";
        if (movement.Type == MovementType.Transfer)
            return $"Carrying {Format.Total(movement.Loot)} → {session.State.Villages[movement.ToVillageId!.Value].Name}";
        if (movement.Type == MovementType.CarrierReturn)
            return movement.Loot == Resources.Zero ? "Carriers returning" : $"Returning with {Format.Total(movement.Loot)}";
        if (movement.Type != MovementType.Return)
            return $"{movement.Type} → {session.State.Villages[movement.ToVillageId!.Value].Name}";

        var loot = movement.Loot.Wood + movement.Loot.Stone + movement.Loot.Food;
        return loot > 0 ? $"Returning with {Format.Amount(loot)} loot" : "Returning";
    }

    public static string OwnerName(IGameSession session, int ownerId) =>
        ownerId == Village.BarbarianOwnerId ? "Barbarians" : session.State.Players[ownerId].Name;

    public static bool LocalWon(IGameSession session, BattleReport report) =>
        report.AttackerId == session.LocalPlayerId ? report.AttackerWon : !report.AttackerWon;

    public static string ReportHeadline(IGameSession session, BattleReport report)
    {
        var result = LocalWon(session, report) ? "Won" : "Lost";
        return report.AttackerId == session.LocalPlayerId
            ? $"{result}: {report.Type} on {OwnerName(session, report.DefenderId)}"
            : $"{result}: {report.Type} by {OwnerName(session, report.AttackerId)}";
    }

    public static bool LocalLostScouts(IGameSession session, ScoutReport report) =>
        !report.Succeeded && report.SenderId == session.LocalPlayerId;

    public static string ScoutHeadline(IGameSession session, ScoutReport report)
    {
        var target = session.State.Villages[report.TargetVillageId].Name;
        return report.SenderId != session.LocalPlayerId ? $"Caught scouts from {OwnerName(session, report.SenderId)} at {target}"
            : report.Succeeded ? $"Scouted {target}"
            : $"Scouts lost at {target}";
    }

    /// <summary>The player's latest scouting of a village under its current owner. Null if there is none.</summary>
    public static string? LatestIntel(IGameSession session, Village target)
    {
        var report = session.State.ScoutReports.LastOrDefault(r =>
            r.SenderId == session.LocalPlayerId && r.TargetVillageId == target.Id && r.DefenderId == target.OwnerId);
        if (report is null)
            return null;

        var age = Format.Time(session.State.ElapsedTime - report.Time);
        if (!report.Succeeded)
            return $"Scouts caught {age} ago: more than {report.Sent.Values.Sum() * session.Data.Combat.TroopsPerScout} troops";

        var wall = report.WallLevel > 0 ? $", Wall {report.WallLevel}" : "";
        return $"Scouted {age} ago: {report.Garrison!.Values.Sum()} troops{wall}, {Format.Total(report.Stock!.Value)} resources";
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
        CommandRejection.OnlyFightersCanScout => "Only Spearmen, Swordsmen and Cavalry can scout.",
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
    public static string Amount(double value) => Mathf.FloorToInt(value).ToString();

    public static string Total(Resources resources) => Amount(resources.Wood + resources.Stone + resources.Food);

    public static string Rate(double perSecond) => $"+{perSecond:0.0}/s";

    /// <summary>Rounded up, so a countdown reaches 0:00 only when the time is really over.</summary>
    public static string Time(double seconds)
    {
        var total = Math.Max(0, (int)Math.Ceiling(seconds));
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var rest = total % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{rest:00}" : $"{minutes}:{rest:00}";
    }
}
