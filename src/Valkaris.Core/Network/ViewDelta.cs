using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;

namespace Valkaris.Core.Network;

/// <summary>The host's side: the map goes once, and each report and notice goes once. Those lists only ever grow.</summary>
public sealed class ViewTrimmer
{
    private static readonly GameMapSnapshot NoMap = new(0, 0, Array.Empty<MapTileSnapshot>());

    private bool _sentMap;
    private int _reports;
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
                Reports = view.State.Reports.Skip(_reports).ToList(),
                ScoutReports = view.State.ScoutReports.Skip(_scoutReports).ToList(),
                ScoutNotices = view.State.ScoutNotices.Skip(_scoutNotices).ToList(),
            },
            Notices = view.Notices.Skip(_notices).ToList(),
        };

        _sentMap = true;
        _reports = view.State.Reports.Count;
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

    public PlayerViewSnapshot Merge(PlayerViewSnapshot trimmed)
    {
        _map ??= trimmed.State.Map;
        _reports.AddRange(trimmed.State.Reports);
        _scoutReports.AddRange(trimmed.State.ScoutReports);
        _scoutNotices.AddRange(trimmed.State.ScoutNotices);
        _notices.AddRange(trimmed.Notices);

        return trimmed with
        {
            State = trimmed.State with { Map = _map, Reports = _reports, ScoutReports = _scoutReports, ScoutNotices = _scoutNotices },
            Notices = _notices,
        };
    }
}
