using Valkaris.Core.Model;

namespace Valkaris.Core.Systems;

/// <summary>One line of news: the same battle result between the same villages, however often it happened.</summary>
public sealed record NewsLine(string FromVillageName, int TargetVillageId, string TargetVillageName, bool AttackerWon, int Count, double LatestTime, bool Faded);

/// <summary>Turns the list of public notices into something readable. Display only: nothing here changes what anyone knows.</summary>
public static class NewsFeed
{
    public const int MaxLines = 50;
    public const double FadeAfterSeconds = 300;

    /// <summary>Newest first. The filter, if any, picks which target villages to keep.</summary>
    public static IReadOnlyList<NewsLine> Build(IEnumerable<PublicNotice> notices, double now, Func<int, bool>? keepTarget = null) =>
        notices
            .Where(n => keepTarget is null || keepTarget(n.TargetVillageId))
            .GroupBy(n => (n.FromVillageName, n.TargetVillageId, n.AttackerWon))
            .Select(group =>
            {
                var latest = group.MaxBy(n => n.Time)!;
                return new NewsLine(latest.FromVillageName, latest.TargetVillageId, latest.TargetVillageName, latest.AttackerWon, group.Count(), latest.Time, now - latest.Time > FadeAfterSeconds);
            })
            .OrderByDescending(line => line.LatestTime)
            .Take(MaxLines)
            .ToList();
}
