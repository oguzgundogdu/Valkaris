using System;
using System.Collections.Generic;
using Godot;
using Valkaris.Core.Commands;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>Display names, descriptions and icons. Presentation content, not balance.</summary>
public sealed record BuildingInfo(string Name, string Description, string IconPath);

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
        [BuildingType.Barracks] = new("Barracks", "Will train troops. Training arrives in a later milestone.", "res://assets/icons/barracks.svg"),
        [BuildingType.Wall] = new("Wall", "Will strengthen the village's defence. Combat arrives in a later milestone.", "res://assets/icons/wall.svg"),
    };

    public static string RejectionText(CommandRejection rejection) => rejection switch
    {
        CommandRejection.InsufficientResources => "Not enough resources yet.",
        CommandRejection.QueueFull => "The build queue is full.",
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
