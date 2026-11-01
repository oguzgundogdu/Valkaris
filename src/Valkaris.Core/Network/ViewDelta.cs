using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;

namespace Valkaris.Core.Network;

/// <summary>The host's side: the terrain goes once, and each report and notice goes once. Notices only grow at the end, but older battle reports can appear for a new spectator.</summary>
public sealed class ViewTrimmer
{
    private static readonly GameMapSnapshot NoMap = new(0, 0, Array.Empty<MapTileSnapshot>());

    private bool _sentMap;
    private readonly HashSet<int> _reports = new();
    private int _scoutReports;
    private int _scoutNotices;
    private int _notices;

    public PlayerViewSnapshot Trim(PlayerViewSnapshot view)
    {
        var trimmed = view with
        {
            State = view.State with
            {
                Map = _sentMap ? NoMap : view.State.Map,
                Reports = view.State.Reports.Where(r => _reports.Add(r.Id)).ToList(),
                ScoutReports = view.State.ScoutReports.Skip(_scoutReports).ToList(),
                ScoutNotices = view.State.ScoutNotices.Skip(_scoutNotices).ToList(),
            },
            Notices = view.Notices.Skip(_notices).ToList(),
        };

        _sentMap = true;
        _scoutReports = view.State.ScoutReports.Count;
        _scoutNotices = view.State.ScoutNotices.Count;
        _notices = view.Notices.Count;
        return trimmed;
    }
}

/// <summary>The client's side: puts back what ViewTrimmer left out.</summary>
public sealed class ViewMerger
{
    private readonly List<BattleReport> _reports = new();
    private readonly List<ScoutReport> _scoutReports = new();
    private readonly List<ScoutNotice> _scoutNotices = new();
    private readonly List<PublicNotice> _notices = new();
    private GameMapSnapshot? _map;
    private int _villagesOnMap;

    public PlayerViewSnapshot Merge(PlayerViewSnapshot trimmed)
    {
        _map ??= trimmed.State.Map;
        PlaceNewVillages(trimmed.State.Villages);
        if (trimmed.State.Reports.Count > 0)
        {
            _reports.AddRange(trimmed.State.Reports);
            _reports.Sort((a, b) => a.Id.CompareTo(b.Id));
        }
        _scoutReports.AddRange(trimmed.State.ScoutReports);
        _scoutNotices.AddRange(trimmed.State.ScoutNotices);
        _notices.AddRange(trimmed.Notices);

        return trimmed with
        {
            State = trimmed.State with { Map = _map, Reports = _reports, ScoutReports = _scoutReports, ScoutNotices = _scoutNotices },
            Notices = _notices,
        };
    }

    /// <summary>Terrain never changes, but a founded village takes a tile. Villages are never removed, only added.</summary>
    private void PlaceNewVillages(IReadOnlyList<VillageSnapshot> villages)
    {
        if (villages.Count == _villagesOnMap)
            return;

        var tiles = _map!.Tiles.ToArray();
        foreach (var village in villages)
        {
            var index = village.Position.Y * _map.Width + village.Position.X;
            tiles[index] = tiles[index] with { VillageId = village.Id };
        }
        _map = _map with { Tiles = tiles };
        _villagesOnMap = villages.Count;
    }
}
