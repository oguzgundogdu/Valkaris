using System;
using System.Collections.Generic;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>What the village looks like at a given size. Visual choices, not balance, and free of Godot so tests can run them.</summary>
public static class VillageArt
{
    public const int StageCount = 3;

    // Where the render tool puts the middle of a building's ground on its canvas. See tools/art.
    public const float SpriteCanvas = 512;
    public const float SpriteGroundX = 256;
    public const float SpriteGroundY = 373;
    public const float SpriteScale = 0.66f;

    public const string PlotSprite = "res://assets/village/plot.png";
    public const string ScaffoldSprite = "res://assets/village/scaffold.png";

    private static readonly IReadOnlyDictionary<BuildingType, string[]> Sprites = new Dictionary<BuildingType, string[]>
    {
        [BuildingType.MainBuilding] = new[] { "res://assets/village/main_1.png", "res://assets/village/main_2.png", "res://assets/village/main_3.png" },
        [BuildingType.Woodcutter] = new[] { "res://assets/village/woodcutter_1.png", "res://assets/village/woodcutter_2.png", "res://assets/village/woodcutter_3.png" },
        [BuildingType.Quarry] = new[] { "res://assets/village/quarry_1.png", "res://assets/village/quarry_2.png", "res://assets/village/quarry_3.png" },
        [BuildingType.Farm] = new[] { "res://assets/village/farm_1.png", "res://assets/village/farm_2.png", "res://assets/village/farm_3.png" },
        [BuildingType.Warehouse] = new[] { "res://assets/village/warehouse_1.png", "res://assets/village/warehouse_2.png", "res://assets/village/warehouse_3.png" },
        [BuildingType.Barracks] = new[] { "res://assets/village/barracks_1.png", "res://assets/village/barracks_2.png", "res://assets/village/barracks_3.png" },
        [BuildingType.Wall] = new[] { "res://assets/village/wall_1.png", "res://assets/village/wall_2.png", "res://assets/village/wall_3.png" },
        [BuildingType.Marketplace] = new[] { "res://assets/village/marketplace_1.png", "res://assets/village/marketplace_2.png", "res://assets/village/marketplace_3.png" },
        [BuildingType.Watchtower] = new[] { "res://assets/village/watchtower_1.png", "res://assets/village/watchtower_2.png", "res://assets/village/watchtower_3.png" },
    };

    public static string Sprite(BuildingType building, int stage) => stage == 0 ? PlotSprite : Sprites[building][stage - 1];

    /// <summary>Plot centres as fractions of the ground, so the layout follows the window size. The same in every village.</summary>
    public static readonly IReadOnlyDictionary<BuildingType, (float X, float Y)> Plots = new Dictionary<BuildingType, (float X, float Y)>
    {
        [BuildingType.MainBuilding] = (0.5f, 0.47f),
        [BuildingType.Woodcutter] = (0.16f, 0.22f),
        [BuildingType.Marketplace] = (0.5f, 0.16f),
        [BuildingType.Quarry] = (0.84f, 0.22f),
        [BuildingType.Farm] = (0.17f, 0.52f),
        [BuildingType.Watchtower] = (0.85f, 0.54f),
        [BuildingType.Warehouse] = (0.27f, 0.82f),
        [BuildingType.Barracks] = (0.5f, 0.8f),
        [BuildingType.Wall] = (0.73f, 0.82f),
    };

    /// <summary>0 is an empty plot. Otherwise a third of the levels each way: for 10 levels, 1-3, 4-7 and 8-10.</summary>
    public static int Stage(int level, int maxLevel)
    {
        if (level <= 0)
            return 0;

        var third = Math.Max(1, maxLevel / StageCount);
        return level <= third ? 1 : level > maxLevel - third ? StageCount : 2;
    }

    /// <summary>The same for a village every time it is drawn, and separate from the match's own random numbers.</summary>
    public static int Seed(ulong matchSeed, int villageId) => unchecked((int)(matchSeed & 0x7FFFFFFF) ^ (villageId * 7919));
}
