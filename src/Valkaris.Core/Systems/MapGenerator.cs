using Valkaris.Core.Data;
using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>Builds the terrain and places villages. Everything comes from the state's seeded generator.</summary>
internal static class MapGenerator
{
    public static void GenerateTerrain(GameState state, GameData data)
    {
        var config = data.Map.Terrain;
        var map = state.Map;
        var elevation = new ValueNoise(state, map.Width, map.Height, config.NoiseCellSize);
        var moisture = new ValueNoise(state, map.Width, map.Height, config.NoiseCellSize);

        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var height = elevation.At(x, y);
                map.TileAt(new TileCoord(x, y)).Terrain =
                    height < config.WaterBelow ? Terrain.Water
                    : height > config.HillsAbove ? Terrain.Hills
                    : moisture.At(x, y) > config.ForestAbove ? Terrain.Forest
                    : Terrain.Grass;
            }
        }
    }

    /// <summary>One player per quadrant, shifted slightly by the seed. The tiles around it become land.</summary>
    public static TileCoord PlayerStart(GameState state, GameData data, int index)
    {
        var map = state.Map;
        var left = map.Width / 4;
        var right = map.Width * 3 / 4;
        var top = map.Height / 4;
        var bottom = map.Height * 3 / 4;
        var (x, y) = index switch
        {
            0 => (left, top),
            1 => (right, bottom),
            2 => (right, top),
            _ => (left, bottom),
        };

        var jitter = data.Map.StartJitter;
        var start = new TileCoord(
            Math.Clamp(x + state.NextRandom(2 * jitter + 1) - jitter, 0, map.Width - 1),
            Math.Clamp(y + state.NextRandom(2 * jitter + 1) - jitter, 0, map.Height - 1));

        ClearAround(map, start, data.Map.StartClearRadius);
        return start;
    }

    private static void ClearAround(GameMap map, TileCoord centre, int radius)
    {
        for (var y = centre.Y - radius; y <= centre.Y + radius; y++)
        {
            for (var x = centre.X - radius; x <= centre.X + radius; x++)
            {
                var coord = new TileCoord(x, y);
                if (map.Contains(coord) && map.TileAt(coord).Terrain == Terrain.Water)
                    map.TileAt(coord).Terrain = Terrain.Grass;
            }
        }
    }

    public static void PlaceBarbarians(GameState state, GameData data)
    {
        var count = data.Map.BarbarianCounts[state.Settings.MapSize];
        var spots = FindBarbarianSpots(state, count, data.Map.MinVillageSpacing);
        var names = Shuffled(state, data.Map.BarbarianVillageNames);

        for (var i = 0; i < spots.Count; i++)
        {
            var tierIndex = PickTier(state, data.Map.Barbarians.Tiers);
            var tier = data.Map.Barbarians.Tiers[tierIndex];

            var village = new Village(state.Villages.Keys.Max() + 1, Village.BarbarianOwnerId, spots[i])
            {
                Name = $"{names[i]} {tier.Name}",
                BarbarianTier = tierIndex,
                Resources = tier.Resources,
            };
            foreach (var (type, level) in tier.Buildings)
                village.SetBuildingLevel(type, level);
            foreach (var (unit, troops) in tier.Garrison)
                village.AddTroops(unit, troops);

            state.Map.TileAt(spots[i]).VillageId = village.Id;
            state.AddVillage(village);
        }
    }

    /// <summary>Keeps the wanted spacing where it can, and relaxes it if the seed's land can't fit them all.</summary>
    private static List<TileCoord> FindBarbarianSpots(GameState state, int count, int spacing)
    {
        var map = state.Map;
        var land = new List<TileCoord>();
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var tile = map.TileAt(new TileCoord(x, y));
                if (tile.Terrain != Terrain.Water && tile.VillageId is null)
                    land.Add(new TileCoord(x, y));
            }
        }
        land = Shuffled(state, land);

        var taken = state.Villages.Values.Select(v => v.Position).ToList();
        for (var wanted = spacing; wanted >= 1; wanted--)
        {
            var spots = new List<TileCoord>();
            foreach (var coord in land)
            {
                if (spots.Count == count)
                    break;
                if (taken.Concat(spots).All(other => UnitRules.Distance(coord, other) >= wanted))
                    spots.Add(coord);
            }
            if (spots.Count == count)
                return spots;
        }

        throw new ArgumentException($"The map has too little land for {count} barbarian villages.");
    }

    private static int PickTier(GameState state, IReadOnlyList<BarbarianTierConfig> tiers)
    {
        var roll = state.NextRandom(tiers.Sum(t => t.Weight));
        for (var i = 0; i < tiers.Count; i++)
        {
            roll -= tiers[i].Weight;
            if (roll < 0)
                return i;
        }
        return tiers.Count - 1;
    }

    public static List<T> Shuffled<T>(GameState state, IEnumerable<T> items)
    {
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = state.NextRandom(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    /// <summary>Random values on a coarse lattice, blended smoothly between its points. Values run from 0 to 1.</summary>
    private sealed class ValueNoise
    {
        private readonly double[,] _lattice;
        private readonly int _cell;

        public ValueNoise(GameState state, int width, int height, int cell)
        {
            _cell = cell;
            _lattice = new double[width / cell + 2, height / cell + 2];
            for (var y = 0; y < _lattice.GetLength(1); y++)
            {
                for (var x = 0; x < _lattice.GetLength(0); x++)
                    _lattice[x, y] = state.NextRandom() / (double)ulong.MaxValue;
            }
        }

        public double At(int x, int y)
        {
            var cellX = x / _cell;
            var cellY = y / _cell;
            var u = Smooth((double)(x % _cell) / _cell);
            var v = Smooth((double)(y % _cell) / _cell);

            var top = Lerp(_lattice[cellX, cellY], _lattice[cellX + 1, cellY], u);
            var bottom = Lerp(_lattice[cellX, cellY + 1], _lattice[cellX + 1, cellY + 1], u);
            return Lerp(top, bottom, v);
        }

        private static double Smooth(double t) => t * t * (3 - 2 * t);

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
