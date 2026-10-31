using System;
using System.Collections.Generic;
using Valkaris.Core.Model;

namespace Valkaris.Presentation;

/// <summary>What the village looks like at a given size. Visual choices, not balance, and free of Godot so tests can run them.</summary>
public static class VillageArt
{
    public const int StageCount = 3;

    /// <summary>Plot centres as fractions of the ground, so the layout follows the window size. The same in every village.</summary>
    public static readonly IReadOnlyDictionary<BuildingType, (float X, float Y)> Plots = new Dictionary<BuildingType, (float X, float Y)>
    {
        [BuildingType.MainBuilding] = (0.5f, 0.47f),
        [BuildingType.Woodcutter] = (0.16f, 0.22f),
        [BuildingType.Watchtower] = (0.5f, 0.16f),
        [BuildingType.Quarry] = (0.84f, 0.22f),
        [BuildingType.Farm] = (0.13f, 0.52f),
        [BuildingType.Marketplace] = (0.87f, 0.52f),
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
