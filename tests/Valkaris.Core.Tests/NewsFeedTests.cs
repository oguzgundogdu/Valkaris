using Valkaris.Core.Model;
using Valkaris.Core.Systems;

namespace Valkaris.Core.Tests;

public class NewsFeedTests
{
    private static PublicNotice Notice(double time, string from = "A", int target = 1, bool won = true) => new(time, from, target, $"V{target}", won);

    [Fact]
    public void RepeatsMergeWithTheLatestTime()
    {
        var line = NewsFeed.Build(new[] { Notice(10), Notice(20), Notice(30), Notice(15) }, 40).Single();

        Assert.Equal(4, line.Count);
        Assert.Equal(30, line.LatestTime);
    }

    [Fact]
    public void ADifferentResultOrVillageIsItsOwnLine()
    {
        var lines = NewsFeed.Build(new[] { Notice(10), Notice(20, won: false), Notice(30, from: "B"), Notice(40, target: 2) }, 50);

        Assert.Equal(4, lines.Count);
    }

    [Fact]
    public void NewestComesFirst()
    {
        var lines = NewsFeed.Build(new[] { Notice(10, from: "A"), Notice(30, from: "B"), Notice(20, from: "C") }, 50);

        Assert.Equal(new[] { "B", "C", "A" }, lines.Select(l => l.FromVillageName));
    }

    [Fact]
    public void OldNewsFades()
    {
        var lines = NewsFeed.Build(new[] { Notice(0, from: "Old"), Notice(100, from: "New") }, NewsFeed.FadeAfterSeconds + 1);

        Assert.True(lines.Single(l => l.FromVillageName == "Old").Faded);
        Assert.False(lines.Single(l => l.FromVillageName == "New").Faded);
    }

    [Fact]
    public void AtMostFiftyLinesAreKept()
    {
        var notices = Enumerable.Range(0, 80).Select(i => Notice(i, from: $"V{i}")).ToList();
        var lines = NewsFeed.Build(notices, 100);

        Assert.Equal(NewsFeed.MaxLines, lines.Count);
        Assert.Equal("V79", lines[0].FromVillageName);
    }

    [Fact]
    public void TheFilterPicksTargets()
    {
        var lines = NewsFeed.Build(new[] { Notice(1, target: 1), Notice(2, target: 2) }, 5, id => id == 2);

        Assert.Equal(2, lines.Single().TargetVillageId);
    }

    [Fact]
    public void ASimulatedBattleCarriesItsWinnerIntoTheNews()
    {
        var match = new AiMatch(new[] { AiDifficulty.Hard, AiDifficulty.Hard, AiDifficulty.Hard, AiDifficulty.Hard }, seed: 5, minutes: 40, speed: 10, size: MapSize.Medium);
        match.Run(40 * 60);

        var battles = match.State.Reports.Where(r => r.Type is MovementType.Attack or MovementType.Raid && r.DefenderId > 0).ToList();
        Assert.NotEmpty(battles);
        foreach (var report in battles)
        {
            var arrival = match.State.BattleArrivals.First(a => a.Time == report.Time && a.TargetVillageId == report.DefenderVillageId);
            Assert.Equal(report.AttackerWon, arrival.AttackerWon);
        }
    }
}
