using Valkaris.Core.Model;
using Valkaris.Core.Snapshots;

namespace Valkaris.Core.Tests;

/// <summary>Whole matches with real AI on every seat.</summary>
public class AiMatchTests
{
    private static readonly AiDifficulty[] Mixed = { AiDifficulty.Easy, AiDifficulty.Normal, AiDifficulty.Hard, AiDifficulty.Hard };

    private static AiMatch Played(double step, ulong seed = 3)
    {
        var match = new AiMatch(Mixed, seed: seed, minutes: 30, speed: 3);
        match.Run(30 * 60, step);
        return match;
    }

    [Fact]
    public void AMatchEndsWithoutRejectionsOrErrors()
    {
        var match = Played(5);

        Assert.True(match.State.IsMatchOver);
        Assert.Equal(0, match.RejectedCommands);
    }

    [Theory]
    [InlineData(600, 1)]
    [InlineData(1, 1)]
    [InlineData(0.25, 1)]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    [InlineData(1, 4)]
    [InlineData(1, 5)]
    [InlineData(0.25, 3)]
    public void TheStepSizeDoesNotChangeTheMatch(double step, ulong seed)
    {
        var expected = Played(5, seed: seed);
        var actual = Played(step, seed: seed);

        Assert.Equal(expected.State.Reports.Count, actual.State.Reports.Count);
        Assert.Equal(expected.State.Villages.Count, actual.State.Villages.Count);
        Assert.Equal(expected.State.Outcome!.WinnerIds, actual.State.Outcome!.WinnerIds);
        foreach (var village in expected.State.Villages.Values)
        {
            var other = actual.State.Villages[village.Id];
            Assert.Equal(village.OwnerId, other.OwnerId);
            Assert.Equal(village.Resources.Wood, other.Resources.Wood, 3);
            Assert.Equal(village.Resources.Food, other.Resources.Food, 3);
            Assert.Equal(village.Troops.Where(t => t.Value != 0).OrderBy(t => t.Key), other.Troops.Where(t => t.Value != 0).OrderBy(t => t.Key));
        }
    }

    [Fact]
    public void TheSameSeedPlaysTheSameMatch()
    {
        var first = SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(Played(5).State));
        var second = SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(Played(5).State));

        Assert.Equal(first, second);
    }

    [Fact]
    public void ADifferentSeedPlaysADifferentMatch()
    {
        Assert.NotEqual(
            SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(Played(5, seed: 3).State)),
            SnapshotMapper.ToJson(SnapshotMapper.ToSnapshot(Played(5, seed: 4).State)));
    }

    [Fact]
    public void AnAiMatchLeavesTheOtherPlayersVillagesAlone()
    {
        var match = Played(5);

        foreach (var view in match.Views.Values)
            Assert.All(view.OwnVillages, v => Assert.Equal(view.PlayerId, v.OwnerId));
    }
}
