namespace Valkaris.Core.Model;

public sealed class MapTile
{
    public int? VillageId { get; internal set; }
}

public sealed class GameMap
{
    private readonly MapTile[] _tiles;

    internal GameMap(int width, int height)
    {
        Width = width;
        Height = height;
        _tiles = new MapTile[width * height];
        for (var i = 0; i < _tiles.Length; i++)
            _tiles[i] = new MapTile();
        Tiles = Array.AsReadOnly(_tiles);
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Row-major: index = y * Width + x.</summary>
    public IReadOnlyList<MapTile> Tiles { get; }

    public bool Contains(TileCoord coord) =>
        coord.X >= 0 && coord.X < Width && coord.Y >= 0 && coord.Y < Height;

    public MapTile TileAt(TileCoord coord)
    {
        if (!Contains(coord))
            throw new ArgumentOutOfRangeException(nameof(coord), coord, "Outside the map.");
        return _tiles[coord.Y * Width + coord.X];
    }
}
